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
/// Function call operator.
/// </summary>
public sealed class FunctionCallOperator : PhysicalOperator
{
    public XQueryFunction? Function { get; init; }
    public required QName FunctionName { get; init; }
    public required IReadOnlyList<PhysicalOperator> ArgumentOperators { get; init; }

    /// <summary>What this call site resolved, the parameter list of that function, and the library state it came from.</summary>
    private sealed record ResolvedCall(FunctionLibrary Library, int LibraryVersion, XQueryFunction? Function, IReadOnlyList<FunctionParameterDef>? Parameters);

    // Replaced as a whole and never mutated, so a plan shared between threads always reads a consistent record.
    private ResolvedCall? _resolved;

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        // Phase B source-location wiring: install this call site's location for the
        // duration of the dispatch so any XQueryException raised by the function
        // implementation inherits accurate (module, line, col) info via context.Error().
        using var _locScope = context.PushLocation(Location);

        // Resolve function: prefer runtime-registered functions (e.g. user-declared)
        // over the statically-resolved reference (which may be a placeholder).
        // The resolution and the function's parameter list are kept until the library changes. Resolving allocated a
        // lookup key on every call, and most built-ins declare Parameters as `=> [...]`, which builds a new list on
        // every read; the XSLT engine evaluates the same call sites hundreds of thousands of times per transformation.
        var resolved = Resolve(context);
        var function = resolved.Function;
        var parameters = resolved.Parameters;

        if (function == null)
        {
            // XSLT 1.0 behaviour (backwards-compatible mode): calling an EXTENSION function that
            // has no implementation is the dynamic error XTDE1425, raised only if the call is
            // evaluated. A function in a standard namespace is still XPST0017 (W3C error-1425a).
            ThrowUnresolved(context);
            yield break;
        }

        // A string held as a slice (xquery#287) is measured, tested and cut without being copied.
        if (StringSliceEvaluation.TryEvaluateCall(this, function, context, out var onSlice))
        {
            yield return onSlice;
            yield break;
        }

        // Streaming optimization for aggregate functions that don't need full materialization.
        // fn:count streams through the argument counting items without storing them.
        // fn:exists/fn:empty only need to check if the first item exists.
        if (function is CountFunction && ArgumentOperators.Count == 1)
        {
            // The count of a range, or of a subsequence of one, is computed from its bounds:
            // count(subsequence(1 to 3000000000, 2147483648)) generated three billion integers.
            if (await RangeSliceAsync(ArgumentOperators[0], context).ConfigureAwait(false) is var (sliceFirst, sliceLast))
            {
                var n = BigInteger.Max(BigInteger.Zero, sliceLast - sliceFirst + 1);
                yield return n <= long.MaxValue ? (object)(long)n : n;
                yield break;
            }
            long itemCount = 0;
            await foreach (var item in ArgumentOperators[0].ExecuteAsync(context))
            {
                itemCount++;
                context.CancellationToken.ThrowIfCancellationRequested();
            }
            yield return itemCount;
            yield break;
        }
        if (function is ExistsFunction && ArgumentOperators.Count == 1)
        {
            var hasAny = false;
            await foreach (var item in ArgumentOperators[0].ExecuteAsync(context))
            {
                hasAny = true;
                break;
            }
            yield return hasAny;
            yield break;
        }
        if (function is EmptyFunction && ArgumentOperators.Count == 1)
        {
            var hasAny = false;
            await foreach (var item in ArgumentOperators[0].ExecuteAsync(context))
            {
                hasAny = true;
                break;
            }
            yield return !hasAny;
            yield break;
        }

        // A subsequence of a range yields its slice without generating the integers before it.
        if (function is SubsequenceFunction or Subsequence3Function
            && await RangeSliceAsync(this, context).ConfigureAwait(false) is var (first, last))
        {
            for (var i = first; i <= last; i++)
            {
                if ((i - first) % 1024 == 0)
                    context.CancellationToken.ThrowIfCancellationRequested();
                yield return i >= long.MinValue && i <= long.MaxValue ? (object)(long)i : i;
            }
            yield break;
        }

