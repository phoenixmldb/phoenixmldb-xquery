namespace PhoenixmlDb.XQuery.Ast;

// ═══════════════════════════════════════════════════════════════════════════
// XQuery and XPath Full Text 3.0: the FTSelection tree (productions 199-220)
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>A node of an FTSelection.</summary>
/// <remarks>
/// A node lists the XQuery expressions it embeds (search words in braces, a weight, a window
/// size, a range bound) and can rebuild itself with each passed through a function, so the
/// analysis passes reach them without knowing the full-text grammar.
/// </remarks>
public abstract class FtSelectionNode
{
    /// <summary>The XQuery expressions this node embeds directly, not those of its children.</summary>
    public virtual IEnumerable<XQueryExpression> OwnExpressions => [];

    /// <summary>The child selections.</summary>
    public virtual IEnumerable<FtSelectionNode> Children => [];

    /// <summary>This node with each embedded expression (its own and its children's) mapped.</summary>
    public abstract FtSelectionNode Map(Func<XQueryExpression, XQueryExpression> map);

    /// <summary>Every expression embedded in this node and its descendants, in document order.</summary>
    public IEnumerable<XQueryExpression> AllExpressions()
    {
        foreach (var e in OwnExpressions) yield return e;
        foreach (var child in Children)
            foreach (var e in child.AllExpressions()) yield return e;
    }

    private protected static IReadOnlyList<FtSelectionNode> MapAll(IReadOnlyList<FtSelectionNode> nodes, Func<XQueryExpression, XQueryExpression> map, out bool changed)
    {
        changed = false;
        var result = new FtSelectionNode[nodes.Count];
        for (var i = 0; i < nodes.Count; i++)
        {
            result[i] = nodes[i].Map(map);
            changed |= !ReferenceEquals(result[i], nodes[i]);
        }
        return result;
    }
}

/// <summary><c>a ftor b ftor …</c> [201].</summary>
public sealed class FtOrNode : FtSelectionNode
{
    public required IReadOnlyList<FtSelectionNode> Operands { get; init; }
    public override IEnumerable<FtSelectionNode> Children => Operands;
    public override FtSelectionNode Map(Func<XQueryExpression, XQueryExpression> map)
    {
        var operands = MapAll(Operands, map, out var changed);
        return changed ? new FtOrNode { Operands = operands } : this;
    }
}

/// <summary><c>a ftand b ftand …</c> [202].</summary>
public sealed class FtAndNode : FtSelectionNode
{
    public required IReadOnlyList<FtSelectionNode> Operands { get; init; }
    public override IEnumerable<FtSelectionNode> Children => Operands;
    public override FtSelectionNode Map(Func<XQueryExpression, XQueryExpression> map)
    {
        var operands = MapAll(Operands, map, out var changed);
        return changed ? new FtAndNode { Operands = operands } : this;
    }
}

/// <summary><c>a not in b not in …</c> [203], applied left to right.</summary>
public sealed class FtMildNotNode : FtSelectionNode
{
    public required IReadOnlyList<FtSelectionNode> Operands { get; init; }
    public override IEnumerable<FtSelectionNode> Children => Operands;
    public override FtSelectionNode Map(Func<XQueryExpression, XQueryExpression> map)
    {
        var operands = MapAll(Operands, map, out var changed);
        return changed ? new FtMildNotNode { Operands = operands } : this;
    }
}

/// <summary><c>ftnot a</c> [204].</summary>
public sealed class FtNotNode : FtSelectionNode
{
    public required FtSelectionNode Operand { get; init; }
    public override IEnumerable<FtSelectionNode> Children => [Operand];
    public override FtSelectionNode Map(Func<XQueryExpression, XQueryExpression> map)
    {
        var operand = Operand.Map(map);
        return ReferenceEquals(operand, Operand) ? this : new FtNotNode { Operand = operand };
    }
}

/// <summary>A primary with its match options and weight [205]: <c>("dog" using stemming) weight {2}</c>.</summary>
public sealed class FtWithOptionsNode : FtSelectionNode
{
    public required FtSelectionNode Selection { get; init; }
    public FtMatchOptions? Options { get; init; }
    /// <summary>The weight expression [200]; it changes the score, never the matches.</summary>
    public XQueryExpression? Weight { get; init; }
    public override IEnumerable<XQueryExpression> OwnExpressions => Weight is null ? [] : [Weight];
    public override IEnumerable<FtSelectionNode> Children => [Selection];
    public override FtSelectionNode Map(Func<XQueryExpression, XQueryExpression> map)
    {
        var selection = Selection.Map(map);
        var weight = Weight is null ? null : map(Weight);
        return ReferenceEquals(selection, Selection) && ReferenceEquals(weight, Weight)
            ? this
            : new FtWithOptionsNode { Selection = selection, Options = Options, Weight = weight };
    }
}

