namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Visitor interface for XQuery expression trees.
/// </summary>
public interface IXQueryExpressionVisitor<T>
{
    // Literals
    T VisitIntegerLiteral(IntegerLiteral expr);
    T VisitDecimalLiteral(DecimalLiteral expr);
    T VisitDoubleLiteral(DoubleLiteral expr);
    T VisitStringLiteral(StringLiteral expr);
    T VisitBooleanLiteral(BooleanLiteral expr);
    T VisitEmptySequence(EmptySequence expr);
    T VisitContextItem(ContextItemExpression expr);

    // Path expressions
    T VisitPathExpression(PathExpression expr);
    T VisitStepExpression(StepExpression expr);
    T VisitFilterExpression(FilterExpression expr);

    // FLWOR
    T VisitFlworExpression(FlworExpression expr);

    // Operators
    T VisitBinaryExpression(BinaryExpression expr);
    T VisitUnaryExpression(UnaryExpression expr);
    T VisitSequenceExpression(SequenceExpression expr);
    T VisitRangeExpression(RangeExpression expr);
    T VisitInstanceOfExpression(InstanceOfExpression expr);
    T VisitCastExpression(CastExpression expr);
    T VisitCastableExpression(CastableExpression expr);
    T VisitTreatExpression(TreatExpression expr);
    T VisitArrowExpression(ArrowExpression expr);
    T VisitSimpleMapExpression(SimpleMapExpression expr);
    T VisitStringConcatExpression(StringConcatExpression expr);

    // Functions and variables
    T VisitFunctionCallExpression(FunctionCallExpression expr);
    T VisitVariableReference(VariableReference expr);
    T VisitNamedFunctionRef(NamedFunctionRef expr);
    T VisitInlineFunctionExpression(InlineFunctionExpression expr);
    T VisitDynamicFunctionCallExpression(DynamicFunctionCallExpression expr);
    T VisitArgumentPlaceholder(ArgumentPlaceholder expr);

    // Conditionals
    T VisitIfExpression(IfExpression expr);
    T VisitQuantifiedExpression(QuantifiedExpression expr);
    T VisitSwitchExpression(SwitchExpression expr);
    T VisitTypeswitchExpression(TypeswitchExpression expr);
    T VisitTryCatchExpression(TryCatchExpression expr);

    // Constructors
    T VisitElementConstructor(ElementConstructor expr);
    T VisitComputedElementConstructor(ComputedElementConstructor expr);
    T VisitAttributeConstructor(AttributeConstructor expr);
    T VisitComputedAttributeConstructor(ComputedAttributeConstructor expr);
    T VisitTextConstructor(TextConstructor expr);
    T VisitCommentConstructor(CommentConstructor expr);
    T VisitPIConstructor(PIConstructor expr);
    T VisitDocumentConstructor(DocumentConstructor expr);
    T VisitNamespaceConstructor(NamespaceConstructor expr);
    T VisitMapConstructor(MapConstructor expr);
    T VisitArrayConstructor(ArrayConstructor expr);
    T VisitLookupExpression(LookupExpression expr);
    T VisitUnaryLookupExpression(UnaryLookupExpression expr);

    // Module declarations
    T VisitModuleExpression(ModuleExpression expr);
    T VisitVariableDeclaration(VariableDeclarationExpression expr);
    T VisitFunctionDeclaration(FunctionDeclarationExpression expr);
    T VisitNamespaceDeclaration(NamespaceDeclarationExpression expr);
    T VisitModuleImport(ModuleImportExpression expr);
}

/// <summary>
/// Base visitor with default traversal behavior.
/// Override methods to customize behavior.
/// </summary>
public abstract class XQueryExpressionVisitor<T> : IXQueryExpressionVisitor<T>
{
    protected virtual T DefaultVisit(XQueryExpression expr) => default!;

    // Literals
    public virtual T VisitIntegerLiteral(IntegerLiteral expr) => DefaultVisit(expr);
    public virtual T VisitDecimalLiteral(DecimalLiteral expr) => DefaultVisit(expr);
    public virtual T VisitDoubleLiteral(DoubleLiteral expr) => DefaultVisit(expr);
    public virtual T VisitStringLiteral(StringLiteral expr) => DefaultVisit(expr);
    public virtual T VisitBooleanLiteral(BooleanLiteral expr) => DefaultVisit(expr);
    public virtual T VisitEmptySequence(EmptySequence expr) => DefaultVisit(expr);
    public virtual T VisitContextItem(ContextItemExpression expr) => DefaultVisit(expr);

