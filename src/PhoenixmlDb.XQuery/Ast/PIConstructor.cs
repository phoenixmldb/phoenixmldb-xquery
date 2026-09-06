using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Processing instruction constructor (<?target content?> or processing-instruction { name } { value }).
/// </summary>
public sealed class PIConstructor : XQueryExpression
{
    public string? DirectTarget { get; init; }
    public XQueryExpression? TargetExpression { get; init; }
    public required XQueryExpression Value { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitPIConstructor(this);

    public override string ToString()
    {
        if (DirectTarget != null)
            return $"<?{DirectTarget} {Value}?>";
        return $"processing-instruction {{ {TargetExpression} }} {{ {Value} }}";
    }
}
