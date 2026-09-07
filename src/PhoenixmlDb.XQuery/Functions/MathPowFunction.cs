using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// math:pow($base, $exponent) as xs:double?
/// </summary>
public sealed class MathPowFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Math, "pow");
    public override XdmSequenceType ReturnType => XdmSequenceType.Double;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [
            new() { Name = new QName(NamespaceId.None, "base"), Type = XdmSequenceType.Double },
            new() { Name = new QName(NamespaceId.None, "exponent"), Type = XdmSequenceType.Double }
        ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        if (arguments[0] is null) return ValueTask.FromResult<object?>(null);
        return ValueTask.FromResult<object?>(
            Math.Pow(NumericParseHelper.ValidateAndConvertToDouble(arguments[0], "math:pow"),
                     NumericParseHelper.ValidateAndConvertToDouble(arguments[1], "math:pow")));
    }
}
