using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Default case in a typeswitch expression.
/// </summary>
public sealed class TypeswitchDefault
{
    public QName? Variable { get; init; }
    public required XQueryExpression Result { get; init; }

    public override string ToString()
    {
        var variable = Variable != null ? $" ${Variable.Value.LocalName}" : "";
        return $"{variable} return {Result}";
    }
}
