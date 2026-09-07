using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:intersperse($seq as item()*, $sep as item()) as item()* — inserts single item between (XPath 4.0).
/// Simpler version of fn:insert-separator for single-item separators.
/// </summary>
public sealed class IntersperseFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "intersperse");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "sep"), Type = XdmSequenceType.Item }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var seq = arguments[0];
        var sep = arguments[1];
        if (seq == null) return ValueTask.FromResult<object?>(Array.Empty<object>());
        var items = seq is object?[] arr ? arr : new[] { seq };
        if (items.Length <= 1) return ValueTask.FromResult<object?>(seq);

        var result = new List<object?>(items.Length * 2 - 1);
        for (var i = 0; i < items.Length; i++)
        {
            if (i > 0) result.Add(sep);
            result.Add(items[i]);
        }
        return ValueTask.FromResult<object?>(result.ToArray());
    }
}
