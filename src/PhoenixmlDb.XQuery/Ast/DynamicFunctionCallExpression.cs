using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Dynamic function call (expr(args)).
/// </summary>
public sealed class DynamicFunctionCallExpression : XQueryExpression
{
    public required XQueryExpression FunctionExpression { get; init; }
    public required IReadOnlyList<XQueryExpression> Arguments { get; init; }

    /// <summary>
    /// <c>invoke updating $f(...)</c> (XQUF 3.0 §5.5): an updating expression, which may call an
    /// updating function or a simple one. An ordinary dynamic call of an updating function is
    /// XUDY0038 (§7.11).
    /// </summary>
    internal bool IsUpdatingInvocation { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitDynamicFunctionCallExpression(this);

    public override string ToString()
    {
        var args = string.Join(", ", Arguments);
        return $"{FunctionExpression}({args})";
    }
}
