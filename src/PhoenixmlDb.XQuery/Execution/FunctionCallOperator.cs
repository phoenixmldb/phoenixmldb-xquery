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

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        // Phase B source-location wiring: install this call site's location for the
        // duration of the dispatch so any XQueryException raised by the function
        // implementation inherits accurate (module, line, col) info via context.Error().
        using var _locScope = context.PushLocation(Location);

        // Resolve function: prefer runtime-registered functions (e.g. user-declared)
        // over the statically-resolved reference (which may be a placeholder).
        var function = context.Functions.Resolve(FunctionName, ArgumentOperators.Count)
            ?? Function;

        if (function == null)
        {
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
                if (itemCount % 65536 == 0)
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

        // XPath 1.0 backwards-compat: coerce arguments to expected types.
        // Multi-item sequence → first item (for single-item parameters).
        // Empty nodeset → NaN for xs:double, empty string for xs:string.
        if (context.BackwardsCompatible && function.Parameters is { Count: > 0 })
        {
            for (var bi = 0; bi < args.Length && bi < function.Parameters.Count; bi++)
            {
                var paramType = function.Parameters[bi].Type;
                if (args[bi] is null)
                {
                    var itemType = paramType?.ItemType;
                    if (itemType is Ast.ItemType.Double or Ast.ItemType.Float or Ast.ItemType.Decimal or Ast.ItemType.Integer)
                        args[bi] = double.NaN;
                    else if (itemType is Ast.ItemType.String)
                        args[bi] = "";
                }
                else if (args[bi] is object[] arr && arr.Length > 0
                    && paramType?.Occurrence is Ast.Occurrence.ExactlyOne or Ast.Occurrence.ZeroOrOne)
                {
                    // BC mode: multi-item sequence passed to single-item parameter → take first item
                    args[bi] = arr[0];
                }
            }
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
}
