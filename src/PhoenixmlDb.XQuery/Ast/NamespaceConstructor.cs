using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

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
