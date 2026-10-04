using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Functions;

namespace PhoenixmlDb.XQuery.Analysis;

/// <summary>
/// The XQuery Static Typing Feature (XQuery 3.1 §2.2.5.2): pessimistic static type checking,
/// run when <see cref="Execution.CompilationOptions.StrictTypeChecking"/> is set.
/// </summary>
/// <remarks>
/// <para>
/// Under the feature, an expression whose static type cannot be shown to fit where it is used
/// is a static error, even if the value it has at run time would fit:
/// <c>fn:function-arity(if (…) then fn:dateTime#2 else 1)</c> is XPTY0004 although the branch
/// taken may well be the function. A path whose static type is empty is XPST0005.
/// </para>
/// <para>
/// The checker is deliberately one-sided. It infers a type only where it can do so precisely:
/// literals, function items, constructors, the declared return types of built-in functions,
/// conditionals (as the union of their branches), sequences, FLWOR variables and path steps.
/// Anything else is "unknown", and an unknown part never causes an error. So an error is
/// raised only when a part of the type is KNOWN not to fit, never because the inference ran
/// out. That keeps it from rejecting queries merely because this checker is less thorough than
/// the formal semantics.
/// </para>
/// </remarks>
internal sealed class StaticTypingChecker : XQueryExpressionWalker
{
    private readonly List<AnalysisError> _errors = [];
    private readonly Dictionary<QName, SType> _variables = new();

    /// <summary>Checks <paramref name="expression"/>, appending any static type errors.</summary>
    public void Check(XQueryExpression expression, List<AnalysisError> errors)
    {
        Walk(expression);
        errors.AddRange(_errors);
    }

    private void Report(string code, string message)
    {
        // One report per message: an inner expression may be reached from more than one walk.
        if (!_errors.Exists(e => e.Code == code && e.Message == message))
            _errors.Add(new AnalysisError(code, message, null));
    }

    // ───────────────────────────── the type model ─────────────────────────────

    private enum K
    {
        Integer, Decimal, Double, Float, String, Boolean, UntypedAtomic, AnyUri, QName, OtherAtomic,
        Function, Map, Array,
        Document, Element, Attribute, Text, Comment, PI, Namespace, AnyNode,
    }

    private sealed record SItem(K Kind, string? Name = null, SType? Returns = null)
    {
        public bool IsNode => Kind >= K.Document;
        public bool IsAtomic => Kind <= K.OtherAtomic;
    }

    /// <summary>
    /// A static type: a union of precisely known item types, possibly with an unknown part,
    /// and what is known about its cardinality.
    /// </summary>
    private sealed class SType
    {
        public List<SItem> Items { get; init; } = [];

        /// <summary>Some part of the value is of a type this checker cannot name.</summary>
        public bool HasUnknown { get; init; }

        /// <summary>Known to be possibly empty (a branch that is precisely <c>()</c>, or <c>?</c>/<c>*</c>).</summary>
        public bool MayBeEmpty { get; init; }

        /// <summary>Known to be possibly more than one item.</summary>
        public bool MayBeMany { get; init; }

        public bool IsDefinitelyEmpty => !HasUnknown && Items.Count == 0;

        public static readonly SType Unknown = new() { HasUnknown = true };
        public static readonly SType Empty = new() { MayBeEmpty = true };

        public static SType One(K kind, string? name = null, SType? returns = null)
            => new() { Items = [new SItem(kind, name, returns)] };

        public static SType Union(SType a, SType b) => new()
        {
            Items = [.. a.Items, .. b.Items.Where(i => !a.Items.Contains(i))],
            HasUnknown = a.HasUnknown || b.HasUnknown,
            MayBeEmpty = a.MayBeEmpty || b.MayBeEmpty,
            MayBeMany = a.MayBeMany || b.MayBeMany,
        };

        /// <summary>The same item types, with the cardinality of a sequence of several values.</summary>
        public SType WithCardinality(bool mayBeEmpty, bool mayBeMany) => new()
        {
            Items = Items, HasUnknown = HasUnknown, MayBeEmpty = mayBeEmpty, MayBeMany = mayBeMany,
        };
    }

