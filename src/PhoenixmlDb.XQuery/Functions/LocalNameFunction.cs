using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:local-name($arg) as xs:string
/// </summary>
public sealed class LocalNameFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "local-name");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalNode }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0] is object[] arr ? (arr.Length > 0 ? arr[0] : null) : arguments[0];
        if (arg == null)
            return ValueTask.FromResult<object?>("");

        return arg switch
        {
            XdmElement elem => ValueTask.FromResult<object?>(elem.LocalName),
            XdmAttribute attr => ValueTask.FromResult<object?>(attr.LocalName),
            XdmProcessingInstruction pi => ValueTask.FromResult<object?>(pi.Target),
            XdmNamespace ns => ValueTask.FromResult<object?>(ns.Prefix),
            _ => ValueTask.FromResult<object?>("")
        };
    }
}
