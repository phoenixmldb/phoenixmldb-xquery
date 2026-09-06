using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Document constructor (document { content }).
/// </summary>
public sealed class DocumentConstructor : XQueryExpression
{
    public required XQueryExpression Content { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitDocumentConstructor(this);

    public override string ToString() => $"document {{ {Content} }}";
}