    private static SType FromSequenceType(XdmSequenceType? t)
    {
        if (t == null)
            return SType.Unknown;
        if (t.Occurrence == Occurrence.Zero || t.ItemType == ItemType.Empty)
            return SType.Empty;
        var item = t.ItemType switch
        {
            ItemType.Integer => new SItem(K.Integer),
            ItemType.Decimal => new SItem(K.Decimal),
            ItemType.Double => new SItem(K.Double),
            ItemType.Float => new SItem(K.Float),
            ItemType.String => new SItem(K.String),
            ItemType.Boolean => new SItem(K.Boolean),
            ItemType.UntypedAtomic => new SItem(K.UntypedAtomic),
            ItemType.AnyUri => new SItem(K.AnyUri),
            ItemType.QName => new SItem(K.QName),
            ItemType.Date or ItemType.DateTime or ItemType.Time or ItemType.Duration
                or ItemType.YearMonthDuration or ItemType.DayTimeDuration or ItemType.GYear
                or ItemType.GYearMonth or ItemType.GMonth or ItemType.GMonthDay or ItemType.GDay
                or ItemType.HexBinary => new SItem(K.OtherAtomic),
            ItemType.Function => new SItem(K.Function),
            ItemType.Map => new SItem(K.Map),
            ItemType.Array => new SItem(K.Array),
            ItemType.Document => new SItem(K.Document),
            ItemType.Element => new SItem(K.Element, t.ElementName),
            ItemType.Attribute => new SItem(K.Attribute, t.AttributeName),
            ItemType.Text => new SItem(K.Text),
            ItemType.Comment => new SItem(K.Comment),
            ItemType.ProcessingInstruction => new SItem(K.PI, t.PIName),
            ItemType.Namespace => new SItem(K.Namespace),
            ItemType.Node => new SItem(K.AnyNode),
            _ => null,
        };
        if (item == null)
            return SType.Unknown;
        return new SType
        {
            Items = [item],
            MayBeEmpty = t.Occurrence is Occurrence.ZeroOrOne or Occurrence.ZeroOrMore,
            MayBeMany = t.Occurrence is Occurrence.OneOrMore or Occurrence.ZeroOrMore,
        };
    }

    // ───────────────────────────── inference (no errors) ─────────────────────────────

    private SType Infer(XQueryExpression? expr, SType? context = null)
    {
        switch (expr)
        {
            case null:
                return SType.Empty;
            case IntegerLiteral:
                return SType.One(K.Integer);
            case DecimalLiteral:
                return SType.One(K.Decimal);
            case DoubleLiteral:
                return SType.One(K.Double);
            case StringLiteral:
                return SType.One(K.String);
            case BooleanLiteral:
                return SType.One(K.Boolean);
            case EmptySequence:
                return SType.Empty;
            case ContextItemExpression:
                return context ?? SType.Unknown;
            case VariableReference vr:
                return _variables.TryGetValue(vr.Name, out var vt) ? vt : SType.Unknown;
            case NamedFunctionRef:
                return SType.One(K.Function);
            case InlineFunctionExpression inline:
                return SType.One(K.Function, returns: inline.ReturnType != null
                    ? FromSequenceType(inline.ReturnType)
                    : Infer(inline.Body));
            case MapConstructor:
                return SType.One(K.Map);
            case ArrayConstructor:
                return SType.One(K.Array);
            case ElementConstructor or ComputedElementConstructor:
                return SType.One(K.Element);
            case AttributeConstructor or ComputedAttributeConstructor:
                return SType.One(K.Attribute);
            case TextConstructor:
                return SType.One(K.Text);
            case CommentConstructor:
                return SType.One(K.Comment);
            case DocumentConstructor:
                return SType.One(K.Document);
            case IfExpression ie:
                return SType.Union(Infer(ie.Then, context), Infer(ie.Else, context));
            case SequenceExpression se:
            {
                var parts = se.Items.Select(i => Infer(i, context)).ToList();
                if (parts.Count == 0)
                    return SType.Empty;
                var union = parts.Aggregate(SType.Union);
                var nonEmpty = parts.Count(p => !p.IsDefinitelyEmpty);
                return union.WithCardinality(
                    mayBeEmpty: parts.All(p => p.MayBeEmpty || p.IsDefinitelyEmpty),
                    mayBeMany: nonEmpty > 1 || parts.Any(p => p.MayBeMany));
            }
            case FunctionCallExpression fc when fc.ResolvedFunction is { } f:
                return FromSequenceType(f.ReturnType);
            case BinaryExpression { Operator: BinaryOperator.Equal or BinaryOperator.NotEqual
                    or BinaryOperator.LessThan or BinaryOperator.LessOrEqual
                    or BinaryOperator.GreaterThan or BinaryOperator.GreaterOrEqual
                    or BinaryOperator.GeneralEqual or BinaryOperator.GeneralNotEqual
                    or BinaryOperator.GeneralLessThan or BinaryOperator.GeneralLessOrEqual
                    or BinaryOperator.GeneralGreaterThan or BinaryOperator.GeneralGreaterOrEqual }:
                // A comparison yields xs:boolean, or () when a value comparison meets ().
                return SType.One(K.Boolean).WithCardinality(mayBeEmpty: false, mayBeMany: false);
            case PathExpression pe:
                return InferPath(pe, context, report: false);
            default:
                return SType.Unknown;
        }
    }

