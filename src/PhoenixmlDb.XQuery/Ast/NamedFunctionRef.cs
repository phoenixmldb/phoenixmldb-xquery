using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Named function reference (fn:name#arity).
/// </summary>
public sealed class NamedFunctionRef : XQueryExpression
{
    public required QName Name { get; set; }
    public required int Arity { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitNamedFunctionRef(this);

    public override string ToString()
    {
        var name = Name.Prefix != null ? $"{Name.Prefix}:{Name.LocalName}" : Name.LocalName;
        return $"{name}#{Arity}";
    }
}
