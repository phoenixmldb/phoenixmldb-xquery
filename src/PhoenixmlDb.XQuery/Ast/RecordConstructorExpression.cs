using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// XPath 4.0: record { name: value, ... } constructor expression.
/// Creates a map with string keys from field names.
/// </summary>
public sealed class RecordConstructorExpression : XQueryExpression
{
    public required IReadOnlyList<(string Name, XQueryExpression Value)> Fields { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitRecordConstructorExpression(this);
}
