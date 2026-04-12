using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// Helpers to coerce maps and arrays (which are callable as functions per XPath 3.1)
/// into a plain delegate for use by higher-order functions like fn:filter, fn:for-each, etc.
/// </summary>
internal static class CallableCoercion
{
    public static async ValueTask<object?> InvokeUnaryAsync(object? callable, object? arg, Ast.ExecutionContext context)
    {
        switch (callable)
        {
            case XQueryFunction fn:
                return await fn.InvokeAsync([arg], context);
            case IDictionary<object, object?> map:
                if (arg != null && MapKeyHelper.TryGetValue(map, arg, out var v))
                    return v;
                return null;
            case List<object?> array:
                if (arg == null) return null;
                var pos = Convert.ToInt32(arg);
                if (pos >= 1 && pos <= array.Count)
                    return array[pos - 1];
                throw new XQueryRuntimeException("FOAY0001", $"Array index {pos} out of range");
            default:
                throw new XQueryRuntimeException("XPTY0004", "Value is not callable as a function");
        }
    }

    public static bool IsCallable(object? value) =>
        value is XQueryFunction or IDictionary<object, object?> or List<object?>;
}

/// <summary>
/// fn:for-each($seq, $action) as item()*
/// </summary>
public sealed class ForEachFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "for-each");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "action"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var seq = SequenceHelper.Flatten(arguments[0]);
        var callable = arguments[1]
            ?? throw new XQueryRuntimeException("XPTY0004", "Second argument to fn:for-each must be callable");

        var results = new List<object?>();
        foreach (var item in seq)
        {
            var result = await CallableCoercion.InvokeUnaryAsync(callable, item, context);
            if (result is IEnumerable<object?> resultSeq)
            {
                foreach (var r in resultSeq) results.Add(r);
            }
            else if (result != null)
                results.Add(result);
        }
        return results.ToArray();
    }
}

/// <summary>
/// fn:filter($seq, $predicate) as item()*
/// </summary>
public sealed class FilterFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "filter");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "f"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var seq = SequenceHelper.Flatten(arguments[0]);
        var callable = arguments[1];
        if (!CallableCoercion.IsCallable(callable))
            throw new XQueryRuntimeException("XPTY0004", "Second argument to fn:filter must be a function");

        var results = new List<object?>();
        foreach (var item in seq)
        {
            var result = await CallableCoercion.InvokeUnaryAsync(callable, item, context);
            // Per spec, fn:filter's $f has type function(item()) as xs:boolean.
            // Non-boolean results raise XPTY0004, but nodes/untypedAtomic are atomized
            // and cast to xs:boolean per function coercion rules.
            var unwrapped = result;
            if (unwrapped is object?[] arr)
            {
                if (arr.Length == 0) unwrapped = null;
                else if (arr.Length == 1) unwrapped = arr[0];
                else throw new XQueryRuntimeException("XPTY0004",
                    "fn:filter predicate must return a single xs:boolean");
            }
            if (unwrapped is null)
                throw new XQueryRuntimeException("XPTY0004",
                    "fn:filter predicate must return xs:boolean, got empty sequence");
            if (unwrapped is bool b)
            {
                if (b) results.Add(item);
            }
            else
            {
                // Atomize nodes, then cast to xs:boolean
                var atomized = DataFunction.Atomize(unwrapped);
                if (atomized is XsUntypedAtomic ua)
                {
                    // xs:untypedAtomic → xs:boolean: "0"/"false" → false, "1"/"true" → true
                    if (bool.TryParse(ua.Value, out var boolVal))
                    {
                        if (boolVal) results.Add(item);
                    }
                    else if (ua.Value == "0") { /* false, skip */ }
                    else if (ua.Value == "1") { results.Add(item); }
                    else throw new XQueryRuntimeException("FORG0001",
                        $"Cannot cast '{ua.Value}' to xs:boolean");
                }
                else
                {
                    throw new XQueryRuntimeException("XPTY0004",
                        $"fn:filter predicate must return xs:boolean, got {unwrapped.GetType().Name}");
                }
            }
        }
        return results.ToArray();
    }
}

/// <summary>
/// fn:fold-left($seq, $zero, $f) as item()*
/// </summary>
public sealed class FoldLeftFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "fold-left");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "zero"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "f"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var seq = SequenceHelper.Flatten(arguments[0]);
        object? accumulator = arguments[1];
        var func = arguments[2] as XQueryFunction
            ?? throw new XQueryRuntimeException("XPTY0004", "Third argument to fn:fold-left must be a function");

        foreach (var item in seq)
            accumulator = await func.InvokeAsync([accumulator, item], context);

        return accumulator;
    }
}

/// <summary>
/// fn:fold-right($seq, $zero, $f) as item()*
/// </summary>
public sealed class FoldRightFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "fold-right");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "zero"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "f"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var seq = SequenceHelper.Flatten(arguments[0]);
        object? accumulator = arguments[1];
        var func = arguments[2] as XQueryFunction
            ?? throw new XQueryRuntimeException("XPTY0004", "Third argument to fn:fold-right must be a function");

        for (int i = seq.Count - 1; i >= 0; i--)
            accumulator = await func.InvokeAsync([seq[i], accumulator], context);

        return accumulator;
    }
}

