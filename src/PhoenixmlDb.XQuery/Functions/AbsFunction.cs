using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:abs($arg) as xs:numeric?
/// </summary>
public sealed class AbsFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "abs");
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalAnyAtomicType;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalAnyAtomicType }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = QueryExecutionContext.AtomizeSingle(arguments[0]);
        arg = NumericParseHelper.ValidateNumericArg(arg, "fn:abs");
        if (arg is null) return ValueTask.FromResult<object?>(null);
        object? result = arg switch
        {
            int i => Math.Abs(i),
            long l => Math.Abs(l),
            System.Numerics.BigInteger bi => System.Numerics.BigInteger.Abs(bi),
            decimal d => Math.Abs(d),
            float f => Math.Abs(f),
            _ => Math.Abs(Convert.ToDouble(arg))
        };
        return ValueTask.FromResult<object?>(result);
    }
}
