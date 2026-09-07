using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:divide-decimals($dividend as xs:decimal, $divisor as xs:decimal,
///                    $scale as xs:integer) as xs:decimal
/// Decimal division with explicit scale (XPath 4.0).
/// </summary>
public sealed class DivideDecimalsFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "divide-decimals");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Decimal, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "dividend"), Type = new() { ItemType = ItemType.Decimal, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "divisor"), Type = new() { ItemType = ItemType.Decimal, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "scale"), Type = XdmSequenceType.Integer }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var dividend = Convert.ToDecimal(arguments[0]);
        var divisor = Convert.ToDecimal(arguments[1]);
        var scale = Convert.ToInt32(arguments[2]);
        if (divisor == 0) throw new InvalidOperationException("FOAR0002: Division by zero");
        var result = Math.Round(dividend / divisor, scale, MidpointRounding.AwayFromZero);
        return ValueTask.FromResult<object?>(result);
    }
}
