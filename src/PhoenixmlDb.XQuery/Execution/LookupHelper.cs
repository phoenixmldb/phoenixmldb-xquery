using System.Numerics;
using System.Text;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.Xdm.Serialization;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Functions;
using PhoenixmlDb.XQuery.Optimizer;

namespace PhoenixmlDb.XQuery.Execution;

/// <summary>
/// Shared lookup logic for LookupOperator and UnaryLookupOperator.
/// </summary>
internal static class LookupHelper
{
    public static async IAsyncEnumerable<object?> WildcardLookup(object? item)
    {
        await Task.CompletedTask;
        if (item is IDictionary<object, object?> map)
        {
            foreach (var value in map.Values)
            {
                if (value is object?[] valSeq)
                    foreach (var v in valSeq)
                        yield return v;
                else if (value != null)
                    yield return value;
                // null represents () — yield nothing
            }
        }
        else if (item is IList<object?> arr)
        {
            foreach (var member in arr)
            {
                if (member is object?[] memberSeq)
                    foreach (var mv in memberSeq)
                        yield return mv;
                else if (member != null)
                    yield return member;
                // null represents () — yield nothing (empty sequence)
            }
        }
        else
        {
            throw new XQueryRuntimeException("XPTY0004",
                $"Lookup '?*' requires a map or array, got {item?.GetType().Name ?? "null"}");
        }
    }

    public static async IAsyncEnumerable<object?> LookupByKey(object? item, object? rawKey)
    {
        await Task.CompletedTask;
        // Atomize the key
        var keyVal = QueryExecutionContext.Atomize(rawKey);

        if (item is IDictionary<object, object?> m)
        {
            // For maps, try the key as-is first (preserves string keys from parse-json),
            // then fall back to integer-coerced key (for maps with integer keys like map{1:"a"}).
            object? val = null;
            bool found = keyVal != null && m.TryGetValue(keyVal, out val);
            if (!found && keyVal is string ks && long.TryParse(ks, out var kl))
                found = m.TryGetValue(kl, out val);
            if (found)
            {
                if (val is object?[] valSeq)
                    foreach (var v in valSeq)
                        yield return v;
                else if (val != null)
                    yield return val;
                // null represents () — yield nothing (empty sequence)
            }
        }
        else if (item is IList<object?> a)
        {
            // Arrays require xs:integer keys — decimal/double/float is XPTY0004
            if (keyVal is decimal || keyVal is double || keyVal is float)
                throw new XQueryRuntimeException("XPTY0004",
                    $"Array lookup requires an xs:integer key, got {keyVal?.GetType().Name} value {keyVal}");
            var index = Convert.ToInt32(keyVal) - 1; // XQuery arrays are 1-based
            if (index < 0 || index >= a.Count)
                throw new XQueryRuntimeException("FOAY0001",
                    $"Array index {index + 1} out of bounds (array size: {a.Count})");
            var member = a[index];
            if (member is object?[] memberSeq)
                foreach (var mv in memberSeq)
                    yield return mv;
            else if (member != null)
                yield return member;
            // null represents () — yield nothing (empty sequence)
        }
        else if (item is Delegate || item is PhoenixmlDb.XQuery.Ast.XQueryFunction)
        {
            // Functions can be called with lookup syntax — fn?(key) is fn(key)
            // For non-map/non-array functions, this is a type error
            throw new XQueryRuntimeException("XPTY0004",
                $"Lookup requires a map or array, got function");
        }
        else
        {
            throw new XQueryRuntimeException("XPTY0004",
                $"Lookup requires a map or array, got {item?.GetType().Name ?? "null"}");
        }
    }
}
