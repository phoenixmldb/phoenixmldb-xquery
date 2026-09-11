using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// math:exp($arg) as xs:double?
/// </summary>
public sealed class MathExpFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Math, "exp");
    public override XdmSequenceType ReturnType => XdmSequenceType.Double;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = new XdmSequenceType { ItemType = ItemType.Double, Occurrence = Occurrence.ZeroOrOne } }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        if (arguments[0] is null) return ValueTask.FromResult<object?>(null);
        return ValueTask.FromResult<object?>(Math.Exp(NumericParseHelper.ValidateAndConvertToDouble(arguments[0], "math:exp")));
    }
}
