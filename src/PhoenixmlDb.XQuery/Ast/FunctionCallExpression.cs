using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Function call expression (fn:name(args)).
/// </summary>
public sealed class FunctionCallExpression : XQueryExpression
{
    /// <summary>
    /// Function name (QName).
    /// </summary>
    public required QName Name { get; set; }

    /// <summary>
    /// Arguments (may be rewritten by FunctionResolver to reorder keyword arguments).
    /// </summary>
    public required IReadOnlyList<XQueryExpression> Arguments { get; set; }

    /// <summary>
    /// Resolved function (set during static analysis).
    /// </summary>
    public XQueryFunction? ResolvedFunction { get; internal set; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitFunctionCallExpression(this);

    public override string ToString()
    {
        var name = Name.Prefix != null ? $"{Name.Prefix}:{Name.LocalName}" : Name.LocalName;
        var args = string.Join(", ", Arguments);
        return $"{name}({args})";
    }
}
