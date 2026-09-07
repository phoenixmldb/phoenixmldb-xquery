using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:scan-left($seq as item()*, $zero as item()*, $f as function(item()*, item()) as item()*)
/// Cumulative left reduction — returns all intermediate results (XPath 4.0).
/// </summary>
public sealed class ScanLeftFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "scan-left");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "zero"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "f"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var seq = arguments[0];
        var accumulator = arguments[1];
        var fn = arguments[2] as XQueryFunction;
        if (fn == null) return Array.Empty<object>();

        var items = seq is object?[] arr ? arr : (seq != null ? new[] { seq } : Array.Empty<object?>());
        var results = new List<object?> { accumulator };

        foreach (var item in items)
        {
            accumulator = await fn.InvokeAsync([accumulator, item], context).ConfigureAwait(false);
            results.Add(accumulator);
        }
        return results.ToArray();
    }
}
