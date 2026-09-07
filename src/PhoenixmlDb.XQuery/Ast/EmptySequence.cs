using System.Numerics;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Empty sequence literal ().
/// </summary>
public sealed class EmptySequence : XQueryExpression
{
    public static EmptySequence Instance { get; } = new();

    private EmptySequence() { }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitEmptySequence(this);

    public override string ToString() => "()";
}
