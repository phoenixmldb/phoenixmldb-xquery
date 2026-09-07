using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:distinct-values($arg, $collation) as xs:anyAtomicType*
/// </summary>
public sealed class DistinctValues2Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "distinct-values");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "collation"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg == null)
            return ValueTask.FromResult<object?>(Array.Empty<object>());

        var comparison = CollationHelper.GetStringComparison(arguments[1]?.ToString());

        if (arg is IEnumerable<object?> seq)
        {
            var comparer = new CollationValueComparer(comparison);
            var atomized = seq.Select(x => DistinctValuesFunction.AtomizeItem(x, context)).Distinct(comparer).ToArray();
            return ValueTask.FromResult<object?>(atomized);
        }

        return ValueTask.FromResult<object?>(new[] { DistinctValuesFunction.AtomizeItem(arg) });
    }
}
