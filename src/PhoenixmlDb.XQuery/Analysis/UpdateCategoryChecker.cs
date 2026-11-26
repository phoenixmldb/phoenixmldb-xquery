using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Analysis;

/// <summary>
/// Classifies every expression as updating or simple and reports an updating expression where
/// the category rules of XQuery Update Facility 3.0 do not allow one (XUST0001).
/// </summary>
/// <remarks>
/// <para>
/// An updating expression is an insert, delete, replace or rename expression, a static call of an
/// updating function, or an expression that directly contains an updating expression (other than
/// the modify clause of a copy modify expression) (§2.2). Most expressions take the category of
/// their operands, which may be of any category: the comma, the branches of a conditional,
/// typeswitch, switch and try/catch, the return clause of a FLWOR, node constructors, function
/// arguments, the clauses of copy modify (§5, §7.1-7.12). These must be simple: the clauses of a
/// FLWOR other than return (§7.1), the test of a conditional, typeswitch or switch (§7.2-7.4), the
/// value of a variable or context item declaration (§4.3, §4.4), the body of a function not declared
/// %updating (§4.2) and of an inline function declared %simple (§7.9), and an operand of any other
/// expression (§7.13).
/// </para>
/// <para>
/// XQuery Update 1.0 had stricter rules (a comma or the branches of a conditional could not mix the
/// categories, a modify clause had to be updating); XQUTS 1.0.1 tests those. This class implements
/// 3.0.
/// </para>
/// </remarks>
internal sealed class UpdateCategoryChecker : XQueryExpressionWalker
{
    private readonly List<AnalysisError> _errors;
    private readonly HashSet<(QName Name, int Arity)> _updatingFunctions = [];
    private readonly Stack<Frame> _frames = new();
    private bool _lastUpdating;

    // The expression being classified: whether one of the operands walked under it is updating
    // (the first such operand, for the error location), and its category once a rule has set it.
    private sealed class Frame
    {
        public XQueryExpression? FirstUpdatingOperand;
        public bool? Category;
    }

    private UpdateCategoryChecker(List<AnalysisError> errors) => _errors = errors;

    /// <summary>
    /// Checks a main or library module; errors are added to <paramref name="errors"/>. Returns whether
    /// the body of the module (or the expression, when it is not a module) is an updating expression.
    /// </summary>
    public static bool Check(XQueryExpression expression, List<AnalysisError> errors)
    {
        var checker = new UpdateCategoryChecker(errors);
        // fn:put is an updating function (XQUF 3.0 §6.1).
        checker._updatingFunctions.Add((Functions.PutFunction.PutName, 2));
        checker._updatingFunctions.Add((Functions.PutFunction.PutName, 3));
        if (expression is ModuleExpression module)
            foreach (var decl in module.Declarations)
                if (decl is FunctionDeclarationExpression { IsUpdating: true } f)
                    checker._updatingFunctions.Add((f.Name, f.Parameters.Count));
        checker.Walk(expression);
        return expression is ModuleExpression ? checker._bodyIsUpdating : checker._lastUpdating;
    }

    // The category of the body of the module walked.
    private bool _bodyIsUpdating;

    public override void Walk(XQueryExpression expr)
    {
        var frame = new Frame();
        _frames.Push(frame);
        try
        {
            base.Walk(expr);
        }
        finally
        {
            _frames.Pop();
        }

        // An expression with no rule of its own is one of the "other expressions" (§7.13): simple,
        // and none of its operands may be updating.
        var updating = frame.Category ?? OtherExpression(frame);
        if (updating && _frames.TryPeek(out var parent))
            parent.FirstUpdatingOperand ??= expr;
        _lastUpdating = updating;
    }

    private bool OtherExpression(Frame frame)
    {
        if (frame.FirstUpdatingOperand is { } operand)
            Report(operand, "an operand of an expression that is not an updating expression");
        return false;
    }

    // The category of an operand; errors inside it are reported.
    private bool Category(XQueryExpression expr)
    {
        Walk(expr);
        return _lastUpdating;
    }

    // An operand that must be a simple expression.
    private void Simple(XQueryExpression? expr, string where)
    {
        if (expr != null && Category(expr))
            Report(expr, where);
    }

