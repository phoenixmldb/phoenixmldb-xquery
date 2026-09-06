using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:items-at($seq as item()*, $positions as xs:integer*) as item()* — items at positions (XPath 4.0).
/// </summary>
public sealed class ItemsAtFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "items-at");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "positions"), Type = new() { ItemType = ItemType.Integer, Occurrence = Occurrence.ZeroOrMore } }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var seq = arguments[0];
        var positions = arguments[1];
        if (seq == null || positions == null) return ValueTask.FromResult<object?>(Array.Empty<object>());

        var items = seq is object?[] arr ? arr : new[] { seq };
        var posArray = positions is object?[] pa ? pa : new[] { positions };

        var result = new List<object?>();
        foreach (var pos in posArray)
        {
            var idx = Convert.ToInt32(pos) - 1; // 1-based → 0-based
            if (idx >= 0 && idx < items.Length)
                result.Add(items[idx]);
        }
        return ValueTask.FromResult<object?>(result.Count == 1 ? result[0] : result.ToArray());
    }
}
