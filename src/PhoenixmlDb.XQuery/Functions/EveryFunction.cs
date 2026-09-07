using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:every($seq as item()*, $pred as function(item()) as xs:boolean) as xs:boolean
/// Tests if all items in the sequence satisfy the predicate (XPath 4.0).
/// </summary>
public sealed class EveryFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "every");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "pred"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var seq = arguments[0];
        var pred = arguments[1] as XQueryFunction;
        if (pred == null) return false;
        if (seq == null) return true;
        var items = seq is object?[] arr ? arr : new[] { seq };

        foreach (var item in items)
        {
            var result = await pred.InvokeAsync([item], context).ConfigureAwait(false);
            if (!(result is true || (result is not false && result != null)))
                return false;
        }
        return true;
    }
}
