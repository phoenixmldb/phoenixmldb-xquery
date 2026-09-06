using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// A single step in a path expression.
/// </summary>
public sealed class StepExpression : XQueryExpression
{
    /// <summary>
    /// The axis (child, descendant, attribute, etc.).
    /// </summary>
    public required Axis Axis { get; init; }

    /// <summary>
    /// The node test (name test or kind test).
    /// </summary>
    public required NodeTest NodeTest { get; init; }

    /// <summary>
    /// Predicates to filter results.
    /// </summary>
    public required IReadOnlyList<XQueryExpression> Predicates { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitStepExpression(this);

    public override string ToString()
    {
        var axis = Axis switch
        {
            Axis.Child => "",
            Axis.Attribute => "@",
            Axis.DescendantOrSelf => "descendant-or-self::",
            Axis.Descendant => "descendant::",
            Axis.Parent => "parent::",
            Axis.Ancestor => "ancestor::",
            Axis.AncestorOrSelf => "ancestor-or-self::",
            Axis.FollowingSibling => "following-sibling::",
            Axis.PrecedingSibling => "preceding-sibling::",
            Axis.Following => "following::",
            Axis.Preceding => "preceding::",
            Axis.Self => "self::",
            Axis.Namespace => "namespace::",
            _ => ""
        };
        var preds = Predicates.Count > 0
            ? string.Concat(Predicates.Select(p => $"[{p}]"))
            : "";
        return $"{axis}{NodeTest}{preds}";
    }
}
