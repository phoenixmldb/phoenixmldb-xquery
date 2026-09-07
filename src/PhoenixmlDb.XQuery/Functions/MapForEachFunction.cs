using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// map:for-each($map as map(*), $action as function(xs:anyAtomicType, item()*) as item()*) as item()*
/// </summary>
public sealed class MapForEachFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Map, "for-each");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "map"), Type = new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "action"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var map = arguments[0] as IDictionary<object, object?>;
        var action = arguments[1] as XQueryFunction;

        if (map == null || action == null)
            return Array.Empty<object>();

        var results = new List<object?>();

        foreach (var (key, value) in map)
        {
            var result = await action.InvokeAsync([key, value], context);
            if (result is IEnumerable<object?> seq)
            {
                results.AddRange(seq);
            }
            else if (result != null)
            {
                results.Add(result);
            }
        }

        return results.ToArray();
    }
}