        // Evaluate arguments
        var args = new object?[ArgumentOperators.Count];
        for (var ai = 0; ai < ArgumentOperators.Count; ai++)
        {
            object? singleItem = null;
            List<object?>? multiItems = null;
            var count = 0;
            await foreach (var item in ArgumentOperators[ai].ExecuteAsync(context))
            {
                count++;
                if (count == 1)
                {
                    singleItem = item;
                }
                else
                {
                    if (count == 2)
                    {
                        multiItems = [singleItem, item];
                    }
                    else
                    {
                        multiItems!.Add(item);
                    }
                }
                // Guard against unbounded materialization
                if (count % 65536 == 0)
                    context.CheckMaterializationLimit(count);
            }
            args[ai] = count switch
            {
                0 => null,
                1 => singleItem,
                _ => multiItems!.ToArray()
            };
        }

        ConvertArguments(function, parameters, args, context);

        if (TailCall(function, args) is { } tailCall)
        {
            yield return tailCall;
            yield break;
        }

        // Invoke function
        var result = await function.InvokeAsync(args, context);

        // XDM arrays (List<object?>) and maps (Dictionary) are items, not sequences — yield as-is
        if (result is IDictionary<object, object?> || result is List<object?>)
        {
            yield return result;
        }
        else if (result is IEnumerable<object?> seq)
        {
            foreach (var item in seq)
                yield return item;
        }
        else if (result != null)
        {
            yield return result;
        }
    }

    /// <summary>
    /// Set on a call in tail position of a declared function's body (<see cref="TailCalls.Mark"/>):
    /// that function's return type key. A call to a declared function of the same return type is
    /// then returned as a <see cref="TailCallRequest"/> instead of made.
    /// </summary>
    internal string? TailCallerReturnType { get; set; }

    private TailCallRequest? TailCall(XQueryFunction function, object?[] args)
        => TailCallerReturnType is { } key && function is DeclaredFunction declared && declared.ReturnTypeKey == key
            ? new TailCallRequest(declared, args)
            : null;

    /// <summary>The function this call resolves to in <paramref name="context"/>, or null.</summary>
    internal XQueryFunction? ResolvedFunction(QueryExecutionContext context) => Resolve(context).Function;

    private ResolvedCall Resolve(QueryExecutionContext context)
    {
        var library = context.Functions;
        var resolved = _resolved;
        if (resolved is null || !ReferenceEquals(resolved.Library, library) || resolved.LibraryVersion != library.Version)
        {
            // A simple type of an imported schema has a constructor function of its name. The
            // static analysis registers them for a query; an expression that never went through
            // it (an XPath in a stylesheet) finds the constructor here.
            var resolvedFunction = library.Resolve(FunctionName, ArgumentOperators.Count) ?? Function
                ?? Functions.SchemaTypeConstructorFunction.TryCreate(FunctionName, ArgumentOperators.Count, context);
            resolved = new ResolvedCall(library, library.Version, resolvedFunction, resolvedFunction?.Parameters);
            _resolved = resolved;
        }
        return resolved;
    }

    /// <summary>
    /// The function conversion rules applied to collected arguments: XPath 1.0 backwards-compatible
    /// conversions, or the cardinality check. Shared by the async and synchronous paths.
    /// </summary>
    private static void ConvertArguments(XQueryFunction function, IReadOnlyList<FunctionParameterDef>? parameters, object?[] args, QueryExecutionContext context)
    {
        // XPath 1.0 backwards-compatible mode: the function conversion rules of XPath 2.0 §3.1.5.
        // For a single-item parameter only the FIRST item is passed. A string parameter then
        // receives fn:string() of it, and a numeric parameter fn:number() of it, so an empty
        // argument becomes "" or NaN and round('20.7') is 21.
        if (context.BackwardsCompatible && parameters is { Count: > 0 })
        {
            for (var bi = 0; bi < args.Length && bi < parameters.Count; bi++)
            {
                var paramType = parameters[bi].Type;
                if (paramType?.Occurrence is not (Ast.Occurrence.ExactlyOne or Ast.Occurrence.ZeroOrOne))
                    continue;
                if (args[bi] is object?[] arr)
                    args[bi] = arr.Length > 0 ? arr[0] : null;
                if (IsBackwardsCompatibleNumericParameter(function, bi, paramType.ItemType))
                    args[bi] = Completed(s_number.InvokeAsync([args[bi]], context));
                else if (paramType.ItemType is Ast.ItemType.String)
                    args[bi] = Completed(s_string.InvokeAsync([args[bi]], context));
            }
        }
        else
        {
            CheckArgumentCardinality(function, parameters, args, context);
        }
    }

    /// <summary>
    /// The value of a ValueTask on the synchronous path. A function that does complete asynchronously
    /// (fn:doc over HTTP) is waited for: the sync path is off where waiting is impossible
    /// (<see cref="PhysicalOperator.SyncEvaluationEnabled"/>), and XSLT transformations run on
    /// their own thread.
    /// </summary>
    /// <summary>
    /// The first and last value <paramref name="op"/> yields when it is a range (a to b), or
    /// fn:subsequence of a range with numeric positions; null for any other operator, which is
    /// then evaluated the ordinary way. An empty result has First greater than Last.
    /// </summary>
    private static async ValueTask<(BigInteger First, BigInteger Last)?> RangeSliceAsync(
        PhysicalOperator op, QueryExecutionContext context)
    {
        if (op is RangeOperator range)
            return await range.EvaluateBoundsAsync(context).ConfigureAwait(false) ?? (BigInteger.One, BigInteger.Zero);
        if (op is not FunctionCallOperator call
            || call.ArgumentOperators.Count is not (2 or 3)
            || call.ArgumentOperators[0] is not RangeOperator source
            || call.Resolve(context).Function is not (SubsequenceFunction or Subsequence3Function))
            return null;
        if (await PositionAsync(call.ArgumentOperators[1], context).ConfigureAwait(false) is not { } start)
            return null;
        double? length = null;
        if (call.ArgumentOperators.Count == 3)
        {
            if (await PositionAsync(call.ArgumentOperators[2], context).ConfigureAwait(false) is not { } len)
                return null;
            length = len;
        }
        var empty = (BigInteger.One, BigInteger.Zero);
        if (await source.EvaluateBoundsAsync(context).ConfigureAwait(false) is not var (rangeFirst, rangeLast))
            return empty;
        // The items at positions p with round(start) <= p, and p < round(start) + round(length)
        // when there is a length (F&O 3.1 §14.1.10); fn:round rounds half towards positive
        // infinity. NaN, or -INF + INF, selects none; without a length, -INF selects all.
        var lo = Math.Floor(start + 0.5);
        var hi = length is { } l ? lo + Math.Floor(l + 0.5) : double.PositiveInfinity;
        if (double.IsNaN(lo) || double.IsNaN(hi) || double.IsPositiveInfinity(lo) || double.IsNegativeInfinity(hi))
            return empty;
        var count = rangeLast - rangeFirst + 1;
        var firstPos = lo <= 1 ? BigInteger.One : new BigInteger(lo);
        var lastPos = double.IsPositiveInfinity(hi) ? count : BigInteger.Min(count, new BigInteger(hi) - 1);
        return (rangeFirst + firstPos - 1, rangeFirst + lastPos - 1);
    }

    // One numeric (or untyped) item as xs:double; null for anything else, which the function
    // itself then reports.
    private static async ValueTask<double?> PositionAsync(PhysicalOperator op, QueryExecutionContext context)
    {
        object? value = null;
        var n = 0;
        await foreach (var item in op.ExecuteAsync(context))
        {
            if (++n > 1) return null;
            value = item;
        }
        if (n == 0) return null;
        value = context.AtomizeWithNodes(value);
        return value switch
        {
            double d => d,
            float f => f,
            decimal m => (double)m,
            long l => l,
            int i => i,
            BigInteger b => (double)b,
            Xdm.XsUntypedAtomic u when double.TryParse(u.Value.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }

    private static object? Completed(ValueTask<object?> pending)
        => pending.IsCompletedSuccessfully ? pending.Result : pending.AsTask().GetAwaiter().GetResult();

    private void ThrowUnresolved(QueryExecutionContext context)
    {
        if (context.BackwardsCompatible
            && FunctionName.Namespace != Functions.FunctionNamespaces.Fn
            && FunctionName.Namespace != Functions.FunctionNamespaces.Xs
            && FunctionName.Namespace != Functions.FunctionNamespaces.Math
            && FunctionName.Namespace != Functions.FunctionNamespaces.Map
            && FunctionName.Namespace != Functions.FunctionNamespaces.Array)
        {
            throw new XQueryRuntimeException("XTDE1425",
                $"No implementation is available for extension function {FunctionName.LocalName}#{ArgumentOperators.Count}");
        }
        throw new XQueryRuntimeException("XPST0017",
            $"Function {FunctionName.LocalName} not found");
    }

    private bool? _supportsSync;
    internal override bool SupportsSync => _supportsSync ??= ArgumentOperators.All(a => a.SupportsSync);

    internal override object? EvaluateSync(QueryExecutionContext context)
    {
        using var _locScope = context.PushLocation(Location);
        var resolved = Resolve(context);
        var function = resolved.Function;
        if (function == null)
        {
            ThrowUnresolved(context);
            return null;
        }
        if (StringSliceEvaluation.TryEvaluateCall(this, function, context, out var onSlice))
            return onSlice;
        if (ArgumentOperators.Count == 1 && function is CountFunction or ExistsFunction or EmptyFunction)
        {
            var arg = ArgumentOperators[0].EvaluateSync(context);
            var n = arg switch { null => 0L, object?[] seq => seq.LongLength, _ => 1L };
            return function switch { CountFunction => n, ExistsFunction => n > 0, _ => (object)(n == 0) };
        }
        var args = new object?[ArgumentOperators.Count];
        for (var ai = 0; ai < args.Length; ai++)
        {
            args[ai] = ArgumentOperators[ai].EvaluateSync(context);
            if (args[ai] is object?[] { Length: >= 65536 } big)
                context.CheckMaterializationLimit(big.Length);
        }
        ConvertArguments(function, resolved.Parameters, args, context);
        if (TailCall(function, args) is { } tailCall)
            return tailCall;
        return ResultOf(Completed(function.InvokeAsync(args, context)));
    }

    /// <summary>A function's return value as a synchronous result (see ExecuteAsync for the item rules).</summary>
    private static object? ResultOf(object? result)
    {
        if (result is IDictionary<object, object?> || result is List<object?>)
            return result; // XDM arrays and maps are items, not sequences
        if (result is object?[] arr)
            return arr.Length switch { 0 => null, 1 => arr[0], _ => arr };
        if (result is IEnumerable<object?> seq)
        {
            var items = new List<object?>();
            foreach (var item in seq)
                items.Add(item);
            return SyncResultOf(items);
        }
        return result;
    }

    private static readonly Functions.NumberFunction s_number = new();
    private static readonly Functions.StringFunction s_string = new();

    /// <summary>
    /// Whether a backwards-compatible call converts this argument with fn:number(). A declared
    /// numeric type does, and so does the argument of the built-ins whose signature is
    /// xs:numeric? but which declare xs:anyAtomicType? here to accept every numeric type.
    /// </summary>
    private static bool IsBackwardsCompatibleNumericParameter(XQueryFunction function, int index, Ast.ItemType? itemType)
        => itemType is Ast.ItemType.Double or Ast.ItemType.Float or Ast.ItemType.Decimal or Ast.ItemType.Integer
           || (index == 0 && function.Name.Namespace == Functions.FunctionNamespaces.Fn
               && function.Name.LocalName is "round" or "floor" or "ceiling" or "abs" or "round-half-to-even");

    /// <summary>
    /// Applies the cardinality half of the function conversion rules to a built-in's arguments.
    /// </summary>
    /// <remarks>
    /// A user-declared function already raised XPTY0004 for <c>local:f(())</c> against
    /// <c>$x as xs:double</c>; a built-in accepted anything, so <c>substring('abc', ())</c>
    /// returned "" and <c>substring('abc', (1, 2))</c> returned "abc". <see cref="Occurrence.Zero"/>
    /// is the enum's default, so a signature that never set its occurrence reads as
    /// empty-sequence(); that is treated as undeclared rather than enforced.
    /// </remarks>
    private static void CheckArgumentCardinality(XQueryFunction function, IReadOnlyList<FunctionParameterDef>? parameters, object?[] args, QueryExecutionContext context)
    {
        if (parameters is not { Count: > 0 })
            return;
        for (var i = 0; i < args.Length && i < parameters.Count; i++)
        {
            var occurrence = parameters[i].Type?.Occurrence;
            var count = args[i] switch { null => 0, object?[] seq => seq.Length, _ => 1 };
            var allowed = occurrence switch
            {
                Ast.Occurrence.ExactlyOne => count == 1,
                Ast.Occurrence.ZeroOrOne => count <= 1,
                Ast.Occurrence.OneOrMore => count >= 1,
                _ => true,
            };
            if (!allowed)
            {
                // context.Error, not a bare exception: it carries the call site's location, which
                // the PushLocation scope above has already set.
                throw context.Error("XPTY0004",
                    $"{(count == 0 ? "An empty sequence" : $"A sequence of {count} items")} is not allowed as argument {i + 1} "
                    + $"(${parameters[i].Name.LocalName}) of {function.Name.LocalName}(), which expects {parameters[i].Type}");
            }
        }
    }
}
