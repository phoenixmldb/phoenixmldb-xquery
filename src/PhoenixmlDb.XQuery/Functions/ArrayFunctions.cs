using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// XQuery 3.1 array functions.
/// </summary>
internal static class ArrayHelper
{
    /// <summary>
    /// Validates that a position argument is an integer (not decimal/double/float) and returns it.
    /// Per spec, array position arguments are xs:integer — non-integer numerics are XPTY0004.
    /// </summary>
    internal static int RequireIntegerPosition(object? arg, string functionName)
    {
        if (arg is decimal || arg is double || arg is float)
            throw new XQueryRuntimeException("XPTY0004",
                $"{functionName} requires an xs:integer position, got {arg.GetType().Name} value {arg}");
        return Convert.ToInt32(arg);
    }
}

/// <summary>
/// array:size($array as array(*)) as xs:integer
/// </summary>
public sealed class ArraySizeFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "size");
    public override XdmSequenceType ReturnType => XdmSequenceType.Integer;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var array = arguments[0] as IList<object?>;
        return ValueTask.FromResult<object?>(array?.Count ?? 0);
    }
}

/// <summary>
/// array:get($array as array(*), $position as xs:integer) as item()*
/// </summary>
public sealed class ArrayGetFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "get");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "position"), Type = XdmSequenceType.Integer }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var array = arguments[0] as IList<object?>;
        var position = ArrayHelper.RequireIntegerPosition(arguments[1], "array:get");

        if (array == null || position < 1 || position > array.Count)
        {
            throw new Execution.XQueryRuntimeException("FOAY0001",
                $"Array index {position} out of bounds (array size: {array?.Count ?? 0})");
        }

        return ValueTask.FromResult(array[position - 1]); // 1-based indexing
    }
}

/// <summary>
/// array:put($array as array(*), $position as xs:integer, $member as item()*) as array(*)
/// </summary>
public sealed class ArrayPutFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "put");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "position"), Type = XdmSequenceType.Integer },
        new() { Name = new QName(NamespaceId.None, "member"), Type = XdmSequenceType.ZeroOrMoreItems }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var array = arguments[0] as IList<object?>;
        var position = ArrayHelper.RequireIntegerPosition(arguments[1], "array:put");
        var member = arguments[2];

        if (array == null || position < 1 || position > array.Count)
        {
            throw new Execution.XQueryRuntimeException("FOAY0001", $"Array position out of bounds");
        }

        var result = new List<object?>(array);
        result[position - 1] = member;

        return ValueTask.FromResult<object?>(result);
    }
}

/// <summary>
/// array:append($array as array(*), $appendage as item()*) as array(*)
/// </summary>
public sealed class ArrayAppendFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "append");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "appendage"), Type = XdmSequenceType.ZeroOrMoreItems }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var array = arguments[0] as IList<object?> ?? [];
        var appendage = arguments[1];

        var result = new List<object?>(array) { appendage };
        return ValueTask.FromResult<object?>(result);
    }
}

/// <summary>
/// array:subarray($array as array(*), $start as xs:integer) as array(*)
/// </summary>
public sealed class ArraySubarrayFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "subarray");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "start"), Type = XdmSequenceType.Integer }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var array = arguments[0] as IList<object?> ?? [];
        var start = ArrayHelper.RequireIntegerPosition(arguments[1], "array:subarray");

        if (start < 1 || start > array.Count + 1)
        {
            throw new Execution.XQueryRuntimeException("FOAY0001", $"Array position out of bounds");
        }

        var result = array.Skip(start - 1).ToList();
        return ValueTask.FromResult<object?>(result);
    }
}

