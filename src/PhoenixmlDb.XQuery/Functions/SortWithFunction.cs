using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:sort-with($seq as item()*, $comparator as function(item(), item()) as xs:integer) as item()*
/// Sorts a sequence using a custom comparator (XPath 4.0).
/// </summary>
public sealed class SortWithFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "sort-with");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "comparator"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var seq = arguments[0];
        var cmp = arguments[1] as XQueryFunction;
        if (seq == null || cmp == null) return Array.Empty<object>();
        var items = seq is object?[] arr ? arr.ToList() : new List<object?> { seq };

        // Insertion sort with async comparator
        for (var i = 1; i < items.Count; i++)
        {
            var key = items[i];
            var j = i - 1;
            while (j >= 0)
            {
                var cmpResult = await cmp.InvokeAsync([items[j], key], context).ConfigureAwait(false);
                if (Convert.ToInt32(cmpResult) <= 0) break;
                items[j + 1] = items[j];
                j--;
            }
            items[j + 1] = key;
        }
        return items.Count == 1 ? items[0] : items.ToArray();
    }
}
