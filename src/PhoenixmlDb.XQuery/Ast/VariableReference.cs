using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Variable reference ($varname).
/// </summary>
public sealed class VariableReference : XQueryExpression
{
    public required QName Name { get; set; }

    /// <summary>
    /// Resolved variable binding (set during static analysis).
    /// </summary>
    public VariableBinding? ResolvedBinding { get; internal set; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitVariableReference(this);

    public override string ToString()
    {
        var name = Name.Prefix != null ? $"{Name.Prefix}:{Name.LocalName}" : Name.LocalName;
        return $"${name}";
    }
}
