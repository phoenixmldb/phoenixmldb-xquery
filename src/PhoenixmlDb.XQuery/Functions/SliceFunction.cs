using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:slice($seq as item()*, $start as xs:integer?, $end as xs:integer?,
///          $step as xs:integer?) as item()* — flexible subsequence (XPath 4.0).
/// </summary>
public sealed class SliceFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "slice");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "start"), Type = new() { ItemType = ItemType.Integer, Occurrence = Occurrence.ZeroOrOne } },
        new() { Name = new QName(NamespaceId.None, "end"), Type = new() { ItemType = ItemType.Integer, Occurrence = Occurrence.ZeroOrOne } },
        new() { Name = new QName(NamespaceId.None, "step"), Type = new() { ItemType = ItemType.Integer, Occurrence = Occurrence.ZeroOrOne } }
    ];
    public override bool IsVariadic => true;
    public override int MinArity => 1;
    public override int MaxArity => 4;

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var seq = arguments[0];
        if (seq == null) return ValueTask.FromResult<object?>(Array.Empty<object>());
        var items = seq is object?[] arr ? arr : new[] { seq };
        var len = items.Length;

        var start = arguments.Count > 1 && arguments[1] != null ? Convert.ToInt32(arguments[1]) : 1;
        var end = arguments.Count > 2 && arguments[2] != null ? Convert.ToInt32(arguments[2]) : len;
        var step = arguments.Count > 3 && arguments[3] != null ? Convert.ToInt32(arguments[3]) : 1;

        // Handle negative indices (Python-style: -1 = last)
        if (start < 0) start = len + start + 1;
        if (end < 0) end = len + end + 1;
        if (step == 0) return ValueTask.FromResult<object?>(Array.Empty<object>());

        var result = new List<object?>();
        if (step > 0)
        {
            for (var i = Math.Max(1, start); i <= Math.Min(len, end); i += step)
                result.Add(items[i - 1]);
        }
        else
        {
            for (var i = Math.Min(len, start); i >= Math.Max(1, end); i += step)
                result.Add(items[i - 1]);
        }
        return ValueTask.FromResult<object?>(result.Count == 1 ? result[0] : result.ToArray());
    }
}
