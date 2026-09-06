using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// array:sort-with($array as array(*), $comparator as function(item()*, item()*) as xs:integer) as array(*)
/// Sorts an array using a custom comparator function (XPath 4.0).
/// </summary>
public sealed class ArraySortWithFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "sort-with");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "comparator"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        if (arguments[0] is not List<object?> list || arguments[1] is not XQueryFunction cmp)
            return new List<object?>();

        var sorted = new List<object?>(list);
        // Use insertion sort with async comparator
        for (var i = 1; i < sorted.Count; i++)
        {
            var key = sorted[i];
            var j = i - 1;
            while (j >= 0)
            {
                var cmpResult = await cmp.InvokeAsync([sorted[j], key], context).ConfigureAwait(false);
                if (Convert.ToInt32(cmpResult) <= 0) break;
                sorted[j + 1] = sorted[j];
                j--;
            }
            sorted[j + 1] = key;
        }
        return sorted;
    }
}
