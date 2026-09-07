using System.Collections.Concurrent;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:string-join($arg1, $arg2) as xs:string
/// </summary>
public sealed class StringJoinFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "string-join");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "arg1"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "arg2"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        if (arguments[1] is null)
            throw context.Error("XPTY0004", "Separator argument to fn:string-join cannot be an empty sequence");
        var nodeProvider = (context as Execution.QueryExecutionContext)?.NodeProvider;
        var items = arguments[0] as IEnumerable<object?> ?? [arguments[0]];
        var separator = ConcatFunction.XQueryStringValue(arguments[1], nodeProvider);
        var result = string.Join(separator, items.Select(item => ConcatFunction.XQueryStringValue(item, nodeProvider)));
        return ValueTask.FromResult<object?>(result);
    }
}
