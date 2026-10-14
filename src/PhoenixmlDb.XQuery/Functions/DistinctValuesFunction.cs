using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:distinct-values($arg) as xs:anyAtomicType*
/// Atomizes the input sequence and returns distinct atomic values.
/// </summary>
public sealed class DistinctValuesFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "distinct-values");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg == null)
            return ValueTask.FromResult<object?>(Array.Empty<object>());

        var comparison = CollationHelper.GetDefaultComparison(context);
        IEqualityComparer<object?> comparer = comparison == StringComparison.Ordinal
            ? XQueryValueComparer.Instance
            : new CollationValueComparer(comparison);

        if (arg is IEnumerable<object?> seq)
        {
            // Atomize each item and then get distinct values using XQuery value equality
            var atomized = Flattened(seq, context).Distinct(comparer).ToArray();
            return ValueTask.FromResult<object?>(atomized);
        }

        return ValueTask.FromResult<object?>(new[] { AtomizeItem(arg) });
    }

    /// <summary>The atomized items of a sequence; a node of a list type gives each item of its typed value.</summary>
    internal static IEnumerable<object?> Flattened(IEnumerable<object?> sequence, Ast.ExecutionContext? context)
    {
        foreach (var item in sequence)
        {
            var atomized = AtomizeItem(item, context);
            if (atomized is object?[] many && item is XdmNode)
            {
                foreach (var one in many)
                    yield return one;
            }
            else
            {
                yield return atomized;
            }
        }
    }

    internal static object? AtomizeItem(object? item, Ast.ExecutionContext? context = null)
    {
        // Route element/document string values through the execution context's node provider so
        // storage-deserialized nodes (NULL precomputed StringValue, lazily-resolved children)
        // atomize correctly via descendant text-node walking, mirroring fn:string() (#163).
        var nodeProvider = (context as Execution.QueryExecutionContext)?.NodeProvider;
        return item switch
        {
            null => null,
            // A validated node gives its typed value: two NOTATION or QName values with
            // different prefixes for one namespace are one value, and 1.0 and 1 of a decimal
            // type are one value. The string value was compared.
            XdmElement typedElem when Execution.TypeCastHelper.HasSchemaTypedValue(typedElem.TypeAnnotation)
                => Execution.QueryExecutionContext.AtomizeTyped(typedElem, nodeProvider),
            XdmAttribute typedAttr when Execution.TypeCastHelper.HasSchemaTypedValue(typedAttr.TypeAnnotation)
                => Execution.QueryExecutionContext.AtomizeTyped(typedAttr, nodeProvider),
            XdmElement elem => Execution.QueryExecutionContext.ComputeElementStringValue(elem, nodeProvider),
            XdmAttribute attr => attr.Value,
            XdmText text => text.Value,
            XdmComment comment => comment.Value,
            XdmProcessingInstruction pi => pi.Value,
            XdmDocument doc => Execution.QueryExecutionContext.ComputeDocumentStringValue(doc, nodeProvider),
            IDictionary<object, object?> => throw context.Error("FOTY0013", "Atomization is not defined for maps"),
            List<object?> => throw context.Error("FOTY0013", "Atomization is not defined for arrays"),
            XQueryFunction => throw context.Error("FOTY0013", "Atomization is not defined for function items"),
            _ => item // Already atomic
        };
    }
}