    // Path expressions
    public virtual T VisitPathExpression(PathExpression expr) => DefaultVisit(expr);
    public virtual T VisitStepExpression(StepExpression expr) => DefaultVisit(expr);
    public virtual T VisitFilterExpression(FilterExpression expr) => DefaultVisit(expr);

    // FLWOR
    public virtual T VisitFlworExpression(FlworExpression expr) => DefaultVisit(expr);

    // Operators
    public virtual T VisitBinaryExpression(BinaryExpression expr) => DefaultVisit(expr);
    public virtual T VisitUnaryExpression(UnaryExpression expr) => DefaultVisit(expr);
    public virtual T VisitSequenceExpression(SequenceExpression expr) => DefaultVisit(expr);
    public virtual T VisitRangeExpression(RangeExpression expr) => DefaultVisit(expr);
    public virtual T VisitInstanceOfExpression(InstanceOfExpression expr) => DefaultVisit(expr);
    public virtual T VisitCastExpression(CastExpression expr) => DefaultVisit(expr);
    public virtual T VisitCastableExpression(CastableExpression expr) => DefaultVisit(expr);
    public virtual T VisitTreatExpression(TreatExpression expr) => DefaultVisit(expr);
    public virtual T VisitArrowExpression(ArrowExpression expr) => DefaultVisit(expr);
    public virtual T VisitSimpleMapExpression(SimpleMapExpression expr) => DefaultVisit(expr);
    public virtual T VisitStringConcatExpression(StringConcatExpression expr) => DefaultVisit(expr);

    // Functions and variables
    public virtual T VisitFunctionCallExpression(FunctionCallExpression expr) => DefaultVisit(expr);
    public virtual T VisitVariableReference(VariableReference expr) => DefaultVisit(expr);
    public virtual T VisitNamedFunctionRef(NamedFunctionRef expr) => DefaultVisit(expr);
    public virtual T VisitInlineFunctionExpression(InlineFunctionExpression expr) => DefaultVisit(expr);
    public virtual T VisitDynamicFunctionCallExpression(DynamicFunctionCallExpression expr) => DefaultVisit(expr);
    public virtual T VisitArgumentPlaceholder(ArgumentPlaceholder expr) => DefaultVisit(expr);

    // Conditionals
    public virtual T VisitIfExpression(IfExpression expr) => DefaultVisit(expr);
    public virtual T VisitQuantifiedExpression(QuantifiedExpression expr) => DefaultVisit(expr);
    public virtual T VisitSwitchExpression(SwitchExpression expr) => DefaultVisit(expr);
    public virtual T VisitTypeswitchExpression(TypeswitchExpression expr) => DefaultVisit(expr);
    public virtual T VisitTryCatchExpression(TryCatchExpression expr) => DefaultVisit(expr);

    // Constructors
    public virtual T VisitElementConstructor(ElementConstructor expr) => DefaultVisit(expr);
    public virtual T VisitComputedElementConstructor(ComputedElementConstructor expr) => DefaultVisit(expr);
    public virtual T VisitAttributeConstructor(AttributeConstructor expr) => DefaultVisit(expr);
    public virtual T VisitComputedAttributeConstructor(ComputedAttributeConstructor expr) => DefaultVisit(expr);
    public virtual T VisitTextConstructor(TextConstructor expr) => DefaultVisit(expr);
    public virtual T VisitCommentConstructor(CommentConstructor expr) => DefaultVisit(expr);
    public virtual T VisitPIConstructor(PIConstructor expr) => DefaultVisit(expr);
    public virtual T VisitDocumentConstructor(DocumentConstructor expr) => DefaultVisit(expr);
    public virtual T VisitNamespaceConstructor(NamespaceConstructor expr) => DefaultVisit(expr);
    public virtual T VisitMapConstructor(MapConstructor expr) => DefaultVisit(expr);
    public virtual T VisitArrayConstructor(ArrayConstructor expr) => DefaultVisit(expr);
    public virtual T VisitLookupExpression(LookupExpression expr) => DefaultVisit(expr);
    public virtual T VisitUnaryLookupExpression(UnaryLookupExpression expr) => DefaultVisit(expr);

