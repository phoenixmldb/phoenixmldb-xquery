using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:last() as xs:integer
/// </summary>
public sealed class LastFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "last");
    public override XdmSequenceType ReturnType => XdmSequenceType.Integer;
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        if (context is Execution.QueryExecutionContext qec)
        {
            return ValueTask.FromResult<object?>(qec.Last);
        }
        return ValueTask.FromResult<object?>(1);
    }
}
