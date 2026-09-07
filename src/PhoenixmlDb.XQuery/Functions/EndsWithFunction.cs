using System.Collections.Concurrent;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:ends-with($arg1, $arg2) as xs:boolean
/// </summary>
public sealed class EndsWithFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "ends-with");
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
        var atomized0 = Execution.QueryExecutionContext.Atomize(arguments[0], nodeProvider);
        var atomized1 = Execution.QueryExecutionContext.Atomize(arguments[1], nodeProvider);
        StringLengthFunction.RequireStringLike(atomized0, "ends-with");
        StringLengthFunction.RequireStringLike(atomized1, "ends-with");
        var str = ConcatFunction.XQueryStringValue(atomized0);
        var suffix = ConcatFunction.XQueryStringValue(atomized1);
        var comparison = CollationHelper.GetDefaultComparison(context);
        return ValueTask.FromResult<object?>(str.EndsWith(suffix, comparison));
    }
}