    // Module declarations
    public virtual T VisitModuleExpression(ModuleExpression expr) => DefaultVisit(expr);
    public virtual T VisitVariableDeclaration(VariableDeclarationExpression expr) => DefaultVisit(expr);
    public virtual T VisitFunctionDeclaration(FunctionDeclarationExpression expr) => DefaultVisit(expr);
    public virtual T VisitNamespaceDeclaration(NamespaceDeclarationExpression expr) => DefaultVisit(expr);
    public virtual T VisitModuleImport(ModuleImportExpression expr) => DefaultVisit(expr);
}

/// <summary>
/// Visitor that rewrites expression trees.
/// Returns the same expression by default, override to transform.
/// </summary>
public abstract class XQueryExpressionRewriter : XQueryExpressionVisitor<XQueryExpression>
{
    protected override XQueryExpression DefaultVisit(XQueryExpression expr) => expr;

    public virtual XQueryExpression Rewrite(XQueryExpression expr)
    {
        if (expr is null) return null!;
        return expr.Accept(this);
    }

    public override XQueryExpression VisitBinaryExpression(BinaryExpression expr)
    {
        var left = Rewrite(expr.Left);
        var right = Rewrite(expr.Right);
        if (left == expr.Left && right == expr.Right)
            return expr;
        return new BinaryExpression
        {
            Left = left,
            Operator = expr.Operator,
            Right = right,
            Location = expr.Location
        };
    }

    public override XQueryExpression VisitUnaryExpression(UnaryExpression expr)
    {
        var operand = Rewrite(expr.Operand);
        if (operand == expr.Operand)
            return expr;
        return new UnaryExpression
        {
            Operator = expr.Operator,
            Operand = operand,
            Location = expr.Location
        };
    }

    public override XQueryExpression VisitIfExpression(IfExpression expr)
    {
        var condition = Rewrite(expr.Condition);
        var then = Rewrite(expr.Then);
        var @else = expr.Else != null ? Rewrite(expr.Else) : null;
        if (condition == expr.Condition && then == expr.Then && @else == expr.Else)
            return expr;
        return new IfExpression
        {
            Condition = condition,
            Then = then,
            Else = @else,
            Location = expr.Location
        };
    }

    public override XQueryExpression VisitPathExpression(PathExpression expr)
    {
        var steps = new List<StepExpression>();
        var changed = false;
        XQueryExpression? newInit = null;

        if (expr.InitialExpression != null)
        {
            newInit = Rewrite(expr.InitialExpression);
            if (newInit != expr.InitialExpression)
                changed = true;
        }

        foreach (var step in expr.Steps)
        {
            var newStep = (StepExpression)Rewrite(step);
            steps.Add(newStep);
            if (newStep != step)
                changed = true;
        }
        if (!changed)
            return expr;
        return new PathExpression
        {
            IsAbsolute = expr.IsAbsolute,
            InitialExpression = newInit,
            Steps = steps,
            Location = expr.Location
        };
    }

    public override XQueryExpression VisitFlworExpression(FlworExpression expr)
    {
        var clauses = new List<FlworClause>();
        var changed = false;
        foreach (var clause in expr.Clauses)
        {
            var newClause = RewriteClause(clause);
            clauses.Add(newClause);
            if (newClause != clause) changed = true;
        }
        var ret = Rewrite(expr.ReturnExpression);
        if (ret != expr.ReturnExpression) changed = true;
        if (!changed) return expr;
        return new FlworExpression
        {
            Clauses = clauses,
            ReturnExpression = ret,
            Location = expr.Location
        };
    }

