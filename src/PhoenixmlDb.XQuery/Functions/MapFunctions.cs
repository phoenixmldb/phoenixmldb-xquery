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
            var keyDouble = Convert.ToDouble(key, System.Globalization.CultureInfo.InvariantCulture);
            foreach (var (k, v) in map)
            {
                if (k is int or long or double or float or decimal or System.Numerics.BigInteger)
                {
                    var kDouble = Convert.ToDouble(k, System.Globalization.CultureInfo.InvariantCulture);
                    if (keyDouble == kDouble)
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

/// <summary>
/// Shared validation helpers for map functions.
/// </summary>
internal static class MapHelper
{
    /// <summary>Creates a new map with XDM-aware key comparison.</summary>
    internal static Dictionary<object, object?> NewMap()
        => new(Execution.XdmMapKeyComparer.Instance);

    /// <summary>Copies a map preserving XDM-aware key comparison.</summary>
    internal static Dictionary<object, object?> CopyMap(IDictionary<object, object?> source)
        => new(source, Execution.XdmMapKeyComparer.Instance);

    /// <summary>
    /// Validates that the argument is a single map. Throws XPTY0004 for non-maps, sequences of maps, empty sequences.
    /// </summary>
    internal static IDictionary<object, object?> RequireMap(object? arg, string funcName)
    {
        if (arg is IDictionary<object, object?> map)
            return map;
        // sequence of maps, function items, non-map sequences, empty sequence → XPTY0004
        throw new XQueryRuntimeException("XPTY0004",
            $"First argument to {funcName} must be a single map");
    }

    /// <summary>
    /// Validates that the key argument is a single atomic value (not a sequence).
    /// </summary>
    internal static object RequireSingleAtomicKey(object? arg, string funcName)
    {
        var key = QueryExecutionContext.AtomizeTyped(arg);
        if (key is null)
            throw new XQueryRuntimeException("XPTY0004",
                $"Key argument to {funcName} must be a single atomic value");
        if (key is object?[] arr)
        {
            if (arr.Length == 1 && arr[0] is not null)
                return arr[0]!;
            throw new XQueryRuntimeException("XPTY0004",
                $"Key argument to {funcName} must be a single atomic value, got sequence of {arr.Length} items");
        }
        return key;
    }
}

/// <summary>
/// map:merge($maps as map(*)*) as map(*)
/// </summary>
public sealed class MapMergeFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Map, "merge");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "maps"), Type = new() { ItemType = ItemType.Map, Occurrence = Occurrence.ZeroOrMore } }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var result = MapHelper.NewMap();

        // Handle single map (FunctionCallOperator unwraps single-item sequences)
        if (arguments[0] is IDictionary<object, object?> singleMap)
        {
            foreach (var (key, value) in singleMap)
            {
                result[key] = value;
            }
        }
        else if (arguments[0] is IEnumerable<object?> maps)
        {
            foreach (var map in maps)
            {
                if (map is IDictionary<object, object?> dict)
                {
                    foreach (var (key, value) in dict)
                    {
                        result[key] = value;
                    }
                }
            }
        }

        return ValueTask.FromResult<object?>(result);
    }
}

/// <summary>
/// map:merge($maps as map(*)*, $options as map(*)) as map(*)
/// </summary>
public sealed class MapMerge2Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Map, "merge");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "maps"), Type = new() { ItemType = ItemType.Map, Occurrence = Occurrence.ZeroOrMore } },
         new() { Name = new QName(NamespaceId.None, "options"), Type = new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne } }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var duplicatesPolicy = "use-first";
        if (arguments.Count > 1)
        {
            if (arguments[1] is null)
                throw new XQueryRuntimeException("XPTY0004",
                    "Second argument to map:merge must be a map, got empty sequence");
            if (arguments[1] is IDictionary<object, object?> opts
                && opts.TryGetValue("duplicates", out var dup) && dup != null)
                duplicatesPolicy = dup.ToString()!;
        }

        var result = MapHelper.NewMap();

        void MergeMaps(IDictionary<object, object?> dict)
        {
            foreach (var (key, value) in dict)
            {
                if (MapKeyHelper.ContainsKey(result, key))
                {
                    switch (duplicatesPolicy)
                    {
                        case "use-first": break;
                        case "use-last":
                        case "use-any":
                            MapKeyHelper.RemoveByKey(result, key);
                            result[key] = value;
                            break;
                        case "combine":
                            if (MapKeyHelper.TryGetValue(result, key, out var existing))
                            {
                                MapKeyHelper.RemoveByKey(result, key);
                                // Combine into a sequence (object?[]), NOT a List<object?> (which is XDM array)
                                if (existing is object?[] existingSeq)
                                {
                                    if (value is object?[] valSeq)
                                    {
                                        var combined = new object?[existingSeq.Length + valSeq.Length];
                                        existingSeq.CopyTo(combined, 0);
                                        valSeq.CopyTo(combined, existingSeq.Length);
                                        result[key] = combined;
                                    }
                                    else
                                    {
                                        var combined = new object?[existingSeq.Length + 1];
                                        existingSeq.CopyTo(combined, 0);
                                        combined[existingSeq.Length] = value;
                                        result[key] = combined;
                                    }
                                }
                                else
                                {
                                    if (value is object?[] valSeq2)
                                    {
                                        var combined = new object?[1 + valSeq2.Length];
                                        combined[0] = existing;
                                        valSeq2.CopyTo(combined, 1);
                                        result[key] = combined;
                                    }
                                    else
                                        result[key] = new object?[] { existing, value };
                                }
                            }
                            break;
                        case "reject":
                            throw new XQueryRuntimeException("FOJS0003",
                                $"Duplicate key in map:merge with duplicates='reject'");
                    }
                }
                else
                    result[key] = value;
            }
        }

        if (arguments[0] is IDictionary<object, object?> singleMap)
            MergeMaps(singleMap);
        else if (arguments[0] is IEnumerable<object?> maps)
            foreach (var map in maps)
                if (map is IDictionary<object, object?> dict)
                    MergeMaps(dict);

        return ValueTask.FromResult<object?>(result);
    }
}

