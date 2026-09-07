using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// map:of-pairs($pairs as map(*)*) as map(*)
/// Merges a sequence of single-entry maps into one map (XPath 4.0).
/// </summary>
public sealed class MapOfPairsFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Map, "of-pairs");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "pairs"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var pairs = arguments[0];
        var result = MapHelper.NewMap();
        if (pairs is object?[] arr)
        {
            foreach (var pair in arr)
            {
                if (pair is IDictionary<object, object?> map)
                    foreach (var kvp in map)
                        result[kvp.Key] = kvp.Value;
            }
        }
        else if (pairs is IDictionary<object, object?> singleMap)
        {
            foreach (var kvp in singleMap)
                result[kvp.Key] = kvp.Value;
        }
        return ValueTask.FromResult<object?>(result);
    }
}