    private FlworClause RewriteClause(FlworClause clause)
    {
        switch (clause)
        {
            case ForClause fc:
            {
                var bindings = new List<ForBinding>();
                var changed = false;
                foreach (var b in fc.Bindings)
                {
                    var newExpr = Rewrite(b.Expression);
                    if (newExpr != b.Expression) changed = true;
                    bindings.Add(new ForBinding
                    {
                        Variable = b.Variable,
                        PositionalVariable = b.PositionalVariable,
                        AllowingEmpty = b.AllowingEmpty,
                        TypeDeclaration = b.TypeDeclaration,
                        Expression = newExpr
                    });
                }
                return changed ? new ForClause { Bindings = bindings } : fc;
            }
            case LetClause lc:
            {
                var bindings = new List<LetBinding>();
                var changed = false;
                foreach (var b in lc.Bindings)
                {
                    var newExpr = Rewrite(b.Expression);
                    if (newExpr != b.Expression) changed = true;
                    bindings.Add(new LetBinding
                    {
                        Variable = b.Variable,
                        TypeDeclaration = b.TypeDeclaration,
                        Expression = newExpr
                    });
                }
                return changed ? new LetClause { Bindings = bindings } : lc;
            }
            case WhereClause wc:
            {
                var newCond = Rewrite(wc.Condition);
                return newCond != wc.Condition ? new WhereClause { Condition = newCond } : wc;
            }
            case OrderByClause obc:
            {
                var specs = new List<OrderSpec>();
                var changed = false;
                foreach (var s in obc.OrderSpecs)
                {
                    var newExpr = Rewrite(s.Expression);
                    if (newExpr != s.Expression) changed = true;
                    specs.Add(new OrderSpec
                    {
                        Expression = newExpr,
                        Direction = s.Direction,
                        EmptyOrder = s.EmptyOrder,
                        Collation = s.Collation
                    });
                }
                return changed ? new OrderByClause { Stable = obc.Stable, OrderSpecs = specs } : obc;
            }
            default:
                return clause;
        }
    }

    public override XQueryExpression VisitFunctionCallExpression(FunctionCallExpression expr)
    {
        var args = new List<XQueryExpression>();
        var changed = false;
        foreach (var arg in expr.Arguments)
        {
            var newArg = Rewrite(arg);
            args.Add(newArg);
            if (newArg != arg)
                changed = true;
        }
        if (!changed)
            return expr;
        return new FunctionCallExpression
        {
            Name = expr.Name,
            Arguments = args,
            ResolvedFunction = expr.ResolvedFunction,
            Location = expr.Location
        };
    }

    public override XQueryExpression VisitSequenceExpression(SequenceExpression expr)
    {
        var items = new List<XQueryExpression>();
        var changed = false;
        foreach (var item in expr.Items)
        {
            var newItem = Rewrite(item);
            items.Add(newItem);
            if (newItem != item)
                changed = true;
        }
        if (!changed)
            return expr;
        return new SequenceExpression
        {
            Items = items,
            Location = expr.Location
        };
    }

    public override XQueryExpression VisitModuleExpression(ModuleExpression expr)
    {
        var decls = new List<XQueryExpression>();
        var changed = false;
        foreach (var decl in expr.Declarations)
        {
            var newDecl = Rewrite(decl);
            decls.Add(newDecl);
            if (newDecl != decl) changed = true;
        }
        var body = Rewrite(expr.Body);
        if (body != expr.Body) changed = true;
        if (!changed) return expr;
        return new ModuleExpression { Declarations = decls, Body = body, Location = expr.Location };
    }

    public override XQueryExpression VisitInstanceOfExpression(InstanceOfExpression expr)
    {
        var operand = Rewrite(expr.Expression);
        if (operand == expr.Expression) return expr;
        return new InstanceOfExpression
        {
            Expression = operand,
            TargetType = expr.TargetType,
            Location = expr.Location
        };
    }

    public override XQueryExpression VisitCastExpression(CastExpression expr)
    {
        var operand = Rewrite(expr.Expression);
        if (operand == expr.Expression) return expr;
        return new CastExpression
        {
            Expression = operand,
            TargetType = expr.TargetType,
            Location = expr.Location
        };
    }

    public override XQueryExpression VisitCastableExpression(CastableExpression expr)
    {
        var operand = Rewrite(expr.Expression);
        if (operand == expr.Expression) return expr;
        return new CastableExpression
        {
            Expression = operand,
            TargetType = expr.TargetType,
            Location = expr.Location
        };
    }

    public override XQueryExpression VisitTreatExpression(TreatExpression expr)
    {
        var operand = Rewrite(expr.Expression);
        if (operand == expr.Expression) return expr;
        return new TreatExpression
        {
            Expression = operand,
            TargetType = expr.TargetType,
            Location = expr.Location
        };
    }

    public override XQueryExpression VisitRangeExpression(RangeExpression expr)
    {
        var start = Rewrite(expr.Start);
        var end = Rewrite(expr.End);
        if (start == expr.Start && end == expr.End) return expr;
        return new RangeExpression
        {
            Start = start,
            End = end,
            Location = expr.Location
        };
    }

