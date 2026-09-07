using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// Shared implementation of <c>fn:highest</c> and <c>fn:lowest</c> (XPath 4.0 §14.5).
///
///     fn:highest($input     as item()*,
///                $collation as xs:string?                          := fn:default-collation(),
///                $key       as (fn(item()) as xs:anyAtomicType*)?  := fn:data#1) as item()*
///
/// The COLLATION is the second argument and the key function the third. This engine
/// previously declared arity 1-2 with the key second, so <c>highest#3</c> did not exist and
/// <c>highest($seq, $key)</c> bound a key function into the collation position — reported by
/// Martin Honnen, 2026-08-23.
///
/// The old implementation also coerced every key with <c>Convert.ToDouble</c>, so
/// <c>highest(("apple","banana"))</c> threw an unhandled <c>System.FormatException</c> and
/// killed the process rather than raising an XQuery error. Keys now go through the same
/// machinery <c>fn:sort</c> uses — <c>CallableCoercion</c> for the key, <c>DataFunction.Atomize</c>
/// for its values, and <c>SortHelper.CompareKeySequences</c> for the comparison — so strings,
/// dates and mixed numerics all order correctly and under the requested collation.
/// </summary>
internal static class HighestLowestHelper
{
    internal static async ValueTask<object?> FindExtremeAsync(
        object? input,
        StringComparison comparison,
        object? keyCallable,
        bool highest,
        Ast.ExecutionContext context)
    {
        var items = SequenceHelper.Flatten(input);
        if (items.Count == 0) return Array.Empty<object?>();

        var keyed = new List<(object? Item, List<object?> Keys)>(items.Count);
        foreach (var item in items)
        {
            // The default $key is fn:data#1, which is what atomizing the item itself does.
            var raw = keyCallable is null
                ? item
                : await CallableCoercion.InvokeUnaryAsync(keyCallable, item, context).ConfigureAwait(false);

            var keys = new List<object?>();
            foreach (var k in SequenceHelper.Flatten(raw))
            {
                var atomized = DataFunction.Atomize(k);
                if (atomized is object?[] seq) keys.AddRange(seq);
                else if (atomized is not null) keys.Add(atomized);
            }
            keyed.Add((item, keys));
        }

        var best = keyed[0].Keys;
        for (var i = 1; i < keyed.Count; i++)
        {
            var c = SortHelper.CompareKeySequences(keyed[i].Keys, best, comparison);
            if (highest ? c > 0 : c < 0) best = keyed[i].Keys;
        }

        // Every item tied at the extreme is returned, in input order — fn:highest is not
        // "one item", it is "the items whose key is highest".
        var result = new List<object?>();
        foreach (var (item, keys) in keyed)
        {
            if (SortHelper.CompareKeySequences(keys, best, comparison) == 0)
                result.Add(item);
        }
        return result.ToArray();
    }
}