    // ───────────────────────────── paths and XPST0005 ─────────────────────────────

    private SType InferPath(PathExpression pe, SType? context, bool report)
    {
        SType current = pe.IsAbsolute
            ? SType.One(K.Document)
            : pe.InitialExpression != null ? Infer(pe.InitialExpression, context) : context ?? SType.Unknown;

        foreach (var step in pe.Steps)
        {
            var next = ApplyStep(current, step);
            if (report && next.IsDefinitelyEmpty && !current.HasUnknown && current.Items.Count > 0)
            {
                Report("XPST0005",
                    $"The step {step} selects nothing from {Describe(current)}: its static type is empty-sequence()");
                return SType.Empty;
            }
            current = next;
        }
        return current.WithCardinality(true, true);
    }

    private static SType ApplyStep(SType input, StepExpression step)
    {
        if (input.HasUnknown || input.Items.Any(i => i.Kind == K.AnyNode))
            return SType.Unknown;
        // Atomic or function items here are a type error, not an empty result; leave them to
        // the dynamic check rather than claiming emptiness.
        if (input.Items.Any(i => !i.IsNode))
            return SType.Unknown;

        var candidates = new List<SItem>();
        var unknownCandidates = false;
        foreach (var node in input.Items)
        {
            var reached = AxisCandidates(node, step.Axis, out var unknown);
            foreach (var c in reached)
                if (!candidates.Contains(c)) candidates.Add(c);
            unknownCandidates |= unknown;
        }
        if (unknownCandidates)
            return SType.Unknown;

        var principal = step.Axis switch
        {
            Axis.Attribute => K.Attribute,
            Axis.Namespace => K.Namespace,
            _ => K.Element,
        };
        var kept = new List<SItem>();
        foreach (var c in candidates)
        {
            switch (step.NodeTest)
            {
                case NameTest nt:
                    if (c.Kind != principal)
                        continue;
                    if (!nt.IsLocalNameWildcard && c.Name != null && c.Name != nt.LocalName)
                        continue;
                    kept.Add(c with { Name = nt.IsLocalNameWildcard ? c.Name : nt.LocalName });
                    break;
                case KindTest kt:
                    var want = kt.Kind switch
                    {
                        XdmNodeKind.None => (K?)null,
                        XdmNodeKind.Document => K.Document,
                        XdmNodeKind.Element => K.Element,
                        XdmNodeKind.Attribute => K.Attribute,
                        XdmNodeKind.Text => K.Text,
                        XdmNodeKind.Comment => K.Comment,
                        XdmNodeKind.ProcessingInstruction => K.PI,
                        XdmNodeKind.Namespace => K.Namespace,
                        _ => (K?)null,
                    };
                    if (want != null && c.Kind != want)
                        continue;
                    var kindName = kt.Name is { IsLocalNameWildcard: false } n ? n.LocalName : null;
                    if (kindName != null && c.Name != null && c.Name != kindName)
                        continue;
                    kept.Add(kindName != null ? c with { Name = kindName } : c);
                    break;
                default:
                    return SType.Unknown;
            }
        }
        return new SType { Items = kept, MayBeEmpty = true, MayBeMany = true };
    }

    /// <summary>The kinds of node <paramref name="axis"/> can reach from <paramref name="node"/>.</summary>
    private static IEnumerable<SItem> AxisCandidates(SItem node, Axis axis, out bool unknown)
    {
        unknown = false;
        var content = new[] { new SItem(K.Element), new SItem(K.Text), new SItem(K.Comment), new SItem(K.PI) };
        var hasContent = node.Kind is K.Document or K.Element;
        switch (axis)
        {
            case Axis.Self:
                return [node];
            case Axis.Child:
            case Axis.Descendant:
                return hasContent ? content : [];
            case Axis.DescendantOrSelf:
                return hasContent ? [node, .. content] : [node];
            case Axis.Attribute:
                return node.Kind == K.Element ? [new SItem(K.Attribute)] : [];
            case Axis.Namespace:
                return node.Kind == K.Element ? [new SItem(K.Namespace)] : [];
            case Axis.Parent:
            case Axis.Ancestor:
                return node.Kind == K.Document ? [] : [new SItem(K.Element), new SItem(K.Document)];
            case Axis.AncestorOrSelf:
                return node.Kind == K.Document ? [node] : [node, new SItem(K.Element), new SItem(K.Document)];
            default:
                unknown = true;
                return [];
        }
    }

