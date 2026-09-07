using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:round($arg, $precision) as xs:numeric?
/// </summary>
public sealed class Round2Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "round");
    public override XdmSequenceType ReturnType => XdmSequenceType.Double;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.Double },
        new() { Name = new QName(NamespaceId.None, "precision"), Type = XdmSequenceType.Integer }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = QueryExecutionContext.AtomizeSingle(arguments[0]);
        arg = NumericParseHelper.ValidateNumericArg(arg, "fn:round");
        if (arg is null) return ValueTask.FromResult<object?>(null);
        var precision = QueryExecutionContext.ToInt(arguments[1]);
        object? result = arg switch
        {
            int i => precision >= 0 ? arg : (int)XPathRound.Round((double)i, precision),
            long l => precision >= 0 ? arg : RoundBigInteger(l, precision),
            System.Numerics.BigInteger bi => precision >= 0 ? arg : RoundBigInteger(bi, precision),
            decimal d => XPathRound.Round(d, precision),
            float f => XPathRound.Round(f, precision),
            _ => XPathRound.Round(Convert.ToDouble(arg), precision)
        };
        return ValueTask.FromResult<object?>(result);
    }

    /// <summary>
    /// Rounds a BigInteger to a given negative precision.
    /// XPath round semantics: round half towards positive infinity.
    /// </summary>
    private static object RoundBigInteger(System.Numerics.BigInteger value, int precision)
    {
        if (precision >= 0 || value.IsZero)
            return value;
        var scale = System.Numerics.BigInteger.Pow(10, -precision);
        var (quotient, remainder) = System.Numerics.BigInteger.DivRem(value, scale);
        // XPath round: half towards positive infinity
        var halfScale = scale / 2;
        if (value >= 0)
        {
            if (remainder >= halfScale)
                quotient++;
        }
        else
        {
            // For negative values: round half towards positive infinity means
            // -0.5 rounds to 0, not -1
            if (-remainder > halfScale)
                quotient--;
        }
        var result = quotient * scale;
        // Narrow to long if possible
        if (result >= long.MinValue && result <= long.MaxValue)
            return (long)result;
        return result;
    }
}