/// <summary>
/// array:subarray($array as array(*), $start as xs:integer, $length as xs:integer) as array(*)
/// </summary>
public sealed class ArraySubarray3Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "subarray");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "start"), Type = XdmSequenceType.Integer },
        new() { Name = new QName(NamespaceId.None, "length"), Type = XdmSequenceType.Integer }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var array = arguments[0] as IList<object?> ?? [];
        var start = ArrayHelper.RequireIntegerPosition(arguments[1], "array:subarray");
        var length = ArrayHelper.RequireIntegerPosition(arguments[2], "array:subarray");

        if (start < 1 || length < 0 || start + length - 1 > array.Count)
        {
            throw new Execution.XQueryRuntimeException("FOAY0001", $"Array position out of bounds");
        }

        var result = array.Skip(start - 1).Take(length).ToList();
        return ValueTask.FromResult<object?>(result);
    }
}

/// <summary>
/// array:remove($array as array(*), $positions as xs:integer*) as array(*)
/// </summary>
public sealed class ArrayRemoveFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "remove");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "positions"), Type = new() { ItemType = ItemType.Integer, Occurrence = Occurrence.ZeroOrMore } }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var array = arguments[0] as IList<object?> ?? [];
        var positions = arguments[1] is IEnumerable<object?> seq
            ? seq.Select(p => Convert.ToInt32(p)).ToHashSet()
            : arguments[1] != null ? [Convert.ToInt32(arguments[1])] : new HashSet<int>();

        // Validate positions are in range (1-based)
        foreach (var pos in positions)
        {
            if (pos < 1 || pos > array.Count)
                throw new XQueryRuntimeException("FOAY0001", $"Array position {pos} out of range (array size: {array.Count})");
        }

        var result = new List<object?>();
        for (var i = 0; i < array.Count; i++)
        {
            if (!positions.Contains(i + 1)) // 1-based
            {
                result.Add(array[i]);
            }
        }

        return ValueTask.FromResult<object?>(result);
    }
}

/// <summary>
/// array:insert-before($array as array(*), $position as xs:integer, $member as item()*) as array(*)
/// </summary>
public sealed class ArrayInsertBeforeFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "insert-before");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "position"), Type = XdmSequenceType.Integer },
        new() { Name = new QName(NamespaceId.None, "member"), Type = XdmSequenceType.ZeroOrMoreItems }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var array = arguments[0] as IList<object?> ?? [];
        var position = ArrayHelper.RequireIntegerPosition(arguments[1], "array:insert-before");
        var member = arguments[2];

        if (position < 1 || position > array.Count + 1)
        {
            throw new Execution.XQueryRuntimeException("FOAY0001", $"Array position out of bounds");
        }

        var result = new List<object?>(array);
        result.Insert(position - 1, member);

        return ValueTask.FromResult<object?>(result);
    }
}

/// <summary>
/// array:head($array as array(*)) as item()*
/// </summary>
public sealed class ArrayHeadFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "head");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var array = arguments[0] as IList<object?>;

        if (array == null || array.Count == 0)
        {
            throw new InvalidOperationException("array:head called on empty array");
        }

        return ValueTask.FromResult(array[0]);
    }
}

/// <summary>
/// array:tail($array as array(*)) as array(*)
/// </summary>
public sealed class ArrayTailFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "tail");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var array = arguments[0] as IList<object?>;

        if (array == null || array.Count == 0)
        {
            throw new InvalidOperationException("array:tail called on empty array");
        }

        var result = array.Skip(1).ToList();
        return ValueTask.FromResult<object?>(result);
    }
}

/// <summary>
/// array:reverse($array as array(*)) as array(*)
/// </summary>
public sealed class ArrayReverseFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "reverse");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var array = arguments[0] as IList<object?> ?? [];
        var result = array.Reverse().ToList();
        return ValueTask.FromResult<object?>(result);
    }
}

