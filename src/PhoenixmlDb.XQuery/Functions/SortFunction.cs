using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:sort($input) as item()*
/// </summary>
public sealed class SortFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "sort");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "input"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var items = SequenceHelper.Flatten(arguments[0]);
        var cmp = CollationHelper.GetDefaultComparison(context);
        SortHelper.SortByAtomicKey(items, cmp);
        return ValueTask.FromResult<object?>(items.ToArray());
    }
}
