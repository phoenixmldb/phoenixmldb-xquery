using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// array:sort($array, $collation) as array(*)
/// </summary>
public sealed class ArraySort2Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "sort");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "collation"), Type = XdmSequenceType.OptionalString }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var array = arguments[0] as IList<object?> ?? [];
        // An absent/empty collation ($collation = ()) selects the dynamic context's default
        // collation, not codepoint (F&O §17.3.1). ResolveAndGetComparison(null) would fall
        // back to codepoint, so branch explicitly.
        var collationArg = arguments[1]?.ToString();
        var comparison = string.IsNullOrEmpty(collationArg)
            ? CollationHelper.GetDefaultComparison(context)
            : CollationHelper.ResolveAndGetComparison(collationArg, context);
        // Sort members by their atomized value using the specified collation
        var keyed = array.Select(item => (item, keys: ArraySortFunction.AtomizeForSortKeys(item))).ToList();
        keyed.Sort((a, b) => SortHelper.CompareKeySequences(a.keys, b.keys, comparison));
        return ValueTask.FromResult<object?>(keyed.Select(k => k.item).ToList());
    }
}
