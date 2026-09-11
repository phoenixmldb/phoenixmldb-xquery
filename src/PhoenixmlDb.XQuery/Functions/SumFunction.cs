using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:sum($arg) as xs:anyAtomicType
/// </summary>
public sealed class SumFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "sum");
    public override XdmSequenceType ReturnType => XdmSequenceType.Double;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        return SumHelper.SumCore(arguments[0], (long)0,
            (context as Execution.QueryExecutionContext)?.NodeProvider);
    }
}
