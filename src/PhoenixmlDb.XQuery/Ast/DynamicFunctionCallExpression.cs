using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Dynamic function call (expr(args)).
/// </summary>
public sealed class DynamicFunctionCallExpression : XQueryExpression
{
    public required XQueryExpression FunctionExpression { get; init; }
    public required IReadOnlyList<XQueryExpression> Arguments { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitDynamicFunctionCallExpression(this);

    public override string ToString()
    {
        var args = string.Join(", ", Arguments);
        return $"{FunctionExpression}({args})";
    }
}
