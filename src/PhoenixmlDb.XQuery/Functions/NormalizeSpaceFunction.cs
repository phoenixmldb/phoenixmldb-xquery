using System.Collections.Concurrent;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:normalize-space($arg) as xs:string
/// </summary>
public sealed class NormalizeSpaceFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "normalize-space");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalString }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var nodeProvider = (context as Execution.QueryExecutionContext)?.NodeProvider;
        var str = ConcatFunction.XQueryStringValue(arguments[0], nodeProvider);
        var result = string.Join(" ", str.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return ValueTask.FromResult<object?>(result);
    }
}
