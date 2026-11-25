using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Inline function expression (function($x) { $x + 1 }).
/// </summary>
public sealed class InlineFunctionExpression : XQueryExpression
{
    public required IReadOnlyList<FunctionParameter> Parameters { get; init; }
    public XdmSequenceType? ReturnType { get; init; }
    public required XQueryExpression Body { get; init; }

    /// <summary>
    /// <c>true</c> for <c>%updating</c>, <c>false</c> for <c>%simple</c>, <c>null</c> for neither
    /// (XQUF 3.0 §7.9).
    /// </summary>
    internal bool? DeclaredUpdating { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitInlineFunctionExpression(this);

    public override string ToString()
    {
        var @params = string.Join(", ", Parameters.Select(p =>
            $"${p.Name.LocalName}" + (p.Type != null ? $" as {p.Type}" : "")));
        var ret = ReturnType != null ? $" as {ReturnType}" : "";
        return $"function({@params}){ret} {{ {Body} }}";
    }
}