    private static string Describe(SType t)
        => string.Join(" | ", t.Items.Select(i => i.Name != null ? $"{i.Kind}({i.Name})" : i.Kind.ToString()));

    // ───────────────────────────── conversion to a required type ─────────────────────────────

    /// <summary>
    /// Why a value of static type <paramref name="actual"/> cannot be supplied where
    /// <paramref name="required"/> is expected under the function conversion rules, or null.
    /// Only parts known precisely are judged.
    /// </summary>
    private static string? Mismatch(SType actual, XdmSequenceType required, SType? requiredFunctionReturn = null)
    {
        if (actual.Items.Count == 0 && !actual.MayBeEmpty)
            return null;
        var occ = required.Occurrence;
        if (actual.MayBeEmpty && occ is Occurrence.ExactlyOne or Occurrence.OneOrMore)
            return "it may be the empty sequence";
        if (actual.MayBeMany && occ is Occurrence.ExactlyOne or Occurrence.ZeroOrOne)
            return "it may contain more than one item";
        foreach (var item in actual.Items)
        {
            if (!ItemFits(item, required.ItemType))
                return $"it may be {item.Kind}, not {required.ItemType}";
            if (requiredFunctionReturn != null && item.Kind == K.Function && item.Returns is { } returns
                && Mismatch(returns, ToSequenceType(requiredFunctionReturn)) is { } why)
                return $"the function's result does not fit: {why}";
        }
        return null;
    }

    private static XdmSequenceType ToSequenceType(SType t) => t.Items.Count == 1 && t.Items[0].Kind == K.Boolean
        ? XdmSequenceType.Boolean
        : XdmSequenceType.ZeroOrMoreItems;

    private static bool ItemFits(SItem item, ItemType required)
    {
        switch (required)
        {
            case ItemType.Item:
                return true;
            case ItemType.Function:
                return item.Kind is K.Function or K.Map or K.Array;
            case ItemType.Map:
                return item.Kind == K.Map;
            case ItemType.Array:
                return item.Kind == K.Array;
            case ItemType.Node:
                return item.IsNode;
            case ItemType.Element or ItemType.Attribute or ItemType.Text or ItemType.Comment
                or ItemType.ProcessingInstruction or ItemType.Document or ItemType.Namespace:
                return item.Kind == K.AnyNode || item.Kind == required switch
                {
                    ItemType.Element => K.Element,
                    ItemType.Attribute => K.Attribute,
                    ItemType.Text => K.Text,
                    ItemType.Comment => K.Comment,
                    ItemType.ProcessingInstruction => K.PI,
                    ItemType.Document => K.Document,
                    _ => K.Namespace,
                };
        }

        // An atomic type is required: nodes atomize (to xs:untypedAtomic, which casts to anything),
        // and function items cannot be atomized at all.
        if (item.IsNode || item.Kind is K.UntypedAtomic or K.OtherAtomic or K.Array)
            return true;
        if (item.Kind is K.Function or K.Map)
            return false;
        return required switch
        {
            ItemType.AnyAtomicType => true,
            ItemType.Numeric => item.Kind is K.Integer or K.Decimal or K.Double or K.Float,
            ItemType.Double => item.Kind is K.Integer or K.Decimal or K.Double or K.Float,
            ItemType.Float => item.Kind is K.Integer or K.Decimal or K.Float,
            ItemType.Decimal => item.Kind is K.Integer or K.Decimal,
            ItemType.Integer => item.Kind == K.Integer,
            ItemType.String => item.Kind is K.String or K.AnyUri,
            ItemType.AnyUri => item.Kind is K.AnyUri,
            ItemType.Boolean => item.Kind == K.Boolean,
            ItemType.QName => item.Kind == K.QName,
            _ => true, // a type this checker does not model: no claim
        };
    }

    /// <summary>
    /// Function-typed parameters whose required signature the built-in declarations do not
    /// record (they declare plain function(*)): the required result type of the function item.
    /// </summary>
    private static SType? RequiredFunctionResult(FunctionCallExpression fc, int argIndex)
    {
        var name = fc.ResolvedFunction?.Name;
        if (name is not { } n || n.Namespace != FunctionNamespaces.Fn)
            return null;
        return (n.LocalName, argIndex) switch
        {
            ("filter", 1) => SType.One(K.Boolean),
            _ => null,
        };
    }

