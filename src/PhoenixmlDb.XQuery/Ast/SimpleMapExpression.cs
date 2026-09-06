namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Simple map expression (expr ! expr) - XQuery 3.0+.
/// </summary>
public sealed class SimpleMapExpression : XQueryExpression
{
    public required XQueryExpression Left { get; init; }
    public required XQueryExpression Right { get; init; }

    /// <summary>
    /// When true, this expression originated from a path step (/) rather than the
    /// simple map operator (!). Path steps require document-order sorting of results.
    /// </summary>
    public bool IsPathStep { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitSimpleMapExpression(this);

    public override string ToString() => $"({Left} ! {Right})";
}