/// <summary>
/// array:join($arrays as array(*)*) as array(*)
/// </summary>
public sealed class ArrayJoinFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "join");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arrays"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ZeroOrMore } }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var result = new List<object?>();
        var arg = arguments[0];

        // Single array argument: return it as-is (join of one array)
        if (arg is List<object?> singleArray)
        {
            return ValueTask.FromResult<object?>(new List<object?>(singleArray));
        }

        // Sequence of arrays: join all members
        if (arg is IEnumerable<object?> arrays)
        {
            foreach (var array in arrays)
            {
                if (array is IList<object?> list)
                {
                    result.AddRange(list);
                }
            }
        }

        return ValueTask.FromResult<object?>(result);
    }
}

/// <summary>
/// array:for-each($array as array(*), $action as function(item()*) as item()*) as array(*)
/// </summary>
public sealed class ArrayForEachFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "for-each");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "action"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var array = arguments[0] as IList<object?> ?? [];
        var callable = arguments[1]
            ?? throw new XQueryRuntimeException("XPTY0004", "Second argument to array:for-each must be callable");

        var result = new List<object?>();

        foreach (var member in array)
        {
            var transformed = await CallableCoercion.InvokeUnaryAsync(callable, member, context);
            result.Add(transformed);
        }

        return result;
    }
}

/// <summary>
/// array:filter($array as array(*), $function as function(item()*) as xs:boolean) as array(*)
/// </summary>
public sealed class ArrayFilterFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "filter");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "function"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var array = arguments[0] as IList<object?> ?? [];
        var callable = arguments[1]
            ?? throw new XQueryRuntimeException("XPTY0004", "Second argument to array:filter must be callable");

        var result = new List<object?>();

        foreach (var member in array)
        {
            var keep = await CallableCoercion.InvokeUnaryAsync(callable, member, context);
            if (keep is not bool b)
                throw new XQueryRuntimeException("XPTY0004",
                    $"array:filter callback must return xs:boolean, got {(keep?.GetType().Name ?? "empty sequence")}");
            if (b)
                result.Add(member);
        }

        return result;
    }
}

/// <summary>
/// array:fold-left($array as array(*), $zero as item()*, $f as function(item()*, item()*) as item()*) as item()*
/// </summary>
public sealed class ArrayFoldLeftFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "fold-left");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "zero"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "f"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var array = arguments[0] as IList<object?> ?? [];
        var accumulator = arguments[1];
        var f = arguments[2] as XQueryFunction;

        if (f == null)
            return accumulator;

        foreach (var member in array)
        {
            accumulator = await f.InvokeAsync([accumulator, member], context);
        }

        return accumulator;
    }
}

/// <summary>
/// array:fold-right($array as array(*), $zero as item()*, $f as function(item()*, item()*) as item()*) as item()*
/// </summary>
public sealed class ArrayFoldRightFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "fold-right");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "zero"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "f"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var array = arguments[0] as IList<object?> ?? [];
        var accumulator = arguments[1];
        var f = arguments[2] as XQueryFunction;

        if (f == null)
            return accumulator;

        for (var i = array.Count - 1; i >= 0; i--)
        {
            accumulator = await f.InvokeAsync([array[i], accumulator], context);
        }

        return accumulator;
    }
}

/// <summary>
/// array:for-each-pair($array1 as array(*), $array2 as array(*), $f as function(item()*, item()*) as item()*) as array(*)
/// </summary>
public sealed class ArrayForEachPairFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "for-each-pair");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "array1"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "array2"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "f"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var array1 = arguments[0] as IList<object?> ?? [];
        var array2 = arguments[1] as IList<object?> ?? [];
        var callable = arguments[2];

        // Validate callable is a function with arity 2
        if (callable is not XQueryFunction f)
            throw new XQueryRuntimeException("XPTY0004",
                "Third argument to array:for-each-pair must be a function");
        if (f.Parameters.Count != 2)
            throw new XQueryRuntimeException("XPTY0004",
                $"Function passed to array:for-each-pair must have arity 2, got {f.Parameters.Count}");

        var result = new List<object?>();
        var minLen = Math.Min(array1.Count, array2.Count);

        for (var i = 0; i < minLen; i++)
        {
            var transformed = await f.InvokeAsync([array1[i], array2[i]], context);
            result.Add(transformed);
        }

        return result;
    }
}

