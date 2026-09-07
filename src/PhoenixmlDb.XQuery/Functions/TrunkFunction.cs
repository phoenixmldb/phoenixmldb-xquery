using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:trunk($seq as item()*) as item()* — all items except the last (XPath 4.0).
/// </summary>
public sealed class TrunkFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "trunk");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg == null) return ValueTask.FromResult<object?>(Array.Empty<object>());
        if (arg is object?[] arr) return ValueTask.FromResult<object?>(arr.Length > 1 ? arr[..^1] : Array.Empty<object?>());
        if (arg is IEnumerable<object?> seq)
        {
            var list = seq.ToList();
            return ValueTask.FromResult<object?>(list.Count > 1 ? list.GetRange(0, list.Count - 1).ToArray() : Array.Empty<object?>());
        }
        return ValueTask.FromResult<object?>(Array.Empty<object>()); // single item → empty
    }
}
