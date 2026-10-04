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

    /// <summary>
    /// When true, this element constructor appears as a direct child of another element
    /// constructor (not inside an enclosed expression {…}). Used to determine whether
    /// copy-namespaces semantics apply during parent construction.
    /// </summary>
    public bool IsDirectChild { get; init; }

    /// <summary>
    /// The boundary-space policy of the module this constructor was written in, when that module
    /// declares one (XQuery 3.1 §4.3); null leaves it to the planning context. A function body is
    /// planned later, in a fresh context that knows nothing of its module's prolog, so the policy
    /// travels with the constructor.
    /// </summary>
    public bool? BoundarySpacePreserve { get; init; }

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