/// <summary>
/// array:sort($array as array(*)) as array(*)
/// </summary>
public sealed class ArraySortFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "sort");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var array = arguments[0] as IList<object?> ?? [];
        // Sort members by their atomized value using XDM comparison
        var keyed = array.Select(item => (item, keys: AtomizeForSortKeys(item))).ToList();
        keyed.Sort((a, b) => SortHelper.CompareKeySequences(a.keys, b.keys));
        return ValueTask.FromResult<object?>(keyed.Select(k => k.item).ToList());
    }

    internal static List<object?> AtomizeForSortKeys(object? item)
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

/// <summary>
/// array:sort($array, $collation) as array(*)
/// </summary>
public sealed class ArraySort2Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "sort");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "collation"), Type = XdmSequenceType.OptionalString }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        // Collation accepted but default codepoint used
        return new ArraySortFunction().InvokeAsync([arguments[0]], context);
    }
}

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

        keyed.Sort((a, b) => SortHelper.CompareKeySequences(a.keys, b.keys));
        return keyed.Select(k => k.item).ToList();
    }
}

/// <summary>
/// array:flatten($input as item()*) as item()*
/// </summary>
public sealed class ArrayFlattenFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "flatten");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "input"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var result = new List<object?>();
        Flatten(arguments[0], result);
        return ValueTask.FromResult<object?>(result.ToArray());
    }

    private static void Flatten(object? item, List<object?> result)
    {
        switch (item)
        {
            case IList<object?> array:
                foreach (var member in array)
                {
                    Flatten(member, result);
                }
                break;

            case IEnumerable<object?> seq when item is not string:
                foreach (var member in seq)
                {
                    Flatten(member, result);
                }
                break;

            default:
                if (item != null)
                {
                    result.Add(item);
                }
                break;
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// XPath/XQuery 4.0 new array functions
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// array:items($array as array(*)) as item()* — returns all members as a flat sequence (XPath 4.0).
/// </summary>
public sealed class ArrayItemsFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "items");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        if (arguments[0] is List<object?> list)
            return ValueTask.FromResult<object?>(list.Count == 1 ? list[0] : list.ToArray());
        return ValueTask.FromResult<object?>(Array.Empty<object>());
    }
}

/// <summary>
/// array:sort-by($array as array(*), $key as function(item()*) as xs:anyAtomicType) as array(*)
/// Sorts an array by a key function (XPath 4.0).
/// </summary>
public sealed class ArraySortByFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "sort-by");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "key"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        if (arguments[0] is not List<object?> list || arguments[1] is not XQueryFunction keyFn)
            return new List<object?>();

        // Compute keys for all members
        var keyed = new List<(object? Member, string Key)>();
        foreach (var member in list)
        {
            var key = await keyFn.InvokeAsync([member], context).ConfigureAwait(false);
            keyed.Add((member, Execution.QueryExecutionContext.Atomize(key)?.ToString() ?? ""));
        }

        keyed.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.Ordinal));
        return new List<object?>(keyed.Select(k => k.Member));
    }
}

/// <summary>
/// array:members($array as array(*)) as array(item()*)* — each member wrapped in its own array (XPath 4.0).
/// </summary>
public sealed class ArrayMembersFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "members");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        if (arguments[0] is not List<object?> list)
            return ValueTask.FromResult<object?>(Array.Empty<object>());

        // Each member becomes a single-element array
        var result = new List<object?>();
        foreach (var member in list)
            result.Add(new List<object?> { member });
        return ValueTask.FromResult<object?>(result.ToArray());
    }
}

