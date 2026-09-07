using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Path expression (e.g., //customer/name, /root/@id, $var/path).
/// </summary>
public sealed class PathExpression : XQueryExpression
{
    /// <summary>
    /// True if path starts with / (absolute path from document root).
    /// </summary>
    public bool IsAbsolute { get; init; }

    /// <summary>
    /// Optional initial expression for paths like $var/path where the path starts
    /// from a non-step expression (variable, function call, etc.).
    /// </summary>
    public XQueryExpression? InitialExpression { get; init; }

    /// <summary>
    /// The steps in the path.
    /// </summary>
    public required IReadOnlyList<StepExpression> Steps { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitPathExpression(this);

    public override string ToString()
    {
        var prefix = IsAbsolute ? "/" : "";
        var init = InitialExpression != null ? $"{InitialExpression}/" : "";
        return init + prefix + string.Join("/", Steps);
    }
}
