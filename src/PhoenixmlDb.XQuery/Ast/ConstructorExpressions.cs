using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Direct element constructor (<element>content</element>).
/// </summary>
public sealed class ElementConstructor : XQueryExpression
{
    /// <summary>
    /// Element name.
    /// </summary>
    public required QName Name { get; init; }

    /// <summary>
    /// Attribute expressions.
    /// </summary>
    public required IReadOnlyList<XQueryExpression> Attributes { get; init; }

    /// <summary>
    /// Content expressions (child elements, text, etc.).
    /// </summary>
    public required IReadOnlyList<XQueryExpression> Content { get; init; }

    /// <summary>
    /// Namespace declarations on this element.
    /// </summary>
    public IReadOnlyList<NamespaceDeclaration>? NamespaceDeclarations { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitElementConstructor(this);

    public override string ToString()
    {
        var name = Name.Prefix != null ? $"{Name.Prefix}:{Name.LocalName}" : Name.LocalName;
        var attrs = Attributes.Count > 0 ? " " + string.Join(" ", Attributes) : "";
        var content = Content.Count > 0 ? string.Join("", Content) : "";
        return $"<{name}{attrs}>{content}</{name}>";
    }
}

/// <summary>
/// Computed element constructor (element { name } { content }).
/// </summary>
public sealed class ComputedElementConstructor : XQueryExpression
{
    /// <summary>
    /// Name expression (evaluates to QName or string).
    /// </summary>
    public required XQueryExpression NameExpression { get; init; }

    /// <summary>
    /// Content expression.
    /// </summary>
    public required XQueryExpression ContentExpression { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitComputedElementConstructor(this);

    public override string ToString()
        => $"element {{ {NameExpression} }} {{ {ContentExpression} }}";
}

/// <summary>
/// Direct attribute constructor (name="value").
/// </summary>
public sealed class AttributeConstructor : XQueryExpression
{
    public required QName Name { get; init; }
    public required XQueryExpression Value { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitAttributeConstructor(this);

    public override string ToString()
    {
        var name = Name.Prefix != null ? $"{Name.Prefix}:{Name.LocalName}" : Name.LocalName;
        return $"{name}=\"{Value}\"";
    }
}

/// <summary>
/// Computed attribute constructor (attribute { name } { value }).
/// </summary>
public sealed class ComputedAttributeConstructor : XQueryExpression
{
    public required XQueryExpression NameExpression { get; init; }
    public required XQueryExpression ValueExpression { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitComputedAttributeConstructor(this);

    public override string ToString()
        => $"attribute {{ {NameExpression} }} {{ {ValueExpression} }}";
}

/// <summary>
/// Text constructor (text { value }).
/// </summary>
public sealed class TextConstructor : XQueryExpression
{
    public required XQueryExpression Value { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitTextConstructor(this);

    public override string ToString() => $"text {{ {Value} }}";
}

/// <summary>
/// Comment constructor (<!-- comment --> or comment { value }).
/// </summary>
public sealed class CommentConstructor : XQueryExpression
{
    public required XQueryExpression Value { get; init; }
    public bool IsDirect { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitCommentConstructor(this);

    public override string ToString()
        => IsDirect ? $"<!--{Value}-->" : $"comment {{ {Value} }}";
}

/// <summary>
/// Processing instruction constructor (<?target content?> or processing-instruction { name } { value }).
/// </summary>
public sealed class PIConstructor : XQueryExpression
{
    public string? DirectTarget { get; init; }
    public XQueryExpression? TargetExpression { get; init; }
    public required XQueryExpression Value { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitPIConstructor(this);

    public override string ToString()
    {
        if (DirectTarget != null)
            return $"<?{DirectTarget} {Value}?>";
        return $"processing-instruction {{ {TargetExpression} }} {{ {Value} }}";
    }
}

/// <summary>
/// Document constructor (document { content }).
/// </summary>
public sealed class DocumentConstructor : XQueryExpression
{
    public required XQueryExpression Content { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitDocumentConstructor(this);

    public override string ToString() => $"document {{ {Content} }}";
}

/// <summary>
/// Namespace constructor (namespace { prefix } { uri }).
/// </summary>
public sealed class NamespaceConstructor : XQueryExpression
{
    public string? DirectPrefix { get; init; }
    public XQueryExpression? PrefixExpression { get; init; }
    public required XQueryExpression UriExpression { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitNamespaceConstructor(this);

    public override string ToString()
    {
        if (DirectPrefix != null)
            return $"namespace {DirectPrefix} {{ {UriExpression} }}";
        return $"namespace {{ {PrefixExpression} }} {{ {UriExpression} }}";
    }
}

/// <summary>
/// Namespace declaration in element constructor.
/// </summary>
public sealed class NamespaceDeclaration
{
    public required string Prefix { get; init; }
    public required string Uri { get; init; }
}

/// <summary>
/// Map constructor (XQuery 3.1): map { key: value, ... }.
/// </summary>
public sealed class MapConstructor : XQueryExpression
{
    public required IReadOnlyList<MapEntry> Entries { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitMapConstructor(this);

    public override string ToString()
    {
        var entries = string.Join(", ", Entries.Select(e => $"{e.Key}: {e.Value}"));
        return $"map {{ {entries} }}";
    }
}

/// <summary>
/// Entry in a map constructor.
/// </summary>
public sealed class MapEntry
{
    public required XQueryExpression Key { get; init; }
    public required XQueryExpression Value { get; init; }
}

/// <summary>
/// Array constructor (XQuery 3.1): [ item, item, ... ] or array { expr }.
/// </summary>
public sealed class ArrayConstructor : XQueryExpression
{
    public ArrayConstructorKind Kind { get; init; }
    public required IReadOnlyList<XQueryExpression> Members { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitArrayConstructor(this);

    public override string ToString()
    {
        if (Kind == ArrayConstructorKind.Square)
            return $"[ {string.Join(", ", Members)} ]";
        return $"array {{ {string.Join(", ", Members)} }}";
    }
}

/// <summary>
/// Kind of array constructor.
/// </summary>
public enum ArrayConstructorKind
{
    /// <summary>
    /// Square bracket: [ a, b, c ]
    /// </summary>
    Square,

    /// <summary>
    /// Curly brace: array { expr }
    /// </summary>
    Curly
}

/// <summary>
/// Lookup expression for maps and arrays (expr?key or expr?*).
/// </summary>
public sealed class LookupExpression : XQueryExpression
{
    public required XQueryExpression Base { get; init; }
    public XQueryExpression? Key { get; init; } // null for wildcard lookup (?*)

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitLookupExpression(this);

    public override string ToString()
        => Key != null ? $"{Base}?{Key}" : $"{Base}?*";
}

/// <summary>
/// XPath 4.0: record { name: value, ... } constructor expression.
/// Creates a map with string keys from field names.
/// </summary>
public sealed class RecordConstructorExpression : XQueryExpression
{
    public required IReadOnlyList<(string Name, XQueryExpression Value)> Fields { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor) => default!;
}

/// <summary>
/// XPath 4.0: keyword argument in a function call (name := value).
/// </summary>
public sealed class KeywordArgument : XQueryExpression
{
    public required string Name { get; init; }
    public required XQueryExpression Value { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor) => default!;
}

/// <summary>
/// Unary lookup (context item lookup): ?key or ?*.
/// </summary>
public sealed class UnaryLookupExpression : XQueryExpression
{
    public XQueryExpression? Key { get; init; } // null for wildcard lookup (?*)

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitUnaryLookupExpression(this);

    public override string ToString()
        => Key != null ? $"?{Key}" : "?*";
}
