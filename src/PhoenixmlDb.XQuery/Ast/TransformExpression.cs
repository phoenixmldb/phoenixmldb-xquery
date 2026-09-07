using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// copy $var := $source modify (update-expr) return $result
/// Functional update — creates a modified copy without side effects.
/// </summary>
public sealed class TransformExpression : UpdateExpression
{
    /// <summary>Copy bindings: variable := source pairs.</summary>
    public required IReadOnlyList<TransformCopyBinding> CopyBindings { get; init; }
    /// <summary>The modify clause — update expressions applied to the copies.</summary>
    public required XQueryExpression ModifyExpr { get; init; }
    /// <summary>The return clause — the result expression.</summary>
    public required XQueryExpression ReturnExpr { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor) => visitor.VisitTransformExpression(this);
}
