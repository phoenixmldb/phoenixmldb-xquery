using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// Shared basis for <c>fn:all-equal</c>, <c>fn:all-different</c> and <c>fn:duplicate-values</c>
/// (XPath 4.0). The spec defines all three in terms of the same value equality as
/// <c>fn:distinct-values</c>, so all three delegate to <see cref="CollationValueComparer"/>
/// rather than each inventing a comparison of its own.
///
/// Each previously compared <c>.ToString()</c> of the atomized value, with two consequences.
/// No collation could be honoured, so the arity-2 forms the spec requires did not exist at all.
/// And values of DIFFERENT types compared equal whenever their lexical forms matched, so
/// <c>all-equal((1, "1"))</c> was true and <c>all-different((1, "1"))</c> was false — both
/// backwards. The comparer keeps strings under the requested collation and numerics under
/// numeric promotion, leaving 1 and "1" correctly distinct.
///
/// Found by auditing the collation-taking functions after Martin Honnen reported the same
/// class of defect in fn:highest/fn:lowest on 2026-08-23. None of these three had any tests.
/// </summary>
internal static class ValueDistinctnessHelper
{
    internal static List<object?> AtomizedItems(object? arg, Ast.ExecutionContext context)
    {
        var result = new List<object?>();
        foreach (var item in SequenceHelper.Flatten(arg))
            result.Add(DistinctValuesFunction.AtomizeItem(item, context));
        return result;
    }

    internal static bool AllEqual(object? arg, StringComparison comparison, Ast.ExecutionContext context)
    {
        var items = AtomizedItems(arg, context);
        // Empty and singleton sequences are vacuously all-equal.
        if (items.Count <= 1) return true;

        var comparer = new CollationValueComparer(comparison);
        for (var i = 1; i < items.Count; i++)
        {
            if (!comparer.Equals(items[0], items[i])) return false;
        }
        return true;
    }

    internal static bool AllDifferent(object? arg, StringComparison comparison, Ast.ExecutionContext context)
    {
        var seen = new HashSet<object?>(new CollationValueComparer(comparison));
        foreach (var item in AtomizedItems(arg, context))
        {
            if (!seen.Add(item)) return false;
        }
        return true;
    }

    internal static object?[] DuplicateValues(object? arg, StringComparison comparison, Ast.ExecutionContext context)
    {
        var comparer = new CollationValueComparer(comparison);
        var distinct = new List<object?>();              // first occurrence of each value, in order
        var seen = new HashSet<object?>(comparer);
        var alreadyReported = new HashSet<object?>(comparer);
        var result = new List<object?>();

        foreach (var item in AtomizedItems(arg, context))
        {
            if (seen.Add(item))
            {
                distinct.Add(item);
                continue;
            }

            // Reported when the SECOND occurrence is seen, so the result is in order of first
            // duplication and a value repeated three times still appears once.
            if (!alreadyReported.Add(item)) continue;

            // Report the value AS FIRST WRITTEN, not the later occurrence that revealed the
            // duplication. Under a case-blind collation ('a','A','b') duplicates on 'a', and
            // returning 'A' would be surprising — fn:distinct-values likewise keeps the first
            // of a set of values that are equal under the collation, so the two agree.
            // The scan runs once per duplicated value, over distinct values only.
            var first = item;
            foreach (var candidate in distinct)
            {
                if (comparer.Equals(candidate, item)) { first = candidate; break; }
            }
            result.Add(first);
        }
        return result.ToArray();
    }
}
