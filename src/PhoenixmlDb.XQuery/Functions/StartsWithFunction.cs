using System.Collections.Concurrent;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:starts-with($arg1, $arg2) as xs:boolean
/// </summary>
public sealed class StartsWithFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "starts-with");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "arg1"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "arg2"), Type = XdmSequenceType.OptionalString }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var nodeProvider = (context as Execution.QueryExecutionContext)?.NodeProvider;
        var str = ConcatFunction.XQueryStringValue(Execution.QueryExecutionContext.Atomize(arguments[0], nodeProvider));
        var prefix = ConcatFunction.XQueryStringValue(Execution.QueryExecutionContext.Atomize(arguments[1], nodeProvider));
        var comparison = CollationHelper.GetDefaultComparison(context);
        return ValueTask.FromResult<object?>(str.StartsWith(prefix, comparison));
    }
}
