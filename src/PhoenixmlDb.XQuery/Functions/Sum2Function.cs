using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:sum($arg, $zero) as xs:anyAtomicType
/// </summary>
public sealed class Sum2Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "sum");
    public override XdmSequenceType ReturnType => XdmSequenceType.Double;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "zero"), Type = XdmSequenceType.OptionalItem }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        return SumHelper.SumCore(arguments[0], arguments[1],
            (context as Execution.QueryExecutionContext)?.NodeProvider);
    }
}
