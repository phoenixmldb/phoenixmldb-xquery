using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;


/// <summary>
/// XQuery 3.1 map functions.
/// </summary>

/// <summary>
/// Helper for map key lookups that handles xs:untypedAtomic / xs:string cross-type matching.
/// Per XPath 3.1 §14.4, map keys use eq semantics where xs:untypedAtomic is promoted to xs:string.
/// </summary>
internal static class MapKeyHelper
{
    /// <summary>
    /// Tries to get a value from a map, handling cross-type lookup between xs:untypedAtomic and xs:string.
    /// </summary>
    internal static bool TryGetValue(IDictionary<object, object?> map, object key, out object? value)
    {
        if (map.TryGetValue(key, out value))
            return true;
        // A map keyed by XdmMapKeyComparer already matches op:same-key across types
        // (untypedAtomic/anyURI/string, numerics, durations) and hashes equal keys equally,
        // so its miss is final. Everything below is for maps built with another comparer —
        // the XSLT engine builds some with the default one. For numerics and durations it is
        // a scan of the whole map, which made every missed numeric lookup O(n).
        if (HasSameKeyComparer(map))
            return false;
        // Cross-type fallback: untypedAtomic eq string per XPath semantics
        if (key is XsUntypedAtomic ua)
            return map.TryGetValue(ua.Value, out value);
        if (key is string s)
        {
            if (map.TryGetValue(new XsUntypedAtomic(s), out value))
                return true;
            // xs:anyURI eq xs:string per XPath 3.1
            if (map.TryGetValue(new XsAnyUri(s), out value))
                return true;
        }
        // xs:anyURI matches xs:string
        if (key is XsAnyUri uri)
        {
            if (map.TryGetValue(uri.Value, out value))
                return true;
        }
        // Numeric cross-type: integer 4 matches double 4.0, float 4.0f, decimal 4m
        if (key is int or long or double or float or decimal or System.Numerics.BigInteger)
        {
            // NaN never matches any key (including NaN) because NaN eq NaN is false per XPath 3.1
            if ((key is double dKey && double.IsNaN(dKey)) || (key is float fKey && float.IsNaN(fKey)))
            {
                return false;
            }
            // Numeric keys match under op:same-key semantics (exact mathematical value),
            // NOT loose double-coercion: xs:decimal('1.0000000000100000000001') must NOT
            // match xs:double('1.00000000001') even though both collapse to the same double.
            // (map-remove-016.) Delegate to the shared exact key comparer.
            foreach (var (k, v) in map)
            {
                if (k is int or long or double or float or decimal or System.Numerics.BigInteger)
                {
                    if (Execution.XdmMapKeyComparer.Instance.Equals(key, k))
                    {
                        value = v;
                        return true;
                    }
                }
            }
        }
        // Duration cross-type: xs:duration, xs:yearMonthDuration, xs:dayTimeDuration
        if (key is TimeSpan || key is YearMonthDuration || key is XsDuration)
        {
            foreach (var (k, v) in map)
            {
                if (DurationEquals(key, k))
                {
                    value = v;
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>Whether the map's own lookup already implements op:same-key.</summary>
    private static bool HasSameKeyComparer(IDictionary<object, object?> map) => map switch
    {
        OrderedXdmMap ordered => ordered.Comparer is XdmMapKeyComparer,
        Dictionary<object, object?> dictionary => dictionary.Comparer is XdmMapKeyComparer,
        _ => false,
    };

    private static bool DurationEquals(object a, object b)
    {
        // xs:yearMonthDuration('P12M') == xs:yearMonthDuration('P1Y')
        if (a is YearMonthDuration ymA && b is YearMonthDuration ymB)
            return ymA.TotalMonths == ymB.TotalMonths;
        if (a is TimeSpan tsA && b is TimeSpan tsB)
            return tsA == tsB;
        // xs:duration cross-type: XsDuration can match YearMonthDuration or TimeSpan
        // xs:duration('P1Y') == xs:yearMonthDuration('P12M')
        if (a is XsDuration durA)
        {
            if (b is YearMonthDuration ymB2)
                return durA.DayTime == TimeSpan.Zero && durA.TotalMonths == ymB2.TotalMonths;
            if (b is TimeSpan tsB2)
                return durA.TotalMonths == 0 && durA.DayTime == tsB2;
            if (b is XsDuration durB)
                return durA.TotalMonths == durB.TotalMonths && durA.DayTime == durB.DayTime;
        }
        if (b is XsDuration durB2)
        {
            if (a is YearMonthDuration ymA2)
                return durB2.DayTime == TimeSpan.Zero && durB2.TotalMonths == ymA2.TotalMonths;
            if (a is TimeSpan tsA2)
                return durB2.TotalMonths == 0 && durB2.DayTime == tsA2;
        }
        return false;
    }

    /// <summary>
    /// Checks if a map contains a key, handling cross-type lookup between xs:untypedAtomic and xs:string.
    /// </summary>
    internal static bool ContainsKey(IDictionary<object, object?> map, object key)
    {
        return TryGetValue(map, key, out _);
    }

    /// <summary>
    /// Removes any existing entry whose key matches the given key under XPath cross-type semantics.
    /// Returns true if an entry was removed.
    /// </summary>
    internal static bool RemoveByKey(IDictionary<object, object?> map, object key)
    {
        if (map.Remove(key))
            return true;
        if (HasSameKeyComparer(map))
            return false;

        // Find the actual dictionary key that matches cross-type
        object? matchingKey = null;
        if (TryGetValue(map, key, out _))
        {
            // Need to find the actual key object in the dictionary
            foreach (var k in map.Keys)
            {
                if (TryGetValue(new Dictionary<object, object?> { { k, null } }, key, out _))
                {
                    matchingKey = k;
                    break;
                }
            }
        }
        if (matchingKey != null)
        {
            map.Remove(matchingKey);
            return true;
        }
        return false;
    }
}
