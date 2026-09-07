using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// array:sort($array as array(*)) as array(*)
/// </summary>
public sealed class ArraySortFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "sort");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var array = arguments[0] as IList<object?> ?? [];
        // array:sort#1 sorts by atomized value using the *default collation* for strings
        // (F&O §17.3.1). Without this it fell back to codepoint ordering and ignored a
        // `declare default collation`, so a case-blind default produced the wrong order.
        var comparison = CollationHelper.GetDefaultComparison(context);
        var keyed = array.Select(item => (item, keys: AtomizeForSortKeys(item))).ToList();
        keyed.Sort((a, b) => SortHelper.CompareKeySequences(a.keys, b.keys, comparison));
        return ValueTask.FromResult<object?>(keyed.Select(k => k.item).ToList());
    }

    internal static List<object?> AtomizeForSortKeys(object? item)
    {
        // Use fn:data() atomization: nodes → xs:untypedAtomic, arrays → recursively atomize members
        var atomized = DataFunction.Atomize(item);
        if (atomized is null) return [];
        if (atomized is object?[] seq)
        {
            var result = new List<object?>(seq.Length);
            foreach (var s in seq)
                if (s != null) result.Add(s);
            return result;
        }
        return [atomized];
    }
}
