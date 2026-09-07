using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

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