/// <summary>
/// map:size($map as map(*)) as xs:integer
/// </summary>
public sealed class MapSizeFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Map, "size");
    public override XdmSequenceType ReturnType => XdmSequenceType.Integer;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "map"), Type = new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne } }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var map = arguments[0] as IDictionary<object, object?>;
        return ValueTask.FromResult<object?>(map?.Count ?? 0);
    }
}

/// <summary>
/// map:keys($map as map(*)) as xs:anyAtomicType*
/// </summary>
public sealed class MapKeysFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Map, "keys");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.AnyAtomicType, Occurrence = Occurrence.ZeroOrMore };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "map"), Type = new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne } }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var map = arguments[0] as IDictionary<object, object?>;
        return ValueTask.FromResult<object?>(map?.Keys.ToArray() ?? Array.Empty<object>());
    }
}

/// <summary>
/// map:contains($map as map(*), $key as xs:anyAtomicType) as xs:boolean
/// </summary>
public sealed class MapContainsFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Map, "contains");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "map"), Type = new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "key"), Type = new() { ItemType = ItemType.AnyAtomicType, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var map = MapHelper.RequireMap(arguments[0], "map:contains");
        var key = MapHelper.RequireSingleAtomicKey(arguments[1], "map:contains");
        return ValueTask.FromResult<object?>(MapKeyHelper.ContainsKey(map, key));
    }
}

/// <summary>
/// map:get($map as map(*), $key as xs:anyAtomicType) as item()*
/// </summary>
public sealed class MapGetFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Map, "get");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "map"), Type = new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "key"), Type = new() { ItemType = ItemType.AnyAtomicType, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var map = MapHelper.RequireMap(arguments[0], "map:get");
        var key = MapHelper.RequireSingleAtomicKey(arguments[1], "map:get");

        if (MapKeyHelper.TryGetValue(map, key, out var value))
            return ValueTask.FromResult(value);

        return ValueTask.FromResult<object?>(null);
    }
}

/// <summary>
/// map:put($map as map(*), $key as xs:anyAtomicType, $value as item()*) as map(*)
/// </summary>
public sealed class MapPutFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Map, "put");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "map"), Type = new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "key"), Type = new() { ItemType = ItemType.AnyAtomicType, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "value"), Type = XdmSequenceType.ZeroOrMoreItems }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var map = arguments[0] as IDictionary<object, object?>;
        var key = QueryExecutionContext.AtomizeTyped(arguments[1]);
        var value = arguments[2];

        var result = new Dictionary<object, object?>(
            map ?? MapHelper.NewMap(),
            Execution.XdmMapKeyComparer.Instance);

        if (key != null)
        {
            // Remove any existing entry with cross-type match
            // (untypedAtomic/string, numeric cross-type, NaN, duration, anyURI)
            MapKeyHelper.RemoveByKey(result, key);
            result[key] = value;
        }

        return ValueTask.FromResult<object?>(result);
    }
}

/// <summary>
/// map:remove($map as map(*), $keys as xs:anyAtomicType*) as map(*)
/// </summary>
public sealed class MapRemoveFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Map, "remove");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "map"), Type = new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "keys"), Type = new() { ItemType = ItemType.AnyAtomicType, Occurrence = Occurrence.ZeroOrMore } }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var map = arguments[0] as IDictionary<object, object?>;
        var rawKeys = arguments[1] as IEnumerable<object?> ?? (arguments[1] != null ? [arguments[1]] : []);

        var result = MapHelper.CopyMap(map ?? MapHelper.NewMap());

        foreach (var rawKey in rawKeys)
        {
            var key = QueryExecutionContext.AtomizeTyped(rawKey);
            if (key != null)
            {
                MapKeyHelper.RemoveByKey(result, key);
            }
        }

        return ValueTask.FromResult<object?>(result);
    }
}