    public override XQueryExpression VisitQuantifiedExpression(QuantifiedExpression expr)
    {
        var bindings = new List<QuantifiedBinding>();
        var changed = false;
        foreach (var b in expr.Bindings)
        {
            var newExpr = Rewrite(b.Expression);
            if (newExpr != b.Expression) changed = true;
            bindings.Add(new QuantifiedBinding { Variable = b.Variable, Expression = newExpr });
        }
        var satisfies = Rewrite(expr.Satisfies);
        if (satisfies != expr.Satisfies) changed = true;
        if (!changed) return expr;
        return new QuantifiedExpression
        {
            Quantifier = expr.Quantifier,
            Bindings = bindings,
            Satisfies = satisfies,
            Location = expr.Location
        };
    }

    public override XQueryExpression VisitSimpleMapExpression(SimpleMapExpression expr)
    {
        var left = Rewrite(expr.Left);
        var right = Rewrite(expr.Right);
        if (left == expr.Left && right == expr.Right) return expr;
        return new SimpleMapExpression
        {
            Left = left,
            Right = right,
            IsPathStep = expr.IsPathStep,
            Location = expr.Location
        };
    }

    public override XQueryExpression VisitStringConcatExpression(StringConcatExpression expr)
    {
        var operands = new List<XQueryExpression>();
        var changed = false;
        foreach (var op in expr.Operands)
        {
            var newOp = Rewrite(op);
            operands.Add(newOp);
            if (newOp != op) changed = true;
        }
        if (!changed) return expr;
        return new StringConcatExpression
        {
            Operands = operands,
            Location = expr.Location
        };
    }

    public override XQueryExpression VisitFilterExpression(FilterExpression expr)
    {
        var primary = Rewrite(expr.Primary);
        var preds = new List<XQueryExpression>();
        var changed = primary != expr.Primary;
        foreach (var pred in expr.Predicates)
        {
            var newPred = Rewrite(pred);
            preds.Add(newPred);
            if (newPred != pred) changed = true;
        }
        if (!changed) return expr;
        return new FilterExpression
        {
            Primary = primary,
            Predicates = preds,
            Location = expr.Location
        };
    }

    public override XQueryExpression VisitInlineFunctionExpression(InlineFunctionExpression expr)
    {
        var body = Rewrite(expr.Body);
        if (body == expr.Body) return expr;
        return new InlineFunctionExpression
        {
            Parameters = expr.Parameters,
            ReturnType = expr.ReturnType,
            Body = body,
            Location = expr.Location
        };
    }

    public override XQueryExpression VisitDynamicFunctionCallExpression(DynamicFunctionCallExpression expr)
    {
        var funcExpr = Rewrite(expr.FunctionExpression);
        var args = new List<XQueryExpression>();
        var changed = funcExpr != expr.FunctionExpression;
        foreach (var arg in expr.Arguments)
        {
            var newArg = Rewrite(arg);
            args.Add(newArg);
            if (newArg != arg) changed = true;
        }
        if (!changed) return expr;
        return new DynamicFunctionCallExpression
        {
            FunctionExpression = funcExpr,
            Arguments = args,
            Location = expr.Location
        };
    }

    public override XQueryExpression VisitSwitchExpression(SwitchExpression expr)
    {
        var operand = Rewrite(expr.Operand);
        var cases = new List<SwitchCase>();
        var changed = operand != expr.Operand;
        foreach (var c in expr.Cases)
        {
            var values = new List<XQueryExpression>();
            var caseChanged = false;
            foreach (var v in c.Values)
            {
                var nv = Rewrite(v);
                values.Add(nv);
                if (nv != v) caseChanged = true;
            }
            var result = Rewrite(c.Result);
            if (result != c.Result) caseChanged = true;
            if (caseChanged) changed = true;
            cases.Add(new SwitchCase { Values = caseChanged ? values : c.Values, Result = result });
        }
        var def = Rewrite(expr.Default);
        if (def != expr.Default) changed = true;
        if (!changed) return expr;
        return new SwitchExpression
        {
            Operand = operand,
            Cases = cases,
            Default = def,
            Location = expr.Location
        };
    }

