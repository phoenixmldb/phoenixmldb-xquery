using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// array:sort($array, $collation, $key) as array(*)
/// </summary>
public sealed class ArraySort3Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "sort");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "collation"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "key"), Type = new XdmSequenceType { ItemType = ItemType.Item, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var array = arguments[0] as IList<object?> ?? [];
        var callable = arguments[2]
            ?? throw new XQueryRuntimeException("XPTY0004", "Third argument to array:sort must be callable");

        // Resolve the collation argument (arg[1]); an absent/empty collation uses the
        // dynamic context's default collation. Previously the collation was accepted but
        // ignored, so the key comparison always fell back to codepoint ordering.
        var collationArg = arguments[1]?.ToString();
        var comparison = string.IsNullOrEmpty(collationArg)
            ? CollationHelper.GetDefaultComparison(context)
            : CollationHelper.ResolveAndGetComparison(collationArg, context);

        var keyed = new List<(object? item, List<object?> keys)>();
        foreach (var item in array)
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

        keyed.Sort((a, b) => SortHelper.CompareKeySequences(a.keys, b.keys, comparison));
        return keyed.Select(k => k.item).ToList();
    }
}
