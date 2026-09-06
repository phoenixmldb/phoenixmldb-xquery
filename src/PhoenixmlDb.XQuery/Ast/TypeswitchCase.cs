using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// A case in a typeswitch expression.
/// </summary>
public sealed class TypeswitchCase
{
    public QName? Variable { get; init; }
    public required IReadOnlyList<XdmSequenceType> Types { get; init; }
    public required XQueryExpression Result { get; init; }

    public override string ToString()
    {
        var types = string.Join(" | ", Types);
        var variable = Variable != null ? $" ${Variable.Value.LocalName}" : "";
        return $"case{variable} {types} return {Result}";
    }
}
