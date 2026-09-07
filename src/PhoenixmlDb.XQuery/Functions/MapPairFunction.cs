using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// map:pair($key as xs:anyAtomicType, $value as item()*) as map(xs:anyAtomicType, item()*)
/// Creates a single-entry map (synonym for map:entry) (XPath 4.0).
/// </summary>
public sealed class MapPairFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Map, "pair");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "key"), Type = new() { ItemType = ItemType.AnyAtomicType, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "value"), Type = XdmSequenceType.ZeroOrMoreItems }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var key = QueryExecutionContext.AtomizeTyped(arguments[0]) ?? arguments[0]!;
        var result = MapHelper.NewMap();
        result[key] = arguments[1];
        return ValueTask.FromResult<object?>(result);
    }
}
