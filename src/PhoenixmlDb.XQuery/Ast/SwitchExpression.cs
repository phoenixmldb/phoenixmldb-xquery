using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Switch expression (XQuery 3.0+).
/// </summary>
public sealed class SwitchExpression : XQueryExpression
{
    public required XQueryExpression Operand { get; init; }
    public required IReadOnlyList<SwitchCase> Cases { get; init; }
    public required XQueryExpression Default { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitSwitchExpression(this);

    public override string ToString()
    {
        var cases = string.Join(" ", Cases);
        return $"switch ({Operand}) {cases} default return {Default}";
    }
}
