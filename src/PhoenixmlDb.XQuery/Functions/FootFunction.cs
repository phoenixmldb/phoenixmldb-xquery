using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:foot($seq as item()*) as item()? — returns the last item (XPath 4.0).
/// </summary>
public sealed class FootFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "foot");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg == null) return ValueTask.FromResult<object?>(null);
        if (arg is object?[] arr) return ValueTask.FromResult<object?>(arr.Length > 0 ? arr[^1] : null);
        if (arg is IEnumerable<object?> seq) return ValueTask.FromResult<object?>(seq.LastOrDefault());
        return ValueTask.FromResult<object?>(arg); // single item
    }
}
