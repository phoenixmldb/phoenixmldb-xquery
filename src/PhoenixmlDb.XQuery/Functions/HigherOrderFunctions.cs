using PhoenixmlDb.Core;
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
        var func = arguments[1] as XQueryFunction
            ?? throw new XQueryRuntimeException("XPTY0004", "Second argument to fn:for-each must be a function");

        var results = new List<object?>();
        foreach (var item in seq)
        {
            var result = await func.InvokeAsync([item], context);
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
            if (QueryExecutionContext.EffectiveBooleanValue(result))
                results.Add(item);
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
        var func = arguments[2] as XQueryFunction
            ?? throw new XQueryRuntimeException("XPTY0004", "Third argument to fn:for-each-pair must be a function");

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
        items.Sort((a, b) =>
        {
            if (a is IComparable ca)
            {
                try { return ca.CompareTo(b); }
                catch (ArgumentException) { /* incompatible types — fall through to string comparison */ }
            }
            return string.Compare(a?.ToString(), b?.ToString(), StringComparison.Ordinal);
        });
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
        // Collation parameter is accepted but we use default comparison (codepoint)
        var items = SequenceHelper.Flatten(arguments[0]);
        items.Sort((a, b) =>
        {
            if (a is IComparable ca)
            {
                try { return ca.CompareTo(b); }
                catch (ArgumentException) { }
            }
            return string.Compare(a?.ToString(), b?.ToString(), StringComparison.Ordinal);
        });
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
        new() { Name = new QName(NamespaceId.None, "key"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var items = SequenceHelper.Flatten(arguments[0]);
        var keyFn = arguments[2] as XQueryFunction
            ?? throw new XQueryRuntimeException("XPTY0004", "Third argument to sort must be a function");

        // Compute sort keys for each item
        var keyed = new List<(object? item, object? key)>();
        foreach (var item in items)
        {
            var key = await keyFn.InvokeAsync([item], context).ConfigureAwait(false);
            keyed.Add((item, key));
        }

        keyed.Sort((a, b) =>
        {
            var ak = a.key;
            var bk = b.key;
            if (ak is IComparable ca)
            {
                try { return ca.CompareTo(bk); }
                catch (ArgumentException) { }
            }
            return string.Compare(ak?.ToString(), bk?.ToString(), StringComparison.Ordinal);
        });

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
        if (arg is IEnumerable<object?> seq)
            return seq.ToList();
        if (arg == null) return [];
        return [arg];
    }
}
