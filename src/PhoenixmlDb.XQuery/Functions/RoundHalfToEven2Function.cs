using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:round-half-to-even($arg, $precision) as numeric?
/// </summary>
public sealed class RoundHalfToEven2Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "round-half-to-even");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Double, Occurrence = Occurrence.ZeroOrOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "arg"), Type = new() { ItemType = ItemType.Double, Occurrence = Occurrence.ZeroOrOne } },
        new() { Name = new QName(NamespaceId.None, "precision"), Type = XdmSequenceType.Integer }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        // Validate precision type: must be integer, not string
        var precArg = arguments[1];
        if (precArg is string)
            throw new XQueryRuntimeException("XPTY0004",
                "fn:round-half-to-even: precision argument must be xs:integer, got xs:string");

        // Handle very large precision values that exceed int.MaxValue.
        // A precision beyond the number's significant digits is a no-op.
        int precision;
        if (precArg is long pl)
            precision = (int)Math.Clamp(pl, int.MinValue, int.MaxValue);
        else
            precision = QueryExecutionContext.ToInt(precArg);
        return RoundHalfToEvenImpl(arguments[0], precision);
    }

    internal static ValueTask<object?> RoundHalfToEvenImpl(object? arg, int precision, Ast.ExecutionContext? context = null)
    {
        if (arg == null) return ValueTask.FromResult<object?>(null);

        // Type checking: argument must be numeric
        NumericParseHelper.ValidateNumericArg(arg, "fn:round-half-to-even");

        // Preserve input type per XQuery spec
        return arg switch
        {
            long l => ValueTask.FromResult<object?>(precision >= 0 ? l : RoundIntegerNeg(l, precision)),
            int i => ValueTask.FromResult<object?>((long)(precision >= 0 ? i : RoundIntegerNeg(i, precision))),
            decimal m => ValueTask.FromResult<object?>(RoundDecimal(m, precision)),
            float f => ValueTask.FromResult<object?>((float)RoundDouble(f, precision)),
            double d => ValueTask.FromResult<object?>(RoundDouble(d, precision)),
            _ => ValueTask.FromResult<object?>(RoundDouble(QueryExecutionContext.ToDouble(arg), precision))
        };
    }

    private static decimal RoundDecimal(decimal val, int precision, Ast.ExecutionContext? context = null)
    {
        if (precision >= 0)
        {
            // Decimal supports at most 28 digits of precision; values beyond that are no-ops
            if (precision > 28) return val;
            return Math.Round(val, precision, MidpointRounding.ToEven);
        }
        // Negative precision: round to nearest 10^(-precision)
        var scale = (decimal)Math.Pow(10, -precision);
        return Math.Round(val / scale, MidpointRounding.ToEven) * scale;
    }

    private static long RoundIntegerNeg(long val, int precision, Ast.ExecutionContext? context = null)
    {
        var scale = (long)Math.Pow(10, -precision);
        var half = scale / 2;
        var remainder = val % scale;
        var truncated = val - remainder;
        if (Math.Abs(remainder) > half) return truncated + (val > 0 ? scale : -scale);
        if (Math.Abs(remainder) == half) return truncated % (2 * scale) == 0 ? truncated : truncated + (val > 0 ? scale : -scale);
        return truncated;
    }

    /// <summary>
    /// Rounds a double half-to-even at the given decimal precision, deciding ties from the
    /// double's ACTUAL binary value rather than from its shortest decimal spelling.
    /// </summary>
    /// <remarks>
    /// This used Math.Round(val, precision, MidpointRounding.ToEven). .NET's double overload
    /// applies a decimal-style correction, which manufactures ties that do not exist in binary:
    ///
    ///     round-half-to-even(250.0250e0, 2)   gave 250.02, must be 250.03
    ///
    /// The nearest double to 250.025 is 250.025000000000005684341886080801486968994140625 —
    /// strictly ABOVE the midpoint, so it is not a tie at all and rounds up. Math.Round saw a
    /// "250.025" and applied half-to-even to a tie the value does not have. The sibling cases
    /// happen to come out right for the same wrong reason: 150.015 as a double is BELOW its
    /// midpoint and 180.018 above, so decimal-thinking and binary agree there. Exactly the
    /// distinction w3c/xslt30-test issue 79 corrected in 2024 (test math-3303), which was
    /// invisible here until the XSLT runner stopped passing &lt;assert&gt; unconditionally.
    ///
    /// A double is mantissa x 2^exp exactly, so |val| x 10^p is the exact rational N/D below.
    /// Rounding it is then integer arithmetic: compare 2r against D — greater rounds up, less
    /// rounds down, and only EQUAL is a genuine tie, where half-to-even applies. No binary
    /// floating point is involved in the decision, so no spurious tie can arise.
    /// </remarks>
    private static double RoundDouble(double val, int precision, Ast.ExecutionContext? context = null)
    {
        if (double.IsNaN(val) || double.IsInfinity(val) || val == 0.0) return val;

        // Beyond these the answer cannot change: a double carries ~17 significant digits and
        // spans about 1e-324 to 1e308, so rounding far to the right of the value is identity
        // and far to the left collapses it to a signed zero. The guard also keeps
        // BigInteger.Pow off absurd exponents — precision arrives clamped to int range.
        if (precision > 340) return val;
        if (precision < -340) return double.IsNegative(val) ? -0.0 : 0.0;

        var bits = BitConverter.DoubleToInt64Bits(val);
        var negative = bits < 0;
        var biasedExponent = (int)((bits >> 52) & 0x7FF);
        var mantissa = bits & 0xF_FFFF_FFFF_FFFFL;
        if (biasedExponent == 0)
            biasedExponent = 1;                     // subnormal: no implicit leading bit
        else
            mantissa |= 1L << 52;                   // normal: restore the implicit bit
        var exponent = biasedExponent - 1075;       // |val| == mantissa * 2^exponent

        // |val| * 10^precision as an exact rational N/D.
        var pow10 = System.Numerics.BigInteger.Pow(10, Math.Abs(precision));
        System.Numerics.BigInteger n = mantissa, d = System.Numerics.BigInteger.One;
        if (precision >= 0) n *= pow10; else d *= pow10;
        if (exponent >= 0) n <<= exponent; else d <<= -exponent;

        var q = System.Numerics.BigInteger.DivRem(n, d, out var r);

        // Exactly representable at this precision: there is nothing to round, and the answer is
        // the input. Returning it directly also avoids the conversion below, which is NOT exact
        // once 10^precision outgrows a double — 10^100 is not representable, so dividing by it
        // rounds twice and can land on a neighbouring double. round-half-to-even(1.2345e0, 100)
        // came back 1.2345000000000002: a no-op that changed the value.
        //
        // This covers every large precision, not just a lucky few: |val| is mantissa * 2^-k with
        // k <= 1074, and 10^p carries a factor of 2^p, so any p >= k cancels the denominator
        // outright and leaves r zero.
        if (r.IsZero) return val;

        var cmp = (r << 1).CompareTo(d);
        if (cmp > 0 || (cmp == 0 && !q.IsEven)) q += System.Numerics.BigInteger.One;

        // Back to double. Each conversion below is individually correctly rounded, so the
        // result is the nearest double to the rounded value.
        double magnitude = precision >= 0
            ? (double)q / (double)pow10
            : (double)(q * pow10);

        return negative ? -magnitude : magnitude;
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// XPath/XQuery 4.0 new functions
// ═══════════════════════════════════════════════════════════════════════════