/// <summary>
/// map:entry($key as xs:anyAtomicType, $value as item()*) as map(*)
/// </summary>
public sealed class MapEntryFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Map, "entry");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "key"), Type = new() { ItemType = ItemType.AnyAtomicType, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "value"), Type = XdmSequenceType.ZeroOrMoreItems }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var key = QueryExecutionContext.AtomizeTyped(arguments[0]);
        var value = arguments[1];

        var result = MapHelper.NewMap();
        if (key != null)
        {
            result[key] = value;
        }

        return ValueTask.FromResult<object?>(result);
    }
}

/// <summary>
/// map:for-each($map as map(*), $action as function(xs:anyAtomicType, item()*) as item()*) as item()*
/// </summary>
public sealed class MapForEachFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Map, "for-each");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "map"), Type = new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "action"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var map = arguments[0] as IDictionary<object, object?>;
        var action = arguments[1] as XQueryFunction;

        if (map == null || action == null)
            return Array.Empty<object>();

        var results = new List<object?>();

        foreach (var (key, value) in map)
        {
            var result = await action.InvokeAsync([key, value], context);
            if (result is IEnumerable<object?> seq)
            {
                results.AddRange(seq);
            }
            else if (result != null)
            {
                results.Add(result);
            }
        }

        return results.ToArray();
    }
}

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

/// <summary>
/// map:pair($key as xs:anyAtomicType, $value as item()*) as map(xs:anyAtomicType, item()*)
/// Creates a single-entry map (synonym for map:entry) (XPath 4.0).
/// </summary>
public sealed class MapPairFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Map, "pair");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "key"), Type = new() { ItemType = ItemType.AnyAtomicType, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "value"), Type = XdmSequenceType.ZeroOrMoreItems }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var key = QueryExecutionContext.AtomizeTyped(arguments[0]) ?? arguments[0]!;
        var result = MapHelper.NewMap();
        result[key] = arguments[1];
        return ValueTask.FromResult<object?>(result);
    }
}

/// <summary>
/// map:of-pairs($pairs as map(*)*) as map(*)
/// Merges a sequence of single-entry maps into one map (XPath 4.0).
/// </summary>
public sealed class MapOfPairsFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Map, "of-pairs");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "pairs"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var pairs = arguments[0];
        var result = MapHelper.NewMap();
        if (pairs is object?[] arr)
        {
            foreach (var pair in arr)
            {
                if (pair is IDictionary<object, object?> map)
                    foreach (var kvp in map)
                        result[kvp.Key] = kvp.Value;
            }
        }
        else if (pairs is IDictionary<object, object?> singleMap)
        {
            foreach (var kvp in singleMap)
                result[kvp.Key] = kvp.Value;
        }
        return ValueTask.FromResult<object?>(result);
    }
}

/// <summary>
/// map:replace($map as map(*), $key as xs:anyAtomicType, $value as item()*) as map(*)
/// Replaces a value in a map (like map:put but only if key exists) (XPath 4.0).
/// </summary>
public sealed class MapReplaceFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Map, "replace");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "map"), Type = new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "key"), Type = new() { ItemType = ItemType.AnyAtomicType, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "value"), Type = XdmSequenceType.ZeroOrMoreItems }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var map = arguments[0] as IDictionary<object, object?>;
        var key = QueryExecutionContext.AtomizeTyped(arguments[1]) ?? arguments[1]!;
        var value = arguments[2];

        var result = MapHelper.CopyMap(map ?? MapHelper.NewMap());
        if (result.ContainsKey(key))
            result[key] = value;
        return ValueTask.FromResult<object?>(result);
    }
}

/// <summary>
/// map:group-by($seq as item()*, $key as function(item()) as xs:anyAtomicType) as map(xs:anyAtomicType, item()*)
/// Groups sequence items by a key function, returning a map of key → items (XPath 4.0).
/// </summary>
public sealed class MapGroupByFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Map, "group-by");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "key"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var seq = arguments[0];
        var keyFn = arguments[1] as XQueryFunction;
        if (seq == null || keyFn == null) return MapHelper.NewMap();

        var items = seq is object?[] arr ? arr : new[] { seq };
        var groups = new Dictionary<object, List<object?>>();

        foreach (var item in items)
        {
            var key = await keyFn.InvokeAsync([item], context).ConfigureAwait(false);
            var atomKey = QueryExecutionContext.AtomizeTyped(key) ?? key ?? "";

            if (!groups.TryGetValue(atomKey, out var group))
            {
                group = [];
                groups[atomKey] = group;
            }
            group.Add(item);
        }

        var result = MapHelper.NewMap();
        foreach (var (k, v) in groups)
            result[k] = v.Count == 1 ? v[0] : v.ToArray();
        return result;
    }
}