/// <summary>
/// array:of-members($seq as array(*)*) as array(*) — combines arrays into one (XPath 4.0).
/// </summary>
public sealed class ArrayOfMembersFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "of-members");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var seq = arguments[0];
        if (seq == null) return ValueTask.FromResult<object?>(new List<object?>());

        var items = seq is object?[] arr ? arr : new[] { seq };
        var result = new List<object?>();
        foreach (var item in items)
        {
            if (item is List<object?> memberArray)
                result.AddRange(memberArray);
            else if (item != null)
                result.Add(item);
        }
        return ValueTask.FromResult<object?>(result);
    }
}

/// <summary>
/// array:empty() as array(*) — returns an empty array (XPath 4.0).
/// </summary>
public sealed class ArrayEmptyFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "empty");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
        => ValueTask.FromResult<object?>(new List<object?>());
}

/// <summary>
/// array:build($seq as item()*, $fn as function(item()) as item()*) as array(*)
/// Builds an array from a sequence, optionally applying a function (XPath 4.0).
/// </summary>
public sealed class ArrayBuildFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "build");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "fn"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ZeroOrOne } }
    ];
    public override bool IsVariadic => true;
    public override int MaxArity => 2;

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var seq = arguments[0];
        var fn = arguments.Count > 1 ? arguments[1] as XQueryFunction : null;
        if (seq == null) return new List<object?>();

        var items = seq is object?[] arr ? arr : new[] { seq };
        var result = new List<object?>();

        if (fn != null)
        {
            foreach (var item in items)
            {
                var mapped = await fn.InvokeAsync([item], context).ConfigureAwait(false);
                result.Add(mapped);
            }
        }
        else
        {
            result.AddRange(items);
        }
        return result;
    }
}

/// <summary>
/// array:foot($array as array(*)) as item()* — returns last member (XPath 4.0).
/// </summary>
public sealed class ArrayFootFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "foot");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        if (arguments[0] is List<object?> list && list.Count > 0)
            return ValueTask.FromResult(list[^1]);
        return ValueTask.FromResult<object?>(null);
    }
}

/// <summary>
/// array:trunk($array as array(*)) as array(*) — all members except last (XPath 4.0).
/// </summary>
public sealed class ArrayTrunkFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "trunk");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        if (arguments[0] is List<object?> list && list.Count > 1)
            return ValueTask.FromResult<object?>(new List<object?>(list.GetRange(0, list.Count - 1)));
        return ValueTask.FromResult<object?>(new List<object?>());
    }
}

/// <summary>
/// array:index-of($array as array(*), $target as item()*) as xs:integer*
/// Returns positions where the target appears (XPath 4.0).
/// </summary>
public sealed class ArrayIndexOfFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "index-of");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Integer, Occurrence = Occurrence.ZeroOrMore };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "target"), Type = XdmSequenceType.ZeroOrMoreItems }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        if (arguments[0] is not List<object?> list) return ValueTask.FromResult<object?>(Array.Empty<object>());
        var target = Execution.QueryExecutionContext.Atomize(arguments[1])?.ToString();

        var result = new List<object?>();
        for (var i = 0; i < list.Count; i++)
        {
            var val = Execution.QueryExecutionContext.Atomize(list[i])?.ToString();
            if (Equals(val, target))
                result.Add((long)(i + 1));
        }
        return ValueTask.FromResult<object?>(result.Count == 1 ? result[0] : result.ToArray());
    }
}

/// <summary>
/// array:index-where($array as array(*), $pred as function(item()*) as xs:boolean) as xs:integer*
/// Returns positions where the predicate is true (XPath 4.0).
/// </summary>
public sealed class ArrayIndexWhereFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "index-where");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Integer, Occurrence = Occurrence.ZeroOrMore };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "pred"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        if (arguments[0] is not List<object?> list || arguments[1] is not XQueryFunction pred)
            return Array.Empty<object>();

        var result = new List<object?>();
        for (var i = 0; i < list.Count; i++)
        {
            var match = await pred.InvokeAsync([list[i]], context).ConfigureAwait(false);
            if (match is true || (match is not false && match != null))
                result.Add((long)(i + 1));
        }
        return result.Count == 1 ? result[0] : result.ToArray();
    }
}

