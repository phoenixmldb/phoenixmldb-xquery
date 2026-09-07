using System.Collections.Concurrent;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:default-collation() as xs:string
/// </summary>
public sealed class DefaultCollationFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "default-collation");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        if (context is Execution.QueryExecutionContext qec && qec.DefaultCollation != null)
            return ValueTask.FromResult<object?>(qec.DefaultCollation);
        return ValueTask.FromResult<object?>("http://www.w3.org/2005/xpath-functions/collation/codepoint");
    }
}
