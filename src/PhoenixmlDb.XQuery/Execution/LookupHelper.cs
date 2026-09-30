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
            // Arrays require xs:integer keys; an untyped key is cast, as function conversion
            // does for array:get. Anything else is XPTY0004. Only decimal/double/float were
            // rejected, so a string key ($a?first) or a date reached Convert.ToInt32 and
            // surfaced .NET's format/cast exceptions (QT3 Lookup-009, -010).
            if (keyVal is Xdm.XsUntypedAtomic untypedKey)
                keyVal = long.TryParse(untypedKey.Value.Trim(), System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsedKey)
                    ? parsedKey
                    : throw new XQueryRuntimeException("FORG0001", $"'{untypedKey.Value}' cannot be cast to xs:integer for an array lookup");
            if (keyVal is not (int or long or System.Numerics.BigInteger or Xdm.XsTypedInteger))
                throw new XQueryRuntimeException("XPTY0004",
                    $"Array lookup requires an xs:integer key, got {XdmShape.TypeNameOf(keyVal)}");
            var index = Functions.ArrayHelper.ClampPosition(keyVal) - 1L; // XQuery arrays are 1-based
            if (index < 0 || index >= a.Count)
                throw new XQueryRuntimeException("FOAY0001",
                    $"Array index {index + 1} out of bounds (array size: {a.Count})");
            var member = a[(int)index];
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