/// <summary>
/// Search words [207] with an optional cardinality [211]: <c>"web site" any word occurs at least 2 times</c>.
/// </summary>
public sealed class FtWordsNode : FtSelectionNode
{
    /// <summary>The literal search string, when the words are a literal.</summary>
    public string? Text { get; init; }
    /// <summary>The expression in braces, when the words are computed: <c>{$terms}</c>.</summary>
    public XQueryExpression? Expression { get; init; }
    public FtAnyAllOption Mode { get; init; } = FtAnyAllOption.Any;
    /// <summary><c>occurs … times</c>.</summary>
    public FtRange? Times { get; init; }

    public override IEnumerable<XQueryExpression> OwnExpressions
    {
        get
        {
            if (Expression is not null) yield return Expression;
            if (Times is not null)
                foreach (var e in Times.Expressions) yield return e;
        }
    }

    public override FtSelectionNode Map(Func<XQueryExpression, XQueryExpression> map)
    {
        var expression = Expression is null ? null : map(Expression);
        var times = Times?.Map(map);
        return ReferenceEquals(expression, Expression) && ReferenceEquals(times, Times)
            ? this
            : new FtWordsNode { Text = Text, Expression = expression, Mode = Mode, Times = times };
    }
}

/// <summary><c>(# pragma #) { selection }</c> [209]. No pragma is recognized, so the selection stands; an empty one is an error.</summary>
public sealed class FtExtensionSelectionNode : FtSelectionNode
{
    public FtSelectionNode? Selection { get; init; }
    public override IEnumerable<FtSelectionNode> Children => Selection is null ? [] : [Selection];
    public override FtSelectionNode Map(Func<XQueryExpression, XQueryExpression> map)
    {
        var selection = Selection?.Map(map);
        return ReferenceEquals(selection, Selection) ? this : new FtExtensionSelectionNode { Selection = selection };
    }
}

/// <summary>A positional filter applied to a selection [213]: <c>selection window 5 words</c>.</summary>
public sealed class FtPositionFilterNode : FtSelectionNode
{
    public required FtSelectionNode Selection { get; init; }
    public required FtPositionFilterKind Kind { get; init; }
    /// <summary>The unit of a window, a distance or a scope.</summary>
    public FtUnit Unit { get; init; } = FtUnit.Words;
    /// <summary>The size of a window.</summary>
    public XQueryExpression? Size { get; init; }
    /// <summary>The range of a distance.</summary>
    public FtRange? Range { get; init; }

    public override IEnumerable<XQueryExpression> OwnExpressions
    {
        get
        {
            if (Size is not null) yield return Size;
            if (Range is not null)
                foreach (var e in Range.Expressions) yield return e;
        }
    }

    public override IEnumerable<FtSelectionNode> Children => [Selection];

    public override FtSelectionNode Map(Func<XQueryExpression, XQueryExpression> map)
    {
        var selection = Selection.Map(map);
        var size = Size is null ? null : map(Size);
        var range = Range?.Map(map);
        return ReferenceEquals(selection, Selection) && ReferenceEquals(size, Size) && ReferenceEquals(range, Range)
            ? this
            : new FtPositionFilterNode { Selection = selection, Kind = Kind, Unit = Unit, Size = size, Range = range };
    }
}

public enum FtPositionFilterKind
{
    Ordered,
    Window,
    Distance,
    SameScope,
    DifferentScope,
    AtStart,
    AtEnd,
    EntireContent,
}

/// <summary>The unit of FTWindow and FTDistance [217], and of FTScope [219] (sentence, paragraph).</summary>
public enum FtUnit
{
    Words,
    Sentences,
    Paragraphs,
}

public enum FtRangeKind
{
    Exactly,
    AtLeast,
    AtMost,
    FromTo,
}

/// <summary>FTRange [212]: <c>exactly N</c>, <c>at least N</c>, <c>at most N</c>, <c>from M to N</c>.</summary>
public sealed class FtRange
{
    public required FtRangeKind Kind { get; init; }
    /// <summary>N, or M for <c>from M to N</c>.</summary>
    public required XQueryExpression First { get; init; }
    /// <summary>N of <c>from M to N</c>.</summary>
    public XQueryExpression? Second { get; init; }

    public IEnumerable<XQueryExpression> Expressions => Second is null ? [First] : [First, Second];

    public FtRange Map(Func<XQueryExpression, XQueryExpression> map)
    {
        var first = map(First);
        var second = Second is null ? null : map(Second);
        return ReferenceEquals(first, First) && ReferenceEquals(second, Second)
            ? this
            : new FtRange { Kind = Kind, First = first, Second = second };
    }
}