/// <summary>
/// array:slice($array as array(*), $start as xs:integer?, $end as xs:integer?,
///             $step as xs:integer?) as array(*)
/// Flexible sub-array with start/end/step (XPath 4.0).
/// </summary>
public sealed class ArraySliceFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "slice");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "start"), Type = new() { ItemType = ItemType.Integer, Occurrence = Occurrence.ZeroOrOne } },
        new() { Name = new QName(NamespaceId.None, "end"), Type = new() { ItemType = ItemType.Integer, Occurrence = Occurrence.ZeroOrOne } },
        new() { Name = new QName(NamespaceId.None, "step"), Type = new() { ItemType = ItemType.Integer, Occurrence = Occurrence.ZeroOrOne } }
    ];
    public override bool IsVariadic => true;
    public override int MaxArity => 4;

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        if (arguments[0] is not List<object?> list) return ValueTask.FromResult<object?>(new List<object?>());
        var len = list.Count;

        var start = arguments.Count > 1 && arguments[1] != null ? Convert.ToInt32(arguments[1]) : 1;
        var end = arguments.Count > 2 && arguments[2] != null ? Convert.ToInt32(arguments[2]) : len;
        var step = arguments.Count > 3 && arguments[3] != null ? Convert.ToInt32(arguments[3]) : 1;

        if (start < 0) start = len + start + 1;
        if (end < 0) end = len + end + 1;
        if (step == 0) return ValueTask.FromResult<object?>(new List<object?>());

        var result = new List<object?>();
        if (step > 0)
        {
            for (var i = Math.Max(1, start); i <= Math.Min(len, end); i += step)
                result.Add(list[i - 1]);
        }
        else
        {
            for (var i = Math.Min(len, start); i >= Math.Max(1, end); i += step)
                result.Add(list[i - 1]);
        }
        return ValueTask.FromResult<object?>(result);
    }
}

/// <summary>
/// array:sort-with($array as array(*), $comparator as function(item()*, item()*) as xs:integer) as array(*)
/// Sorts an array using a custom comparator function (XPath 4.0).
/// </summary>
public sealed class ArraySortWithFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "sort-with");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "comparator"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        if (arguments[0] is not List<object?> list || arguments[1] is not XQueryFunction cmp)
            return new List<object?>();

        var sorted = new List<object?>(list);
        // Use insertion sort with async comparator
        for (var i = 1; i < sorted.Count; i++)
        {
            var key = sorted[i];
            var j = i - 1;
            while (j >= 0)
            {
                var cmpResult = await cmp.InvokeAsync([sorted[j], key], context).ConfigureAwait(false);
                if (Convert.ToInt32(cmpResult) <= 0) break;
                sorted[j + 1] = sorted[j];
                j--;
            }
            sorted[j + 1] = key;
        }
        return sorted;
    }
}

/// <summary>
/// array:split($array as array(*), $sizes as xs:integer*) as array(array(*))
/// Splits an array into chunks (XPath 4.0).
/// </summary>
public sealed class ArraySplitFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "split");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "size"), Type = XdmSequenceType.Integer }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        if (arguments[0] is not List<object?> list) return ValueTask.FromResult<object?>(new List<object?>());
        var chunkSize = Convert.ToInt32(arguments[1]);
        if (chunkSize <= 0) return ValueTask.FromResult<object?>(new List<object?>());

        var result = new List<object?>();
        for (var i = 0; i < list.Count; i += chunkSize)
        {
            var chunk = new List<object?>(list.GetRange(i, Math.Min(chunkSize, list.Count - i)));
            result.Add(chunk);
        }
        return ValueTask.FromResult<object?>(result);
    }
}
