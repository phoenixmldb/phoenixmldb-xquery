using System.Collections.Concurrent;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:string-join($arg1) as xs:string (1-arg version, separator defaults to "")
/// </summary>
public sealed class StringJoin1Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "string-join");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "arg1"), Type = XdmSequenceType.ZeroOrMoreItems }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var nodeProvider = (context as Execution.QueryExecutionContext)?.NodeProvider;
        var items = arguments[0] as IEnumerable<object?> ?? [arguments[0]];
        var result = string.Join("", items.Select(item => ConcatFunction.XQueryStringValue(item, nodeProvider)));
        return ValueTask.FromResult<object?>(result);
    }
}
