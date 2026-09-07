using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:sort-by($seq as item()*, $key as function(item()) as xs:anyAtomicType?) as item()*
/// Sorts by a key function (XPath 4.0).
/// </summary>
public sealed class SortByFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "sort-by");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "key"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var seq = arguments[0];
        var keyFn = arguments[1] as XQueryFunction;
        if (seq == null || keyFn == null) return Array.Empty<object>();
        var items = seq is object?[] arr ? arr.ToList() : new List<object?> { seq };

        var keyed = new List<(object? Item, string Key)>();
        foreach (var item in items)
        {
            var key = await keyFn.InvokeAsync([item], context).ConfigureAwait(false);
            keyed.Add((item, QueryExecutionContext.Atomize(key)?.ToString() ?? ""));
        }

        keyed.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.Ordinal));
        var result = keyed.Select(k => k.Item).ToList();
        return result.Count == 1 ? result[0] : result.ToArray();
    }
}
