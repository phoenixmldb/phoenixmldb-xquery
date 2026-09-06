using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Computed element constructor (element { name } { content }).
/// </summary>
public sealed class ComputedElementConstructor : XQueryExpression
{
    /// <summary>
    /// Name expression (evaluates to QName or string).
    /// Only used when <see cref="StaticName"/> is null.
    /// </summary>
    public required XQueryExpression NameExpression { get; init; }

    /// <summary>
    /// When the name is a static EQName (e.g. <c>element Q{uri}local { }</c>),
    /// this carries the fully-resolved QName so the runtime doesn't need to re-parse
    /// a lossy string encoding. Null when the name is computed from an expression.
    /// </summary>
    public QName? StaticName { get; init; }

    /// <summary>
    /// Content expression.
    /// </summary>
    public required XQueryExpression ContentExpression { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitComputedElementConstructor(this);

    public override string ToString()
        => $"element {{ {NameExpression} }} {{ {ContentExpression} }}";
}
