using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:replicate($seq as item()*, $count as xs:integer) as item()* — repeats a sequence (XPath 4.0).
/// </summary>
public sealed class ReplicateFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "replicate");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "count"), Type = XdmSequenceType.Integer }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var seq = arguments[0];
        var count = Convert.ToInt32(arguments[1]);
        if (count <= 0 || seq == null) return ValueTask.FromResult<object?>(Array.Empty<object>());

        var items = seq is object?[] arr ? arr : new[] { seq };
        if (count == 1) return ValueTask.FromResult<object?>(seq);

        var result = new List<object?>(items.Length * count);
        for (var i = 0; i < count; i++)
            result.AddRange(items);
        return ValueTask.FromResult<object?>(result.ToArray());
    }
}
