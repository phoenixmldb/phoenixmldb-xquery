using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// XPath fn:round semantics: round half towards positive infinity.
/// For non-negative values, AwayFromZero is identical.
/// For negative values, AwayFromZero is correct for all non-midpoint cases
/// (e.g., -13.65 → -13.7 correctly) but rounds the wrong direction at exact
/// midpoints (e.g., -2.5 → -3 instead of -2). We use AwayFromZero then fix
/// exact midpoints by checking the distance from the rounded result.
/// </summary>
internal static class XPathRound
{
    public static double Round(double value, int precision = 0)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value == 0.0)
            return value;
        if (precision < 0)
        {
            var scale = Math.Pow(10, -precision);
            var result = Math.Round(value / scale, MidpointRounding.AwayFromZero) * scale;
            if (value < 0 && result < value)
            {
                // AwayFromZero rounded more negative. Check if midpoint.
                var candidate = result + scale;
                if (value == (result + candidate) / 2.0)
                    return candidate;
            }
            return result;
        }
        // For high precision values, shift into Math.Round's 0-15 range
        if (precision > 15)
        {
            // Find the magnitude of the value
            var abs = Math.Abs(value);
            if (abs == 0) return value;
            int exp = (int)Math.Floor(Math.Log10(abs));
            // shift = how many decimal places to shift so rounding is in range 0-15
            // e.g., value=9.9e-99, precision=99 → exp=-99, shift=84 →
            //   scaled=9.9e-15, roundPrec=15
            int shift = precision - 15;
            if (shift > 0)
            {
                var scaleFactor = Math.Pow(10, shift);
                var scaled = value * scaleFactor;
                if (double.IsInfinity(scaled))
                    return value; // precision is so high the value is unchanged
                var rounded = Math.Round(scaled, 15, MidpointRounding.AwayFromZero);
                return rounded / scaleFactor;
            }
        }
        var clampedPrecision = Math.Min(precision, 15);
        // XPath round semantics: round half toward positive infinity.
        // For negative values, this means -0.5 → -0, -1.5 → -1.
        // .NET's MidpointRounding.AwayFromZero rounds -0.5 → -1, so we need to correct.
        // Strategy: use ToPositiveInfinity for exact midpoints, AwayFromZero otherwise.
        // First try ToPositiveInfinity (rounds -0.5 → 0, but also rounds -0.6 → 0 which is wrong).
        // Instead: round with AwayFromZero, then check if we over-rounded.
        var result2 = Math.Round(value, clampedPrecision, MidpointRounding.AwayFromZero);
        if (value < 0 && result2 < value)
        {
            // AwayFromZero rounded more negative. Check if we're at exact midpoint
            // by verifying that the value scaled by 10^precision has fractional part = 0.5.
            var scale = Math.Pow(10, clampedPrecision);
            var scaled = value * scale;
            var frac = scaled - Math.Truncate(scaled);
            // Check if fractional part is exactly -0.5 (or very close due to FP)
            if (Math.Abs(Math.Abs(frac) - 0.5) < 1e-10)
            {
                // Use decimal arithmetic for the correction to avoid double precision artifacts
                // (e.g., -0.13 + 0.01 in double = -0.12000000000000001)
                var candidate = (double)((decimal)result2 + (decimal)Math.Pow(10, -clampedPrecision));
                // XPath: preserve negative zero when rounding negative values to zero
                if (candidate == 0.0 && double.IsNegative(value))
                    return -0.0;
                return candidate;
            }
        }
        return result2;
    }

    public static float Round(float value, int precision = 0)
        => (float)Round((double)value, precision);

    public static decimal Round(decimal value, int precision = 0)
    {
        if (value == 0m) return value;
        if (precision < 0)
        {
            var scale = DecimalPow10(-precision);
            var result = decimal.Round(value / scale, MidpointRounding.AwayFromZero) * scale;
            if (value < 0 && result < value)
            {
                var candidate = result + scale;
                if (value == (result + candidate) / 2m)
                    return candidate;
            }
            return result;
        }
        var clampedPrecision = Math.Min(precision, 28);
        var rounded = decimal.Round(value, clampedPrecision, MidpointRounding.AwayFromZero);
        if (value < 0 && rounded < value)
        {
            var unit = DecimalPow10Neg(clampedPrecision);
            var candidate = rounded + unit;
            if (value == (rounded + candidate) / 2m)
                return candidate;
        }
        return rounded;
    }

    private static decimal DecimalPow10(int n)
    {
        var result = 1m;
        for (int i = 0; i < n; i++) result *= 10m;
        return result;
    }

    private static decimal DecimalPow10Neg(int n)
    {
        var result = 1m;
        for (int i = 0; i < n; i++) result /= 10m;
        return result;
    }
}