    private object? Rule(bool updating)
    {
        _frames.Peek().Category = updating;
        return null;
    }

    // The category is that of the operands: updating when one of them is (node constructors).
    private object? RuleByOperands() => Rule(_frames.Peek().FirstUpdatingOperand != null);

    private void Report(XQueryExpression expr, string where)
        => _errors.Add(new AnalysisError("XUST0001",
            $"XUST0001: An updating expression is not allowed as {where}", expr.Location));

    // ---- §4 Prolog ----

    public override object? VisitModuleExpression(ModuleExpression expr)
    {
        foreach (var decl in expr.Declarations)
            Walk(decl);
        _bodyIsUpdating = Category(expr.Body);
        return Rule(false);
    }

    public override object? VisitFunctionDeclaration(FunctionDeclarationExpression expr)
    {
        // §4.2: an updating function may have a body of any category; a simple one may not.
        var body = Category(expr.Body);
        if (body && !expr.IsUpdating && !expr.IsExternal)
            Report(expr.Body, $"the body of {expr.Name}#{expr.Parameters.Count}, a function not declared %updating");
        return Rule(false);
    }

    public override object? VisitVariableDeclaration(VariableDeclarationExpression expr)
    {
        Simple(expr.Value, $"the value of variable ${expr.Name}");
        return Rule(false);
    }

    public override object? VisitContextItemDeclaration(ContextItemDeclarationExpression expr)
    {
        Simple(expr.DefaultValue, "the value of the context item declaration");
        return Rule(false);
    }

    // ---- §5 Basic updating expressions and copy modify: operands of any category ----

    public override object? VisitInsertExpression(InsertExpression expr)
    {
        base.VisitInsertExpression(expr);
        return Rule(true);
    }

    public override object? VisitDeleteExpression(DeleteExpression expr)
    {
        base.VisitDeleteExpression(expr);
        return Rule(true);
    }

    public override object? VisitReplaceNodeExpression(ReplaceNodeExpression expr)
    {
        base.VisitReplaceNodeExpression(expr);
        return Rule(true);
    }

    public override object? VisitReplaceValueExpression(ReplaceValueExpression expr)
    {
        base.VisitReplaceValueExpression(expr);
        return Rule(true);
    }

    public override object? VisitRenameExpression(RenameExpression expr)
    {
        base.VisitRenameExpression(expr);
        return Rule(true);
    }

    public override object? VisitTransformExpression(TransformExpression expr)
    {
        // §5.6: updating when a copy or the return clause is; the modify clause does not count.
        var updating = false;
        foreach (var binding in expr.CopyBindings)
            updating |= Category(binding.Expression);
        Category(expr.ModifyExpr);
        updating |= Category(expr.ReturnExpr);
        return Rule(updating);
    }

    // ---- §7 Extensions to existing expressions ----

    public override object? VisitFlworExpression(FlworExpression expr)
    {
        const string where = "an expression in a FLWOR clause other than return";
        foreach (var clause in expr.Clauses)
        {
            switch (clause)
            {
                case ForClause fc:
                    foreach (var binding in fc.Bindings)
                        Simple(binding.Expression, where);
                    break;
                case LetClause lc:
                    foreach (var binding in lc.Bindings)
                        Simple(binding.Expression, where);
                    break;
                case WindowClause wc:
                    Simple(wc.Expression, where);
                    Simple(wc.Start.When, where);
                    Simple(wc.End?.When, where);
                    break;
                case WhereClause wh:
                    Simple(wh.Condition, where);
                    break;
                case WhileClause wl:
                    Simple(wl.Condition, where);
                    break;
                case GroupByClause gc:
                    foreach (var spec in gc.GroupingSpecs)
                        Simple(spec.Expression, where);
                    break;
                case OrderByClause oc:
                    foreach (var spec in oc.OrderSpecs)
                        Simple(spec.Expression, where);
                    break;
            }
        }
        var updating = Category(expr.ReturnExpression);
        if (expr.OtherwiseExpression != null)
            updating |= Category(expr.OtherwiseExpression);
        return Rule(updating);
    }

