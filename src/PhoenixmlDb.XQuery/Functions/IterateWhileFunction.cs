using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:iterate-while($seed as item()*, $fn as function(item()*) as item()*,
///                  $pred as function(item()*) as xs:boolean) as item()*
/// Iteratively applies fn while pred returns true (XPath 4.0).
/// </summary>
public sealed class IterateWhileFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "iterate-while");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "seed"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "fn"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "pred"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var current = arguments[0];
        var fn = arguments[1] as XQueryFunction;
        var pred = arguments[2] as XQueryFunction;
        if (fn == null || pred == null) return current;

        const int maxIterations = 10000;
        for (var i = 0; i < maxIterations; i++)
        {
            var shouldContinue = await pred.InvokeAsync([current], context).ConfigureAwait(false);
            if (!(shouldContinue is true || (shouldContinue is not false && shouldContinue != null)))
                break;
            current = await fn.InvokeAsync([current], context).ConfigureAwait(false);
        }
        return current;
    }
}
