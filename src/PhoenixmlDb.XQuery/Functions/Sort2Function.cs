using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:sort($input, $collation) as item()*
/// </summary>
public sealed class Sort2Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "sort");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "input"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "collation"), Type = XdmSequenceType.OptionalString }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var items = SequenceHelper.Flatten(arguments[0]);
        var cmp = ResolveCollation(arguments[1], context);
        SortHelper.SortByAtomicKey(items, cmp);
        return ValueTask.FromResult<object?>(items.ToArray());
    }

    internal static StringComparison ResolveCollation(object? collArg, Ast.ExecutionContext context)
    {
        // Handle empty sequence (null, empty array) → use default collation
        var collUri = collArg is object?[] arr && arr.Length == 0 ? null : collArg?.ToString();
        return string.IsNullOrEmpty(collUri)
            ? CollationHelper.GetDefaultComparison(context)
            : CollationHelper.GetStringComparison(collUri);
    }
}