    public override XQueryExpression VisitTryCatchExpression(TryCatchExpression expr)
    {
        var tryExpr = Rewrite(expr.TryExpression);
        var catches = new List<CatchClause>();
        var changed = tryExpr != expr.TryExpression;
        foreach (var c in expr.CatchClauses)
        {
            var catchExpr = Rewrite(c.Expression);
            if (catchExpr != c.Expression) changed = true;
            catches.Add(new CatchClause { ErrorCodes = c.ErrorCodes, Expression = catchExpr });
        }
        if (!changed) return expr;
        return new TryCatchExpression
        {
            TryExpression = tryExpr,
            CatchClauses = catches,
            Location = expr.Location
        };
    }

    public override XQueryExpression VisitMapConstructor(MapConstructor expr)
    {
        var entries = new List<MapEntry>();
        var changed = false;
        foreach (var e in expr.Entries)
        {
            var key = Rewrite(e.Key);
            var value = Rewrite(e.Value);
            if (key != e.Key || value != e.Value) changed = true;
            entries.Add(new MapEntry { Key = key, Value = value });
        }
        if (!changed) return expr;
        return new MapConstructor { Entries = entries, Location = expr.Location };
    }

    public override XQueryExpression VisitArrayConstructor(ArrayConstructor expr)
    {
        var members = new List<XQueryExpression>();
        var changed = false;
        foreach (var m in expr.Members)
        {
            var nm = Rewrite(m);
            members.Add(nm);
            if (nm != m) changed = true;
        }
        if (!changed) return expr;
        return new ArrayConstructor { Kind = expr.Kind, Members = members, Location = expr.Location };
    }

    public override XQueryExpression VisitLookupExpression(LookupExpression expr)
    {
        var @base = Rewrite(expr.Base);
        var key = expr.Key != null ? Rewrite(expr.Key) : null;
        if (@base == expr.Base && key == expr.Key) return expr;
        return new LookupExpression { Base = @base, Key = key, Location = expr.Location };
    }

    public override XQueryExpression VisitVariableDeclaration(VariableDeclarationExpression expr)
    {
        var value = expr.Value != null ? Rewrite(expr.Value) : null;
        if (value == expr.Value) return expr;
        return new VariableDeclarationExpression
        {
            Name = expr.Name, TypeDeclaration = expr.TypeDeclaration,
            Value = value, IsExternal = expr.IsExternal, Location = expr.Location
        };
    }

    public override XQueryExpression VisitFunctionDeclaration(FunctionDeclarationExpression expr)
    {
        var body = Rewrite(expr.Body);
        if (body == expr.Body) return expr;
        return new FunctionDeclarationExpression
        {
            Name = expr.Name, Parameters = expr.Parameters, ReturnType = expr.ReturnType,
            Body = body, Location = expr.Location
        };
    }
}

/// <summary>
/// Visitor that walks the tree without modifying it.
/// Useful for analysis passes.
/// </summary>
public abstract class XQueryExpressionWalker : XQueryExpressionVisitor<object?>
{
    protected override object? DefaultVisit(XQueryExpression expr) => null;

    public virtual void Walk(XQueryExpression expr)
    {
        expr.Accept(this);
    }

    public override object? VisitBinaryExpression(BinaryExpression expr)
    {
        Walk(expr.Left);
        Walk(expr.Right);
        return null;
    }

    public override object? VisitUnaryExpression(UnaryExpression expr)
    {
        Walk(expr.Operand);
        return null;
    }

    public override object? VisitIfExpression(IfExpression expr)
    {
        Walk(expr.Condition);
        Walk(expr.Then);
        if (expr.Else != null)
            Walk(expr.Else);
        return null;
    }

    public override object? VisitPathExpression(PathExpression expr)
    {
        if (expr.InitialExpression != null)
            Walk(expr.InitialExpression);
        foreach (var step in expr.Steps)
            Walk(step);
        return null;
    }

    public override object? VisitStepExpression(StepExpression expr)
    {
        foreach (var pred in expr.Predicates)
            Walk(pred);
        return null;
    }

    public override object? VisitFlworExpression(FlworExpression expr)
    {
        foreach (var clause in expr.Clauses)
        {
            switch (clause)
            {
                case ForClause fc:
                    foreach (var binding in fc.Bindings)
                        Walk(binding.Expression);
                    break;
                case LetClause lc:
                    foreach (var binding in lc.Bindings)
                        Walk(binding.Expression);
                    break;
                case WhereClause wc:
                    Walk(wc.Condition);
                    break;
                case OrderByClause obc:
                    foreach (var spec in obc.OrderSpecs)
                        Walk(spec.Expression);
                    break;
            }
        }
        Walk(expr.ReturnExpression);
        return null;
    }

