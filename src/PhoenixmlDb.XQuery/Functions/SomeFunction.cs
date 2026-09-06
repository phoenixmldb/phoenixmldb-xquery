using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:some($seq as item()*, $pred as function(item()) as xs:boolean) as xs:boolean
/// Tests if any item in the sequence satisfies the predicate (XPath 4.0).
/// </summary>
public sealed class SomeFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "some");
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
        if (seq == null || pred == null) return false;
        var items = seq is object?[] arr ? arr : new[] { seq };

        foreach (var item in items)
        {
            var result = await pred.InvokeAsync([item], context).ConfigureAwait(false);
            if (result is true || (result is not false && result != null))
                return true;
        }
        return false;
    }
}
