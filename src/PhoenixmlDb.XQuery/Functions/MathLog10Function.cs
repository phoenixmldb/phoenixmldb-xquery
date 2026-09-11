using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// math:log10($arg) as xs:double?
/// </summary>
public sealed class MathLog10Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Math, "log10");
    public override XdmSequenceType ReturnType => XdmSequenceType.Double;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = new XdmSequenceType { ItemType = ItemType.Double, Occurrence = Occurrence.ZeroOrOne } }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        if (arguments[0] is null) return ValueTask.FromResult<object?>(null);
        return ValueTask.FromResult<object?>(Math.Log10(NumericParseHelper.ValidateAndConvertToDouble(arguments[0], "math:log10")));
    }
}
