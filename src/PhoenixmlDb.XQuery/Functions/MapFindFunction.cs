using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// map:find($input as item()*, $key as xs:anyAtomicType) as array(*)
/// </summary>
public sealed class MapFindFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Map, "find");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "input"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "key"), Type = new() { ItemType = ItemType.AnyAtomicType, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var input = arguments[0];
        var key = QueryExecutionContext.AtomizeTyped(arguments[1]);

        var results = new List<object?>();
        FindInItems(input, key, results);

        return ValueTask.FromResult<object?>(results);
    }

    private static void FindInItems(object? item, object? key, List<object?> results)
    {
        switch (item)
        {
            case IDictionary<object, object?> map:
                if (key != null && MapKeyHelper.TryGetValue(map, key, out var value))
                {
                    results.Add(value);
                }
                // Recursively search values
                foreach (var v in map.Values)
                {
                    FindInItems(v, key, results);
                }
                break;

            case IList<object?> array:
                foreach (var member in array)
                {
                    FindInItems(member, key, results);
                }
                break;

            case IEnumerable<object?> seq:
                foreach (var member in seq)
                {
                    FindInItems(member, key, results);
                }
                break;
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// XPath/XQuery 4.0 new map functions
// ═══════════════════════════════════════════════════════════════════════════
