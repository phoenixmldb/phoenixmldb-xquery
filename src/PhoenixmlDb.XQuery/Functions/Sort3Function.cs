using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:sort($input, $collation, $key) as item()*
/// </summary>
public sealed class Sort3Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "sort");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "input"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "collation"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "key"), Type = new XdmSequenceType { ItemType = ItemType.Item, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var items = SequenceHelper.Flatten(arguments[0]);
        var cmp = Sort2Function.ResolveCollation(arguments[1], context);
        var callable = arguments[2]
            ?? throw new XQueryRuntimeException("XPTY0004", "Third argument to sort must be callable");

        // Compute sort keys for each item using CallableCoercion (supports functions, maps, arrays)
        // Per spec, sort keys are atomized: fn:data() is applied to each key value
        var keyed = new List<(object? item, List<object?> keys)>();
        foreach (var item in items)
        {
            var keyResult = await CallableCoercion.InvokeUnaryAsync(callable, item, context).ConfigureAwait(false);
            var rawKeys = SequenceHelper.Flatten(keyResult);
            // Atomize each key value (nodes → xs:untypedAtomic, arrays → recursive atomize)
            var keys = new List<object?>();
            foreach (var k in rawKeys)
            {
                var atomized = DataFunction.Atomize(k);
                if (atomized is object?[] seq)
                    keys.AddRange(seq);
                else if (atomized != null)
                    keys.Add(atomized);
            }
            keyed.Add((item, keys));
        }

        // Stable sort: add original index as tiebreaker (List.Sort is not stable in .NET)
        var indexed = new List<(object? item, List<object?> keys, int idx)>(keyed.Count);
        for (int i = 0; i < keyed.Count; i++)
            indexed.Add((keyed[i].item, keyed[i].keys, i));
        indexed.Sort((a, b) =>
        {
            var c = SortHelper.CompareKeySequences(a.keys, b.keys, cmp);
            return c != 0 ? c : a.idx.CompareTo(b.idx);
        });
        return indexed.Select(k => k.item).ToArray();
    }
}
