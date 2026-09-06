using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:name() as xs:string (uses context item)
/// </summary>
public sealed class Name0Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "name");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        // Use context item
        var item = context.ContextItem;
        if (item == null)
            throw new XQueryRuntimeException("XPDY0002", "Context item is absent in fn:name()");

        return item switch
        {
            XdmElement elem => ValueTask.FromResult<object?>(
                !string.IsNullOrEmpty(elem.Prefix) ? $"{elem.Prefix}:{elem.LocalName}" : elem.LocalName),
            XdmAttribute attr => ValueTask.FromResult<object?>(
                !string.IsNullOrEmpty(attr.Prefix) ? $"{attr.Prefix}:{attr.LocalName}" : attr.LocalName),
            XdmProcessingInstruction pi => ValueTask.FromResult<object?>(pi.Target),
            XdmNamespace ns => ValueTask.FromResult<object?>(ns.Prefix),
            XdmDocument or XdmText or XdmComment or TextNodeItem => ValueTask.FromResult<object?>(""),
            _ => throw new XQueryRuntimeException("XPTY0004", "Context item is not a node in fn:name()")
        };
    }
}
