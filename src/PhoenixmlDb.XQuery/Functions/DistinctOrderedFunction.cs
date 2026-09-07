using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:distinct-ordered($seq as xs:anyAtomicType*) as xs:anyAtomicType*
/// Returns distinct values preserving first-occurrence order (XPath 4.0).
/// </summary>
public sealed class DistinctOrderedFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "distinct-ordered");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var seq = arguments[0];
        if (seq == null) return ValueTask.FromResult<object?>(Array.Empty<object>());
        var items = seq is object?[] arr ? arr : new[] { seq };

        var seen = new HashSet<string>();
        var result = new List<object?>();
        foreach (var item in items)
        {
            var val = QueryExecutionContext.Atomize(item)?.ToString() ?? "";
            if (seen.Add(val))
                result.Add(item);
        }
        return ValueTask.FromResult<object?>(result.Count == 1 ? result[0] : result.ToArray());
    }
}
