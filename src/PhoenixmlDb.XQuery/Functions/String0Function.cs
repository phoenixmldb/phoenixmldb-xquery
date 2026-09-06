using System.Collections.Concurrent;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:string() as xs:string (uses context item)
/// </summary>
public sealed class String0Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "string");
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
        // Use XQueryStringValue for proper formatting (e.g., double -INF → "-INF" not "-Infinity")
        var nodeProvider = (context as Execution.QueryExecutionContext)?.NodeProvider;
        return ValueTask.FromResult<object?>(ConcatFunction.XQueryStringValue(item, nodeProvider));
    }
}