    public override object? VisitFunctionCallExpression(FunctionCallExpression expr)
    {
        foreach (var arg in expr.Arguments)
            Walk(arg);
        return null;
    }

    public override object? VisitSequenceExpression(SequenceExpression expr)
    {
        foreach (var item in expr.Items)
            Walk(item);
        return null;
    }

    public override object? VisitFilterExpression(FilterExpression expr)
    {
        Walk(expr.Primary);
        foreach (var pred in expr.Predicates)
            Walk(pred);
        return null;
    }

    public override object? VisitModuleExpression(ModuleExpression expr)
    {
        foreach (var decl in expr.Declarations)
            Walk(decl);
        Walk(expr.Body);
        return null;
    }

    public override object? VisitVariableDeclaration(VariableDeclarationExpression expr)
    {
        if (expr.Value != null)
            Walk(expr.Value);
        return null;
    }

    public override object? VisitFunctionDeclaration(FunctionDeclarationExpression expr)
    {
        Walk(expr.Body);
        return null;
    }

    public override object? VisitInstanceOfExpression(InstanceOfExpression expr)
    {
        Walk(expr.Expression);
        return null;
    }

    public override object? VisitCastExpression(CastExpression expr)
    {
        Walk(expr.Expression);
        return null;
    }

    public override object? VisitCastableExpression(CastableExpression expr)
    {
        Walk(expr.Expression);
        return null;
    }

    public override object? VisitTreatExpression(TreatExpression expr)
    {
        Walk(expr.Expression);
        return null;
    }

    public override object? VisitRangeExpression(RangeExpression expr)
    {
        Walk(expr.Start);
        Walk(expr.End);
        return null;
    }

    public override object? VisitQuantifiedExpression(QuantifiedExpression expr)
    {
        foreach (var b in expr.Bindings)
            Walk(b.Expression);
        Walk(expr.Satisfies);
        return null;
    }

    public override object? VisitSimpleMapExpression(SimpleMapExpression expr)
    {
        Walk(expr.Left);
        Walk(expr.Right);
        return null;
    }

    public override object? VisitStringConcatExpression(StringConcatExpression expr)
    {
        foreach (var op in expr.Operands)
            Walk(op);
        return null;
    }

    public override object? VisitInlineFunctionExpression(InlineFunctionExpression expr)
    {
        Walk(expr.Body);
        return null;
    }

    public override object? VisitDynamicFunctionCallExpression(DynamicFunctionCallExpression expr)
    {
        Walk(expr.FunctionExpression);
        foreach (var arg in expr.Arguments)
            Walk(arg);
        return null;
    }

    public override object? VisitSwitchExpression(SwitchExpression expr)
    {
        Walk(expr.Operand);
        foreach (var c in expr.Cases)
        {
            foreach (var v in c.Values)
                Walk(v);
            Walk(c.Result);
        }
        Walk(expr.Default);
        return null;
    }

    public override object? VisitTypeswitchExpression(TypeswitchExpression expr)
    {
        Walk(expr.Operand);
        foreach (var c in expr.Cases)
            Walk(c.Result);
        Walk(expr.Default.Result);
        return null;
    }

    public override object? VisitTryCatchExpression(TryCatchExpression expr)
    {
        Walk(expr.TryExpression);
        foreach (var c in expr.CatchClauses)
            Walk(c.Expression);
        return null;
    }

    public override object? VisitLookupExpression(LookupExpression expr)
    {
        Walk(expr.Base);
        if (expr.Key != null) Walk(expr.Key);
        return null;
    }

    public override object? VisitUnaryLookupExpression(UnaryLookupExpression expr)
    {
        if (expr.Key != null) Walk(expr.Key);
        return null;
    }

    public override object? VisitElementConstructor(ElementConstructor expr)
    {
        foreach (var attr in expr.Attributes)
            Walk(attr);
        foreach (var content in expr.Content)
            Walk(content);
        return null;
    }

    public override object? VisitMapConstructor(MapConstructor expr)
    {
        foreach (var entry in expr.Entries)
        {
            Walk(entry.Key);
            Walk(entry.Value);
        }
        return null;
    }

    public override object? VisitArrayConstructor(ArrayConstructor expr)
    {
        foreach (var member in expr.Members)
            Walk(member);
        return null;
    }
}
