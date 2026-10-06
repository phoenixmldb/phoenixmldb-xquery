using System.Collections.Concurrent;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:string-length() as xs:integer — zero-arity version uses context item
/// </summary>
public sealed class StringLength0Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "string-length");
    public override XdmSequenceType ReturnType => XdmSequenceType.Integer;
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
        // fn:string(.), which is the node's string value and not its typed value: a validated
        // element with element-only content has the first and not the second (FOTY0012).
        var str = ConcatFunction.XQueryStringValue(item, nodeProvider);
        return ValueTask.FromResult<object?>((long)StringLengthFunction.CountCodepoints(str));
    }
}
