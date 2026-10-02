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
        var library = context.Functions;
        var resolved = _resolved;
        if (resolved is null || !ReferenceEquals(resolved.Library, library) || resolved.LibraryVersion != library.Version)
        {
            var resolvedFunction = library.Resolve(FunctionName, ArgumentOperators.Count) ?? Function;
            resolved = new ResolvedCall(library, library.Version, resolvedFunction, resolvedFunction?.Parameters);
            _resolved = resolved;
        }
        var function = resolved.Function;
        var parameters = resolved.Parameters;

        if (function == null)
        {
            // XSLT 1.0 behaviour (backwards-compatible mode): calling an EXTENSION function that
            // has no implementation is the dynamic error XTDE1425, raised only if the call is
            // evaluated. A function in a standard namespace is still XPST0017 (W3C error-1425a).
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

        // Streaming optimization for aggregate functions that don't need full materialization.
        // fn:count streams through the argument counting items without storing them.
        // fn:exists/fn:empty only need to check if the first item exists.
        if (function is CountFunction && ArgumentOperators.Count == 1)
        {
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
                    args[bi] = await s_number.InvokeAsync([args[bi]], context).ConfigureAwait(false);
                else if (paramType.ItemType is Ast.ItemType.String)
                    args[bi] = await s_string.InvokeAsync([args[bi]], context).ConfigureAwait(false);
            }
        }

        else
        {
            CheckArgumentCardinality(function, parameters, args, context);
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