/// <summary>
/// map:items($map as map(*)) as array(union(xs:anyAtomicType, item()*))*
/// Returns a sequence of [key, value] arrays for each entry (XPath 4.0).
/// </summary>
public sealed class MapItemsFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Map, "items");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "map"), Type = new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne } }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var map = arguments[0] as IDictionary<object, object?>;
        if (map == null) return ValueTask.FromResult<object?>(Array.Empty<object>());

        var items = new List<object?>();
        foreach (var kvp in map)
            items.Add(new List<object?> { kvp.Key, kvp.Value });
        return ValueTask.FromResult<object?>(items.ToArray());
    }
}

/// <summary>
/// map:empty() as map(*) — returns an empty map (XPath 4.0).
/// </summary>
public sealed class MapEmptyFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Map, "empty");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
        => ValueTask.FromResult<object?>(MapHelper.NewMap());
}

/// <summary>
/// map:entries($map as map(*)) as map(xs:string, item()*)* — returns sequence of entry maps (XPath 4.0).
/// Each entry is a map with "key" and "value" entries.
/// </summary>
public sealed class MapEntriesFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Map, "entries");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "map"), Type = new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne } }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var map = arguments[0] as IDictionary<object, object?>;
        if (map == null) return ValueTask.FromResult<object?>(Array.Empty<object>());

        var entries = new List<object?>();
        foreach (var kvp in map)
        {
            var entry = new Dictionary<object, object?>
            {
                ["key"] = kvp.Key,
                ["value"] = kvp.Value
            };
            entries.Add(entry);
        }
        return ValueTask.FromResult<object?>(entries.ToArray());
    }
}

/// <summary>
/// map:keys-where($map as map(*), $pred as function(xs:anyAtomicType) as xs:boolean) as xs:anyAtomicType*
/// Returns keys whose values satisfy a predicate (XPath 4.0).
/// </summary>
public sealed class MapKeysWhereFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Map, "keys-where");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "map"), Type = new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "pred"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var map = arguments[0] as IDictionary<object, object?>;
        var pred = arguments[1] as XQueryFunction;
        if (map == null || pred == null) return Array.Empty<object>();

        var result = new List<object?>();
        foreach (var kvp in map)
        {
            var match = await pred.InvokeAsync([kvp.Value], context).ConfigureAwait(false);
            if (match is true || (match is not false && match != null))
                result.Add(kvp.Key);
        }
        return result.Count == 1 ? result[0] : result.ToArray();
    }
}

/// <summary>
/// map:filter($map as map(*), $pred as function(xs:anyAtomicType, item()*) as xs:boolean) as map(*)
/// Returns a map containing entries that satisfy a predicate (XPath 4.0).
/// </summary>
public sealed class MapFilterFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Map, "filter");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "map"), Type = new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "pred"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var map = arguments[0] as IDictionary<object, object?>;
        var pred = arguments[1] as XQueryFunction;
        if (map == null || pred == null) return MapHelper.NewMap();

        var result = MapHelper.NewMap();
        foreach (var kvp in map)
        {
            var match = await pred.InvokeAsync([kvp.Key, kvp.Value], context).ConfigureAwait(false);
            if (match is true || (match is not false && match != null))
                result[kvp.Key] = kvp.Value;
        }
        return result;
    }
}

/// <summary>
/// map:build($seq as item()*, $key as function(item()) as xs:anyAtomicType,
///           $value as function(item()) as item()*) as map(*)
/// Builds a map from a sequence using key and value functions (XPath 4.0).
/// </summary>
public sealed class MapBuildFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Map, "build");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "key"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "value"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ZeroOrOne } }
    ];
    public override bool IsVariadic => true;
    public override int MaxArity => 3;

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var seq = arguments[0];
        var keyFn = arguments[1] as XQueryFunction;
        var valueFn = arguments.Count > 2 ? arguments[2] as XQueryFunction : null;
        if (seq == null || keyFn == null) return MapHelper.NewMap();

        var items = seq is object?[] arr ? arr : new[] { seq };
        var result = MapHelper.NewMap();
        foreach (var item in items)
        {
            var key = await keyFn.InvokeAsync([item], context).ConfigureAwait(false);
            var value = valueFn != null
                ? await valueFn.InvokeAsync([item], context).ConfigureAwait(false)
                : item;
            if (key != null)
            {
                var atomKey = QueryExecutionContext.AtomizeTyped(key) ?? key;
                result[atomKey] = value;
            }
        }
        return result;
    }
}
