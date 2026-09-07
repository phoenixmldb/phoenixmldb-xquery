using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:number() as xs:double (uses context item)
/// </summary>
public sealed class Number0Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "number");
    public override XdmSequenceType ReturnType => XdmSequenceType.Double;
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var contextItem = context.ContextItem;
        if (contextItem == null)
            throw context.Error("XPDY0002", "Context item is absent for fn:number()");

        // Delegate to the 1-argument number() function
        return new NumberFunction().InvokeAsync([contextItem], context);
    }
}
