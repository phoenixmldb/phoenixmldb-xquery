using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// A single grouping specification.
/// </summary>
public sealed class GroupingSpec
{
    public required QName Variable { get; init; }
    public XQueryExpression? Expression { get; init; }
    public XdmSequenceType? TypeDeclaration { get; init; }
    public string? Collation { get; init; }

    public override string ToString()
    {
        var expr = Expression != null ? $" := {Expression}" : "";
        var coll = Collation != null ? $" collation \"{Collation}\"" : "";
        return $"${Variable.LocalName}{expr}{coll}";
    }
}
