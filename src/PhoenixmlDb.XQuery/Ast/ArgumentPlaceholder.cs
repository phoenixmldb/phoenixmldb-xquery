using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Placeholder for partial function application (fn(?, 1)).
/// </summary>
public sealed class ArgumentPlaceholder : XQueryExpression
{
    public static ArgumentPlaceholder Instance { get; } = new();

    private ArgumentPlaceholder() { }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitArgumentPlaceholder(this);

    public override string ToString() => "?";
}
