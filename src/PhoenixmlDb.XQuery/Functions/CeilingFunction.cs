using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:ceiling($arg) as xs:numeric?
/// </summary>
public sealed class CeilingFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "ceiling");
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalAnyAtomicType;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalAnyAtomicType }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = QueryExecutionContext.AtomizeSingle(arguments[0]);
        arg = NumericParseHelper.ValidateNumericArg(arg, "fn:ceiling");
        if (arg is null) return ValueTask.FromResult<object?>(null);
        object? result = arg switch
        {
            int or long or System.Numerics.BigInteger => arg, // integers are already whole
            decimal d => decimal.Ceiling(d),
            float f => (float)Math.Ceiling(f),
            _ => Math.Ceiling(Convert.ToDouble(arg))
        };
        return ValueTask.FromResult<object?>(result);
    }
}
