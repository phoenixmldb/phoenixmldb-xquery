using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:for-each($seq, $action) as item()*
/// </summary>
public sealed class ForEachFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "for-each");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "action"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var seq = SequenceHelper.Flatten(arguments[0]);
        var callable = arguments[1]
            ?? throw new XQueryRuntimeException("XPTY0004", "Second argument to fn:for-each must be callable");

        var results = new List<object?>();
        foreach (var item in seq)
        {
            var result = await CallableCoercion.InvokeUnaryAsync(callable, item, context);
            if (result is IEnumerable<object?> resultSeq)
            {
                foreach (var r in resultSeq) results.Add(r);
            }
            else if (result != null)
                results.Add(result);
        }
        return results.ToArray();
    }
}
