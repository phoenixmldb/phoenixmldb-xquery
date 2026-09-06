using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:remove($target, $position) as item()*
/// </summary>
public sealed class RemoveFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "remove");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "target"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "position"), Type = XdmSequenceType.Integer }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var target = arguments[0];
        var posArg = arguments[1];
        // XPTY0004: position must be xs:integer
        if (posArg is double or float or decimal or string or Xdm.XsAnyUri)
            throw new Execution.XQueryRuntimeException("XPTY0004",
                $"fn:remove: position must be xs:integer, got {posArg.GetType().Name}");
        var position = QueryExecutionContext.ToInt(posArg);

        if (target == null)
            return ValueTask.FromResult<object?>(Array.Empty<object>());

        var seq = target is object?[] arr ? arr : target is IEnumerable<object?> t ? t.ToArray() : [target];

        // XPath uses 1-based indexing
        var removeIndex = position - 1;
        if (removeIndex < 0 || removeIndex >= seq.Length)
            return ValueTask.FromResult<object?>(seq);

        var result = new object?[seq.Length - 1];
        Array.Copy(seq, 0, result, 0, removeIndex);
        Array.Copy(seq, removeIndex + 1, result, removeIndex, seq.Length - removeIndex - 1);
        return ValueTask.FromResult<object?>(result);
    }
}
