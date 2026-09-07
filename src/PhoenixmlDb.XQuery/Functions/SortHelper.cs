using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// Shared sort comparison logic for fn:sort and array:sort.
/// Uses MinFunction.CompareValues for proper XDM atomic comparison.
/// Throws XPTY0004 for incompatible types per spec.
/// </summary>
internal static class SortHelper
{
    /// <summary>
    /// Sort items by their atomized value using XDM comparison rules.
    /// For mixed types (numeric vs string), throws XPTY0004.
    /// </summary>
    public static void SortByAtomicKey(List<object?> items, StringComparison stringComparison = StringComparison.Ordinal)
    {
        if (items.Count <= 1) return;

        // Atomize each item to get its sort key
        var keyed = new List<(object? item, List<object?> keys)>(items.Count);
        foreach (var item in items)
        {
            var atomized = Atomize(item);
            keyed.Add((item, atomized));
        }

        // Stable sort: add original index as tiebreaker (List.Sort is not stable in .NET)
        var indexed = new List<(object? item, List<object?> keys, int idx)>(keyed.Count);
        for (int i = 0; i < keyed.Count; i++)
            indexed.Add((keyed[i].item, keyed[i].keys, i));
        indexed.Sort((a, b) =>
        {
            var c = CompareKeySequences(a.keys, b.keys, stringComparison);
            return c != 0 ? c : a.idx.CompareTo(b.idx);
        });
        for (int i = 0; i < items.Count; i++)
            items[i] = indexed[i].item;
    }

    /// <summary>
    /// Compares two key sequences lexicographically per fn:sort spec.
    /// Empty key sorts before any value. Sequences are compared element-by-element.
    /// </summary>
    public static int CompareKeySequences(List<object?> a, List<object?> b, StringComparison stringComparison = StringComparison.Ordinal)
    {
        int len = Math.Max(a.Count, b.Count);
        for (int i = 0; i < len; i++)
        {
            if (i >= a.Count) return -1; // shorter sorts first
            if (i >= b.Count) return 1;
            var cmp = CompareAtomicSortKeys(a[i], b[i], stringComparison);
            if (cmp != 0) return cmp;
        }
        return 0;
    }

    private static int CompareAtomicSortKeys(object? a, object? b, StringComparison stringComparison = StringComparison.Ordinal)
    {
        if (a is null && b is null) return 0;
        if (a is null) return -1;
        if (b is null) return 1;

        // NaN sorts equal to NaN
        if (IsNaN(a) && IsNaN(b)) return 0;
        // NaN sorts before any other numeric value
        if (IsNaN(a)) return -1;
        if (IsNaN(b)) return 1;

        // In fn:sort, xs:untypedAtomic is NOT promoted — it is comparable only with
        // other xs:untypedAtomic or xs:string values. Mixing with numeric is XPTY0004.
        bool aIsUA = a is Xdm.XsUntypedAtomic;
        bool bIsUA = b is Xdm.XsUntypedAtomic;
        bool aIsNum = a is int or long or double or float or decimal or System.Numerics.BigInteger;
        bool bIsNum = b is int or long or double or float or decimal or System.Numerics.BigInteger;
        if ((aIsUA && bIsNum) || (bIsUA && aIsNum))
            throw new XQueryRuntimeException("XPTY0004",
                $"Values of type '{a.GetType().Name}' and '{b.GetType().Name}' are not comparable in sort");
        // xs:untypedAtomic vs xs:untypedAtomic or xs:string: compare as strings
        // xs:untypedAtomic vs anything else (date, duration, etc.): XPTY0004
        if (aIsUA || bIsUA)
        {
            bool otherIsStringLike = aIsUA
                ? (b is string or Xdm.XsUntypedAtomic or Xdm.XsAnyUri)
                : (a is string or Xdm.XsUntypedAtomic or Xdm.XsAnyUri);
            if (!otherIsStringLike)
                throw new XQueryRuntimeException("XPTY0004",
                    $"Values of type '{a.GetType().Name}' and '{b.GetType().Name}' are not comparable in sort");
            return string.Compare(a.ToString(), b.ToString(), stringComparison);
        }

        try
        {
            return MinFunction.CompareValues(a, b, stringComparison);
        }
        catch (XQueryRuntimeException ex) when (ex.ErrorCode is "FORG0006")
        {
            // Incompatible types in sort → XPTY0004
            throw new XQueryRuntimeException("XPTY0004",
                $"Values of type '{a.GetType().Name}' and '{b.GetType().Name}' are not comparable");
        }
    }

    private static bool IsNaN(object? val) => val is double d && double.IsNaN(d)
        || val is float f && float.IsNaN(f);

    private static List<object?> Atomize(object? item)
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