    // ───────────────────────────── the checks ─────────────────────────────

    public override object? VisitFunctionCallExpression(FunctionCallExpression fc)
    {
        if (fc.ResolvedFunction is { } f)
        {
            var parameters = f.Parameters;
            for (var i = 0; i < fc.Arguments.Count && i < parameters.Count; i++)
            {
                var arg = Infer(fc.Arguments[i]);
                if (arg.HasUnknown && arg.Items.Count == 0)
                    continue;
                if (Mismatch(arg, parameters[i].Type, RequiredFunctionResult(fc, i)) is { } why)
                    Report("XPTY0004",
                        $"Argument {i + 1} of {fc.Name.LocalName}() does not match its required type {parameters[i].Type}: {why}");
            }
        }
        return base.VisitFunctionCallExpression(fc);
    }

    // Functions whose zero-argument form uses the context item, which must be a node.
    private static readonly HashSet<string> NodeFocusFunctions = new(StringComparer.Ordinal)
    {
        "has-children", "name", "local-name", "namespace-uri", "root", "base-uri", "document-uri",
        "nilled", "node-name", "generate-id", "path",
    };

    public override object? VisitSimpleMapExpression(SimpleMapExpression sm)
    {
        if (sm.Right is FunctionCallExpression { Arguments.Count: 0 } focus
            && focus.Name.Namespace == FunctionNamespaces.Fn && NodeFocusFunctions.Contains(focus.Name.LocalName))
        {
            var left = Infer(sm.Left);
            var bad = left.Items.FirstOrDefault(i => !i.IsNode);
            if (bad != null)
                Report("XPTY0004", $"{focus.Name.LocalName}() needs a node as its context item, which may be {bad.Kind}");
        }
        return base.VisitSimpleMapExpression(sm);
    }

    public override object? VisitPathExpression(PathExpression pe)
    {
        InferPath(pe, null, report: true);
        return base.VisitPathExpression(pe);
    }

    public override object? VisitFlworExpression(FlworExpression flwor)
    {
        foreach (var clause in flwor.Clauses)
        {
            switch (clause)
            {
                case ForClause fc:
                    foreach (var b in fc.Bindings)
                    {
                        var seq = Infer(b.Expression);
                        _variables[b.Variable] = b.TypeDeclaration != null
                            ? FromSequenceType(b.TypeDeclaration)
                            : seq.WithCardinality(mayBeEmpty: b.AllowingEmpty, mayBeMany: false);
                    }
                    break;
                case LetClause lc:
                    foreach (var b in lc.Bindings)
                    {
                        var value = Infer(b.Expression);
                        if (b.TypeDeclaration != null)
                        {
                            if (Mismatch(value, b.TypeDeclaration) is { } why)
                                Report("XPTY0004", $"The value bound to ${b.Variable.LocalName} does not match its declared type {b.TypeDeclaration}: {why}");
                            else if (PiNameMismatch(value, b.TypeDeclaration) is { } piWhy)
                                Report("XPTY0004", $"The value bound to ${b.Variable.LocalName} does not match its declared type {b.TypeDeclaration}: {piWhy}");
                            _variables[b.Variable] = FromSequenceType(b.TypeDeclaration);
                        }
                        else
                            _variables[b.Variable] = value;
                    }
                    break;
                case WhereClause wc:
                    CheckEffectiveBooleanValue(Infer(wc.Condition), "where clause");
                    break;
            }
        }
        return base.VisitFlworExpression(flwor);
    }

    private static string? PiNameMismatch(SType value, XdmSequenceType declared)
    {
        if (declared.ItemType != ItemType.ProcessingInstruction || declared.PIName == null)
            return null;
        var other = value.Items.FirstOrDefault(i => i.Kind == K.PI && i.Name != null && i.Name != declared.PIName);
        return other == null ? null : $"it is processing-instruction({other.Name}), not processing-instruction({declared.PIName})";
    }

    /// <summary>
    /// The effective boolean value is defined for a single atomic value or a sequence starting
    /// with a node; a sequence that may hold more than one item, some of them atomic, has none.
    /// </summary>
    private void CheckEffectiveBooleanValue(SType t, string where)
    {
        if (t.MayBeMany && t.Items.Any(i => !i.IsNode))
            Report("XPTY0004", $"The {where} has no effective boolean value: it may be a sequence of more than one item that is not all nodes");
    }
}
