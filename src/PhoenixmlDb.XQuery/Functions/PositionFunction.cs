using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:position() as xs:integer
/// </summary>
public sealed class PositionFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "position");
    public override XdmSequenceType ReturnType => XdmSequenceType.Integer;
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        if (context is Execution.QueryExecutionContext qec)
        {
            // Access Position which throws XPDY0002 if focus is absent
            return ValueTask.FromResult<object?>(qec.Position);
        }
        return ValueTask.FromResult<object?>(1);
    }
}
