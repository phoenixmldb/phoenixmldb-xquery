using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// math:atan2($y, $x) as xs:double
/// </summary>
public sealed class MathAtan2Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Math, "atan2");
    public override XdmSequenceType ReturnType => XdmSequenceType.Double;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [
            new() { Name = new QName(NamespaceId.None, "y"), Type = XdmSequenceType.Double },
            new() { Name = new QName(NamespaceId.None, "x"), Type = XdmSequenceType.Double }
        ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        return ValueTask.FromResult<object?>(
            Math.Atan2(NumericParseHelper.ValidateAndConvertToDouble(arguments[0], "math:atan2"),
                       NumericParseHelper.ValidateAndConvertToDouble(arguments[1], "math:atan2")));
    }
}
