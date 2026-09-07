using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;


// ═══════════════════════════════════════════════════════════════════════════
// XQuery Update Facility 3.0 — AST node definitions
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Base class for all XQuery Update expressions.
/// Update expressions return Pending Update Lists (PULs) instead of values.
/// </summary>
public abstract class UpdateExpression : XQueryExpression
{
    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor) => default!;
}
