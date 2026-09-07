using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Typeswitch expression.
/// </summary>
public sealed class TypeswitchExpression : XQueryExpression
{
    public required XQueryExpression Operand { get; init; }
    public required IReadOnlyList<TypeswitchCase> Cases { get; init; }
    public required TypeswitchDefault Default { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitTypeswitchExpression(this);

    public override string ToString()
    {
        var cases = string.Join(" ", Cases);
        return $"typeswitch ({Operand}) {cases} default {Default}";
    }
}
