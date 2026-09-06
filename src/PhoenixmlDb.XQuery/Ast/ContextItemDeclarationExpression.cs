using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// declare context item := expr;
/// </summary>
public sealed class ContextItemDeclarationExpression : XQueryExpression
{
    public XQueryExpression? DefaultValue { get; init; }
    /// <summary>The declared item type constraint (from "as &lt;type&gt;"), or null if none.</summary>
    public XdmSequenceType? TypeConstraint { get; init; }
    /// <summary>True when "external" keyword is present.</summary>
    public bool IsExternal { get; init; }
    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor) => visitor.VisitContextItemDeclaration(this);
}
