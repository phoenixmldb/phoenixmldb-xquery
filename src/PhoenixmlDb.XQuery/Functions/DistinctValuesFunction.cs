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
            var atomized = seq.Select(x => AtomizeItem(x, context)).Distinct(comparer).ToArray();
            return ValueTask.FromResult<object?>(atomized);
        }

        return ValueTask.FromResult<object?>(new[] { AtomizeItem(arg) });
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