/// <summary>
/// fn:for-each-pair($seq1, $seq2, $action) as item()*
/// </summary>
public sealed class ForEachPairFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "for-each-pair");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "seq1"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "seq2"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "f"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var seq1 = SequenceHelper.Flatten(arguments[0]);
        var seq2 = SequenceHelper.Flatten(arguments[1]);
        var callable = arguments[2];
        // Maps and arrays have arity 1, not 2 — XPTY0004
        if (callable is IDictionary<object, object?> || callable is List<object?>)
            throw new XQueryRuntimeException("XPTY0004",
                "fn:for-each-pair requires a function of arity 2, maps and arrays have arity 1");
        var func = callable as XQueryFunction
            ?? throw new XQueryRuntimeException("XPTY0004",
                "Third argument to fn:for-each-pair must be a function");
        if (func.Parameters.Count != 2)
            throw new XQueryRuntimeException("XPTY0004",
                $"fn:for-each-pair requires a function of arity 2, got arity {func.Parameters.Count}");

        var results = new List<object?>();
        var len = Math.Min(seq1.Count, seq2.Count);
        for (int i = 0; i < len; i++)
        {
            var result = await func.InvokeAsync([seq1[i], seq2[i]], context);
            if (result is IEnumerable<object?> resultSeq)
            {
                foreach (var r in resultSeq) results.Add(r);
            }
            else if (result != null)
                results.Add(result);
        }
        return results.ToArray();
    }
}

/// <summary>
/// fn:sort($input) as item()*
/// </summary>
public sealed class SortFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "sort");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "input"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var items = SequenceHelper.Flatten(arguments[0]);
        SortHelper.SortByAtomicKey(items);
        return ValueTask.FromResult<object?>(items.ToArray());
    }
}

/// <summary>
/// fn:sort($input, $collation) as item()*
/// </summary>
public sealed class Sort2Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "sort");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "input"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "collation"), Type = XdmSequenceType.OptionalString }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var items = SequenceHelper.Flatten(arguments[0]);
        // Collation parameter: null/empty means default (codepoint)
        SortHelper.SortByAtomicKey(items);
        return ValueTask.FromResult<object?>(items.ToArray());
    }
}

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

        keyed.Sort((a, b) => SortHelper.CompareKeySequences(a.keys, b.keys));

        return keyed.Select(k => k.item).ToArray();
    }
}

/// <summary>
/// fn:apply($function, $array) as item()*
/// </summary>
public sealed class ApplyFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "apply");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "function"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var func = arguments[0] as XQueryFunction
            ?? throw new XQueryRuntimeException("XPTY0004", "First argument to fn:apply must be a function");
        // XDM arrays are List<object?>, but can also be object?[] in some contexts
        IReadOnlyList<object?> arr;
        if (arguments[1] is List<object?> list)
            arr = list;
        else if (arguments[1] is object?[] objArr)
            arr = objArr;
        else
            throw new XQueryRuntimeException("XPTY0004", "Second argument to fn:apply must be an array");

        return await func.InvokeAsync(arr, context);
    }
}

/// <summary>
/// Helper for converting arguments to sequences.
/// </summary>
internal static class SequenceHelper
{
    public static List<object?> Flatten(object? arg)
    {
        // XDM arrays (List<object?>) are single items — do not flatten
        if (arg is List<object?>) return [arg];
        if (arg is IEnumerable<object?> seq)
            return seq.ToList();
        if (arg == null) return [];
        return [arg];
    }
}

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
    public static void SortByAtomicKey(List<object?> items)
    {
        if (items.Count <= 1) return;

        // Atomize each item to get its sort key
        var keyed = new List<(object? item, List<object?> keys)>(items.Count);
        foreach (var item in items)
        {
            var atomized = Atomize(item);
            keyed.Add((item, atomized));
        }

        keyed.Sort((a, b) => CompareKeySequences(a.keys, b.keys));

        for (int i = 0; i < items.Count; i++)
            items[i] = keyed[i].item;
    }

    /// <summary>
    /// Compares two key sequences lexicographically per fn:sort spec.
    /// Empty key sorts before any value. Sequences are compared element-by-element.
    /// </summary>
    public static int CompareKeySequences(List<object?> a, List<object?> b)
    {
        int len = Math.Max(a.Count, b.Count);
        for (int i = 0; i < len; i++)
        {
            if (i >= a.Count) return -1; // shorter sorts first
            if (i >= b.Count) return 1;
            var cmp = CompareAtomicSortKeys(a[i], b[i]);
            if (cmp != 0) return cmp;
        }
        return 0;
    }

    private static int CompareAtomicSortKeys(object? a, object? b)
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
            return string.Compare(a.ToString(), b.ToString(), StringComparison.Ordinal);
        }

        try
        {
            return MinFunction.CompareValues(a, b);
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
