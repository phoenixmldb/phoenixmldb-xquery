using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:is-NaN($arg as xs:anyAtomicType) as xs:boolean — tests for NaN (XPath 4.0).
/// </summary>
public sealed class IsNaNFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "is-NaN");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.Item }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var val = QueryExecutionContext.Atomize(arguments[0]);
        var isNaN = val is double d && double.IsNaN(d)
            || val is float f && float.IsNaN(f);
        return ValueTask.FromResult<object?>(isNaN);
    }
}
