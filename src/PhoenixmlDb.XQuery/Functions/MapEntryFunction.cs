using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// map:entry($key as xs:anyAtomicType, $value as item()*) as map(*)
/// </summary>
public sealed class MapEntryFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Map, "entry");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "key"), Type = new() { ItemType = ItemType.AnyAtomicType, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "value"), Type = XdmSequenceType.ZeroOrMoreItems }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var key = QueryExecutionContext.AtomizeTyped(arguments[0]);
        var value = arguments[1];

        var result = MapHelper.NewMap();
        if (key != null)
        {
            result[key] = value;
        }

        return ValueTask.FromResult<object?>(result);
    }
}
