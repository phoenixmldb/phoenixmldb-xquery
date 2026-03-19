using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Analysis;

/// <summary>
/// Infers static types for expressions.
/// </summary>
public sealed class TypeInferrer : XQueryExpressionWalker
{
    private readonly StaticContext _context;
    private readonly List<AnalysisError> _errors = [];

    public TypeInferrer(StaticContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Infers types for all expressions.
    /// </summary>
    public void Infer(XQueryExpression expression, List<AnalysisError> errors)
    {
        _errors.Clear();
        InferType(expression);
        errors.AddRange(_errors);
    }

    private XdmSequenceType InferType(XQueryExpression expr)
    {
        var type = expr switch
        {
            IntegerLiteral => XdmSequenceType.Integer,
            DecimalLiteral => XdmSequenceType.Decimal,
            DoubleLiteral => XdmSequenceType.Double,
            StringLiteral => XdmSequenceType.String,
            BooleanLiteral => XdmSequenceType.Boolean,
            EmptySequence => XdmSequenceType.Empty,
            ContextItemExpression => _context.ContextItemType ?? XdmSequenceType.Item,
            VariableReference vr => InferVariableType(vr),
            PathExpression pe => InferPathType(pe),
            StepExpression se => InferStepType(se),
            FilterExpression fe => InferFilterType(fe),
            FlworExpression fe => InferFlworType(fe),
            BinaryExpression be => InferBinaryType(be),
            UnaryExpression ue => InferUnaryType(ue),
            IfExpression ie => InferIfType(ie),
            FunctionCallExpression fc => InferFunctionCallType(fc),
            SequenceExpression se => InferSequenceType(se),
            RangeExpression => new XdmSequenceType { ItemType = ItemType.Integer, Occurrence = Occurrence.ZeroOrMore },
            InstanceOfExpression => XdmSequenceType.Boolean,
            CastExpression ce => ce.TargetType,
            CastableExpression => XdmSequenceType.Boolean,
            TreatExpression te => te.TargetType,
            QuantifiedExpression => XdmSequenceType.Boolean,
            SwitchExpression se => InferSwitchType(se),
            TypeswitchExpression te => InferTypeswitchType(te),
            TryCatchExpression tce => InferTryCatchType(tce),
            ElementConstructor => new XdmSequenceType { ItemType = ItemType.Element, Occurrence = Occurrence.ExactlyOne },
            ComputedElementConstructor => new XdmSequenceType { ItemType = ItemType.Element, Occurrence = Occurrence.ExactlyOne },
            AttributeConstructor => new XdmSequenceType { ItemType = ItemType.Attribute, Occurrence = Occurrence.ExactlyOne },
            ComputedAttributeConstructor => new XdmSequenceType { ItemType = ItemType.Attribute, Occurrence = Occurrence.ExactlyOne },
            TextConstructor => new XdmSequenceType { ItemType = ItemType.Text, Occurrence = Occurrence.ExactlyOne },
            CommentConstructor => new XdmSequenceType { ItemType = ItemType.Comment, Occurrence = Occurrence.ExactlyOne },
            PIConstructor => new XdmSequenceType { ItemType = ItemType.ProcessingInstruction, Occurrence = Occurrence.ExactlyOne },
            DocumentConstructor => new XdmSequenceType { ItemType = ItemType.Document, Occurrence = Occurrence.ExactlyOne },
            MapConstructor => new XdmSequenceType { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne },
            ArrayConstructor => new XdmSequenceType { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne },
            ArrowExpression ae => InferArrowType(ae),
            SimpleMapExpression => XdmSequenceType.ZeroOrMoreItems,
            InlineFunctionExpression => new XdmSequenceType { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne },
            NamedFunctionRef => new XdmSequenceType { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne },
            _ => XdmSequenceType.ZeroOrMoreItems
        };

        expr.StaticType = type;
        return type;
    }

    private XdmSequenceType InferVariableType(VariableReference expr)
    {
        if (expr.ResolvedBinding != null)
            return expr.ResolvedBinding.Type;
        return XdmSequenceType.ZeroOrMoreItems;
    }

    private XdmSequenceType InferPathType(PathExpression expr)
    {
        if (expr.Steps.Count == 0)
            return XdmSequenceType.Empty;

        // Walk all steps
        foreach (var step in expr.Steps)
            InferType(step);

        // Path expressions return nodes
        var lastStep = expr.Steps[^1];
        var lastType = lastStep.StaticType ?? XdmSequenceType.ZeroOrMoreNodes;

        // Paths generally return zero or more
        return new XdmSequenceType
        {
            ItemType = lastType.ItemType,
            Occurrence = Occurrence.ZeroOrMore
        };
    }

    private XdmSequenceType InferStepType(StepExpression expr)
    {
        // Infer predicate types
        foreach (var pred in expr.Predicates)
            InferType(pred);

        // Determine item type based on axis and node test
        var itemType = expr.Axis switch
        {
            Axis.Attribute => ItemType.Attribute,
            Axis.Namespace => ItemType.Node, // Namespace nodes
            _ when expr.NodeTest is KindTest kt => kt.Kind switch
            {
                Core.XdmNodeKind.Element => ItemType.Element,
                Core.XdmNodeKind.Attribute => ItemType.Attribute,
                Core.XdmNodeKind.Text => ItemType.Text,
                Core.XdmNodeKind.Comment => ItemType.Comment,
                Core.XdmNodeKind.ProcessingInstruction => ItemType.ProcessingInstruction,
                Core.XdmNodeKind.Document => ItemType.Document,
                _ => ItemType.Node
            },
            _ => ItemType.Node
        };

        return new XdmSequenceType
        {
            ItemType = itemType,
            Occurrence = Occurrence.ZeroOrMore
        };
    }

    private XdmSequenceType InferFilterType(FilterExpression expr)
    {
        var primaryType = InferType(expr.Primary);
        foreach (var pred in expr.Predicates)
            InferType(pred);

        // Filtering may reduce to zero items
        return new XdmSequenceType
        {
            ItemType = primaryType.ItemType,
            Occurrence = Occurrence.ZeroOrMore
        };
    }

    private XdmSequenceType InferFlworType(FlworExpression expr)
    {
        // Walk clauses
        foreach (var clause in expr.Clauses)
        {
            switch (clause)
            {
                case ForClause fc:
                    foreach (var b in fc.Bindings)
                        InferType(b.Expression);
                    break;
                case LetClause lc:
                    foreach (var b in lc.Bindings)
                        InferType(b.Expression);
                    break;
                case WhereClause wc:
                    InferType(wc.Condition);
                    break;
                case OrderByClause obc:
                    foreach (var s in obc.OrderSpecs)
                        InferType(s.Expression);
                    break;
            }
        }

        var returnType = InferType(expr.ReturnExpression);

        // FLWOR produces zero or more results
        return new XdmSequenceType
        {
            ItemType = returnType.ItemType,
            Occurrence = Occurrence.ZeroOrMore
        };
    }

    private XdmSequenceType InferBinaryType(BinaryExpression expr)
    {
        var leftType = InferType(expr.Left);
        var rightType = InferType(expr.Right);

        return expr.Operator switch
        {
            // Arithmetic returns numeric
            BinaryOperator.Add or BinaryOperator.Subtract or
            BinaryOperator.Multiply or BinaryOperator.Divide or
            BinaryOperator.Modulo => InferArithmeticType(leftType, rightType),

            BinaryOperator.IntegerDivide => XdmSequenceType.Integer,

            // Comparisons return boolean
            BinaryOperator.Equal or BinaryOperator.NotEqual or
            BinaryOperator.LessThan or BinaryOperator.LessOrEqual or
            BinaryOperator.GreaterThan or BinaryOperator.GreaterOrEqual or
            BinaryOperator.GeneralEqual or BinaryOperator.GeneralNotEqual or
            BinaryOperator.GeneralLessThan or BinaryOperator.GeneralLessOrEqual or
            BinaryOperator.GeneralGreaterThan or BinaryOperator.GeneralGreaterOrEqual or
            BinaryOperator.Is or BinaryOperator.Precedes or BinaryOperator.Follows
                => XdmSequenceType.Boolean,

            // Logical return boolean
            BinaryOperator.And or BinaryOperator.Or => XdmSequenceType.Boolean,

            // Set operations return nodes
            BinaryOperator.Union or BinaryOperator.Intersect or BinaryOperator.Except
                => XdmSequenceType.ZeroOrMoreNodes,

            // Range returns integers
            BinaryOperator.To => new XdmSequenceType
            {
                ItemType = ItemType.Integer,
                Occurrence = Occurrence.ZeroOrMore
            },

            // String concat
            BinaryOperator.Concat => XdmSequenceType.String,

            _ => XdmSequenceType.ZeroOrMoreItems
        };
    }

    private static XdmSequenceType InferArithmeticType(XdmSequenceType left, XdmSequenceType right)
    {
        // Numeric type promotion
        if (left.ItemType == ItemType.Double || right.ItemType == ItemType.Double)
            return XdmSequenceType.Double;
        if (left.ItemType == ItemType.Decimal || right.ItemType == ItemType.Decimal)
            return XdmSequenceType.Decimal;
        return XdmSequenceType.Integer;
    }

    private XdmSequenceType InferUnaryType(UnaryExpression expr)
    {
        var operandType = InferType(expr.Operand);

        return expr.Operator switch
        {
            UnaryOperator.Plus or UnaryOperator.Minus => operandType,
            UnaryOperator.Not => XdmSequenceType.Boolean,
            _ => operandType
        };
    }

    private XdmSequenceType InferIfType(IfExpression expr)
    {
        InferType(expr.Condition);
        var thenType = InferType(expr.Then);
        var elseType = expr.Else != null ? InferType(expr.Else) : XdmSequenceType.Empty;

        // Union of then and else types
        return UnionTypes(thenType, elseType);
    }

    private XdmSequenceType InferFunctionCallType(FunctionCallExpression expr)
    {
        foreach (var arg in expr.Arguments)
            InferType(arg);

        if (expr.ResolvedFunction != null)
            return expr.ResolvedFunction.ReturnType;

        return XdmSequenceType.ZeroOrMoreItems;
    }

    private XdmSequenceType InferSequenceType(SequenceExpression expr)
    {
        if (expr.Items.Count == 0)
            return XdmSequenceType.Empty;

        ItemType itemType = ItemType.Empty;
        foreach (var item in expr.Items)
        {
            var t = InferType(item);
            itemType = UnionItemTypes(itemType, t.ItemType);
        }

        return new XdmSequenceType
        {
            ItemType = itemType,
            Occurrence = expr.Items.Count == 1
                ? Occurrence.ExactlyOne
                : Occurrence.OneOrMore
        };
    }

    private XdmSequenceType InferSwitchType(SwitchExpression expr)
    {
        InferType(expr.Operand);

        XdmSequenceType? result = null;
        foreach (var @case in expr.Cases)
        {
            foreach (var v in @case.Values)
                InferType(v);
            var caseType = InferType(@case.Result);
            result = result == null ? caseType : UnionTypes(result, caseType);
        }

        var defaultType = InferType(expr.Default);
        return result == null ? defaultType : UnionTypes(result, defaultType);
    }

    private XdmSequenceType InferTypeswitchType(TypeswitchExpression expr)
    {
        InferType(expr.Operand);

        XdmSequenceType? result = null;
        foreach (var @case in expr.Cases)
        {
            var caseType = InferType(@case.Result);
            result = result == null ? caseType : UnionTypes(result, caseType);
        }

        var defaultType = InferType(expr.Default.Result);
        return result == null ? defaultType : UnionTypes(result, defaultType);
    }

    private XdmSequenceType InferTryCatchType(TryCatchExpression expr)
    {
        var tryType = InferType(expr.TryExpression);

        XdmSequenceType result = tryType;
        foreach (var @catch in expr.CatchClauses)
        {
            var catchType = InferType(@catch.Expression);
            result = UnionTypes(result, catchType);
        }

        return result;
    }

    private XdmSequenceType InferArrowType(ArrowExpression expr)
    {
        InferType(expr.Expression);
        return InferType(expr.FunctionCall);
    }

    private static XdmSequenceType UnionTypes(XdmSequenceType a, XdmSequenceType b)
    {
        return new XdmSequenceType
        {
            ItemType = UnionItemTypes(a.ItemType, b.ItemType),
            Occurrence = UnionOccurrences(a.Occurrence, b.Occurrence)
        };
    }

    private static ItemType UnionItemTypes(ItemType a, ItemType b)
    {
        if (a == b) return a;
        if (a == ItemType.Empty) return b;
        if (b == ItemType.Empty) return a;

        // Both are nodes
        if (IsNodeType(a) && IsNodeType(b))
            return ItemType.Node;

        // Both are atomic
        if (IsAtomicType(a) && IsAtomicType(b))
            return ItemType.AnyAtomicType;

        return ItemType.Item;
    }

    private static bool IsNodeType(ItemType t) =>
        t is ItemType.Node or ItemType.Element or ItemType.Attribute or
            ItemType.Text or ItemType.Comment or ItemType.ProcessingInstruction or
            ItemType.Document;

    private static bool IsAtomicType(ItemType t) =>
        t is ItemType.String or ItemType.Boolean or ItemType.Integer or
            ItemType.Decimal or ItemType.Double or ItemType.Float or
            ItemType.Date or ItemType.DateTime or ItemType.Time or
            ItemType.Duration or ItemType.YearMonthDuration or ItemType.DayTimeDuration or
            ItemType.QName or ItemType.AnyUri or
            ItemType.AnyAtomicType;

    private static Occurrence UnionOccurrences(Occurrence a, Occurrence b)
    {
        // Union tends toward more permissive
        if (a == Occurrence.ZeroOrMore || b == Occurrence.ZeroOrMore)
            return Occurrence.ZeroOrMore;
        if (a == Occurrence.OneOrMore || b == Occurrence.OneOrMore)
            return Occurrence.OneOrMore;
        if (a == Occurrence.ZeroOrOne || b == Occurrence.ZeroOrOne)
            return Occurrence.ZeroOrOne;
        return Occurrence.ExactlyOne;
    }
}
