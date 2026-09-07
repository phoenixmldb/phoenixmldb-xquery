using System.Collections.Concurrent;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:normalize-space() as xs:string (uses context item)
/// </summary>
public sealed class NormalizeSpace0Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "normalize-space");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        object? item = null;
        if (context is Execution.QueryExecutionContext qec)
            item = qec.ContextItem;
        if (item is null)
            throw new Execution.XQueryRuntimeException("XPDY0002", "Context item is absent");
        var nodeProvider = (context as Execution.QueryExecutionContext)?.NodeProvider;
        var atomized = Execution.QueryExecutionContext.Atomize(item, nodeProvider);
        var str = ConcatFunction.XQueryStringValue(atomized);
        var result = string.Join(" ", str.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return ValueTask.FromResult<object?>(result);
    }
}
