using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// array:sort-by($array as array(*), $key as function(item()*) as xs:anyAtomicType) as array(*)
/// Sorts an array by a key function (XPath 4.0).
/// </summary>
public sealed class ArraySortByFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "sort-by");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "key"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        if (arguments[0] is not List<object?> list || arguments[1] is not XQueryFunction keyFn)
            return new List<object?>();

        // Compute keys for all members
        var keyed = new List<(object? Member, string Key)>();
        foreach (var member in list)
        {
            var key = await keyFn.InvokeAsync([member], context).ConfigureAwait(false);
            keyed.Add((member, Execution.QueryExecutionContext.Atomize(key)?.ToString() ?? ""));
        }

        keyed.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.Ordinal));
        return new List<object?>(keyed.Select(k => k.Member));
    }
}