    public override object? VisitTypeswitchExpression(TypeswitchExpression expr)
    {
        Simple(expr.Operand, "the operand of a typeswitch");
        var updating = false;
        foreach (var c in expr.Cases)
            updating |= Category(c.Result);
        updating |= Category(expr.Default.Result);
        return Rule(updating);
    }

    public override object? VisitSwitchExpression(SwitchExpression expr)
    {
        Simple(expr.Operand, "the operand of a switch");
        var updating = false;
        foreach (var c in expr.Cases)
        {
            foreach (var value in c.Values)
                Simple(value, "a case operand of a switch");
            updating |= Category(c.Result);
        }
        updating |= Category(expr.Default);
        return Rule(updating);
    }

    public override object? VisitIfExpression(IfExpression expr)
    {
        Simple(expr.Condition, "the test of a conditional expression");
        var updating = Category(expr.Then);
        if (expr.Else != null)
            updating |= Category(expr.Else);
        return Rule(updating);
    }

    public override object? VisitTryCatchExpression(TryCatchExpression expr)
    {
        var updating = Category(expr.TryExpression);
        foreach (var c in expr.CatchClauses)
            updating |= Category(c.Expression);
        return Rule(updating);
    }

    public override object? VisitSequenceExpression(SequenceExpression expr)
    {
        var updating = false;
        foreach (var item in expr.Items)
            updating |= Category(item);
        return Rule(updating);
    }

    public override object? VisitInlineFunctionExpression(InlineFunctionExpression expr)
    {
        // §7.9: a simple expression in any case; with %simple its body must be simple.
        var body = Category(expr.Body);
        if (body && expr.DeclaredUpdating == false)
            Report(expr.Body, "the body of an inline function declared %simple");
        return Rule(false);
    }

    public override object? VisitFunctionCallExpression(FunctionCallExpression expr)
    {
        // §7.10: arguments of any category; a call of an updating function is updating, a partial
        // application of one is a simple expression returning an updating function. (E => f(x) is
        // built as the call f(E, x). The 4.0 E -> f(x) maps over E like `!`: an other expression.)
        var partial = false;
        foreach (var arg in expr.Arguments)
        {
            partial |= arg is ArgumentPlaceholder;
            Category(arg);
        }
        return Rule(!partial && _updatingFunctions.Contains((expr.Name, expr.Arguments.Count)));
    }

    public override object? VisitDynamicFunctionCallExpression(DynamicFunctionCallExpression expr)
    {
        // §7.11: a simple expression; §5.5: invoke updating is an updating expression. The operands
        // of both may be of any category.
        Category(expr.FunctionExpression);
        foreach (var arg in expr.Arguments)
            Category(arg);
        return Rule(expr.IsUpdatingInvocation);
    }

    public override object? VisitKeywordArgument(KeywordArgument expr)
        => Rule(Category(expr.Value));

    // §7.12: a node constructor takes the category of its operands, which may be of any category.

    public override object? VisitElementConstructor(ElementConstructor expr)
    {
        base.VisitElementConstructor(expr);
        return RuleByOperands();
    }

    public override object? VisitComputedElementConstructor(ComputedElementConstructor expr)
    {
        base.VisitComputedElementConstructor(expr);
        return RuleByOperands();
    }

    public override object? VisitAttributeConstructor(AttributeConstructor expr)
        => Rule(Category(expr.Value));

    public override object? VisitComputedAttributeConstructor(ComputedAttributeConstructor expr)
    {
        base.VisitComputedAttributeConstructor(expr);
        return RuleByOperands();
    }

    public override object? VisitTextConstructor(TextConstructor expr)
    {
        base.VisitTextConstructor(expr);
        return RuleByOperands();
    }

    public override object? VisitCommentConstructor(CommentConstructor expr)
    {
        base.VisitCommentConstructor(expr);
        return RuleByOperands();
    }

    public override object? VisitPIConstructor(PIConstructor expr)
    {
        base.VisitPIConstructor(expr);
        return RuleByOperands();
    }

    public override object? VisitDocumentConstructor(DocumentConstructor expr)
    {
        base.VisitDocumentConstructor(expr);
        return RuleByOperands();
    }
}
