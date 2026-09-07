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
        var atomized = Execution.QueryExecutionContext.Atomize(item, nodeProvider);
        var str = ConcatFunction.XQueryStringValue(atomized);
        return ValueTask.FromResult<object?>((long)StringLengthFunction.CountCodepoints(str));
    }
}
