using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// map:merge($maps as map(*)*) as map(*)
/// </summary>
public sealed class MapMergeFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Map, "merge");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "maps"), Type = new() { ItemType = ItemType.Map, Occurrence = Occurrence.ZeroOrMore } }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var result = MapHelper.NewMap();

        // The single-argument form uses the default duplicate-key policy "use-first":
        // when the same key appears in more than one input map, the entry from the
        // earliest map in the sequence wins (XPath/XQuery F&O §17.1.5). Implemented by
        // only inserting a key the first time it is seen.
        // Handle single map (FunctionCallOperator unwraps single-item sequences)
        if (arguments[0] is IDictionary<object, object?> singleMap)
        {
            foreach (var (key, value) in singleMap)
            {
                if (!result.ContainsKey(key))
                    result[key] = value;
            }
        }
        else if (arguments[0] is IEnumerable<object?> maps)
        {
            foreach (var map in maps)
            {
                if (map is IDictionary<object, object?> dict)
                {
                    foreach (var (key, value) in dict)
                    {
                        if (!result.ContainsKey(key))
                            result[key] = value;
                    }
                }
            }
        }

        return ValueTask.FromResult<object?>(result);
    }
}
