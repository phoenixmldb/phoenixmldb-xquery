using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// math:asin($arg) as xs:double?
/// </summary>
public sealed class MathAsinFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Math, "asin");
    public override XdmSequenceType ReturnType => XdmSequenceType.Double;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.Double }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        if (arguments[0] is null) return ValueTask.FromResult<object?>(null);
        return ValueTask.FromResult<object?>(Math.Asin(NumericParseHelper.ValidateAndConvertToDouble(arguments[0], "math:asin")));
    }
}
