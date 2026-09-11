using System.Numerics;
using System.Text;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.Xdm.Serialization;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Functions;
using PhoenixmlDb.XQuery.Optimizer;

namespace PhoenixmlDb.XQuery.Execution;

/// <summary>
/// XDM-aware key equality comparer for map keys.
/// Handles cross-type numeric equality (int/long/float/double/decimal),
/// NaN=NaN, INF=INF across float/double, and timezone-normalized time equality.
/// </summary>
internal sealed class XdmMapKeyComparer : IEqualityComparer<object>
{
    public static readonly XdmMapKeyComparer Instance = new();

    public new bool Equals(object? x, object? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x == null || y == null) return false;
        // Unwrap derived-integer-typed values to their underlying long so a map keyed by
        // e.g. xs:short(1) matches a lookup with bare 1 (op:same-key over numerics).
        if (x is Xdm.XsTypedInteger tix) x = tix.Value;
        if (y is Xdm.XsTypedInteger tiy) y = tiy.Value;
        if (x.Equals(y)) return true;

        // Cross-type numeric comparison — per op:same-key, two numerics are equal iff
        // they represent exactly the same mathematical value. Double-coercion is too
        // loose: xs:decimal("1.1") and xs:double("1.1") are NOT same-key because
        // xs:double("1.1") ≠ 11/10 exactly. (same-key-006, same-key-007)
        if (IsNumeric(x) && IsNumeric(y))
        {
            // Handle NaN: NaN equals NaN for map key purposes
            if (IsNaN(x) && IsNaN(y)) return true;
            // Handle Infinity
            if (IsPositiveInfinity(x) && IsPositiveInfinity(y)) return true;
            if (IsNegativeInfinity(x) && IsNegativeInfinity(y)) return true;
            // If either is NaN/Inf but not both, they're unequal
            if (IsNaN(x) || IsNaN(y) || IsPositiveInfinity(x) || IsPositiveInfinity(y)
                || IsNegativeInfinity(x) || IsNegativeInfinity(y))
                return false;
            return ExactNumericEquals(x, y);
        }

        // Date/time comparison for op:same-key semantics (per XPath 3.1 §17.1.1):
        //   use a fixed implicit timezone of UTC (Z) for values that lack a timezone,
        //   then compare as UTC instants. This differs from op:eq / distinct-values
        //   / group-by, which use the system's implicit timezone.
        // A value WITH a timezone and one WITHOUT are never the same key, however their
        // instants compare. op:same-key is deliberately finer than op:eq here, and
        // same-key-013 pins the contrast in one expression: over
        //     ($other, $without_tz, adjust-dateTime-to-timezone($without_tz, implicit-timezone()))
        // it asserts map:size eq 3 while distinct-values and group-by both yield fewer than 3.
        // The last two denote the same instant; as map keys they stay distinct.
        //
        // Comparing instants alone made them collide only when the implicit timezone was UTC,
        // because that is the one offset for which "reinterpret the wall clock as Z" is the
        // identity. So this passed on every developer machine outside UTC and failed on CI
        // (maps-010, "mix values with and without timezones" — W3C bug 28632).
        if (x is Xdm.XsTime tx && y is Xdm.XsTime ty)
            return tx.Timezone.HasValue == ty.Timezone.HasValue
                && SameKeyTimeUtcTicks(tx) == SameKeyTimeUtcTicks(ty);
        if (x is Xdm.XsDateTime dtx && y is Xdm.XsDateTime dty)
            return dtx.HasTimezone == dty.HasTimezone
                && SameKeyDateTimeUtcTicks(dtx) == SameKeyDateTimeUtcTicks(dty);
        if (x is Xdm.XsDate datex && y is Xdm.XsDate datey)
            return datex.Timezone.HasValue == datey.Timezone.HasValue
                && SameKeyDateUtcTicks(datex) == SameKeyDateUtcTicks(datey);

        // xs:gYear-family: map-key equality uses lexical (canonical) comparison without
        // applying implicit timezone — same rule as date/time above. The canonical lexical
        // form from our type constructors already normalizes the tz, so string equality
        // gives us the op:same-key semantics.
        if (x is Xdm.XsGYear gya && y is Xdm.XsGYear gyb) return gya.Value == gyb.Value;
        if (x is Xdm.XsGYearMonth gyma && y is Xdm.XsGYearMonth gymb) return gyma.Value == gymb.Value;
        if (x is Xdm.XsGMonth gma && y is Xdm.XsGMonth gmb) return gma.Value == gmb.Value;
        if (x is Xdm.XsGMonthDay gmda && y is Xdm.XsGMonthDay gmdb) return gmda.Value == gmdb.Value;
        if (x is Xdm.XsGDay gda && y is Xdm.XsGDay gdb) return gda.Value == gdb.Value;

        // xs:anyURI / xs:string cross-type
        var sx = x is Xdm.XsAnyUri ax ? ax.Value : x as string;
        var sy = y is Xdm.XsAnyUri ay ? ay.Value : y as string;
        if (sx != null && sy != null) return sx == sy;

        // xs:untypedAtomic / xs:string cross-type
        var ux = x is Xdm.XsUntypedAtomic uax ? uax.Value : x as string;
        var uy = y is Xdm.XsUntypedAtomic uay ? uay.Value : y as string;
        if (ux != null && uy != null) return ux == uy;

        // Duration cross-type
        if (IsDuration(x) && IsDuration(y))
        {
            var (xm, xt) = GetDurationComponents(x);
            var (ym, yt) = GetDurationComponents(y);
            return xm == ym && xt == yt;
        }

        // QName comparison
        if (x is QName qx && y is QName qy)
        {
            if (qx.LocalName != qy.LocalName) return false;
            var lUri = qx.ResolvedNamespace;
            var rUri = qy.ResolvedNamespace;
            if (lUri != null && rUri != null) return lUri == rUri;
            return qx.Namespace == qy.Namespace;
        }

        return false;
    }

    public int GetHashCode(object obj)
    {
        // Unwrap derived-integer-typed values so they hash like the bare long (consistent
        // with Equals — required for hash-bucket lookup of XsTypedInteger keys).
        if (obj is Xdm.XsTypedInteger ti) obj = ti.Value;
        if (IsNumeric(obj))
        {
            if (IsNaN(obj)) return int.MinValue; // All NaN values have the same hash
            if (IsPositiveInfinity(obj)) return int.MaxValue;
            if (IsNegativeInfinity(obj)) return int.MaxValue - 1;
            return ExactNumericHash(obj);
        }
        // Use UTC-with-Z-default hash for date/time types so that same-key values
        // (equal as UTC instants when no-tz defaults to Z) hash consistently.
        // Timezone presence is part of key identity (see Equals), so it must be part of the
        // hash too — otherwise two keys that Equals says are distinct could still collide in
        // the same bucket, which is merely slow, but two that Equals says are EQUAL must never
        // land in different buckets, which is a lookup that silently misses.
        if (obj is Xdm.XsTime xt)
            return HashCode.Combine(xt.Timezone.HasValue, SameKeyTimeUtcTicks(xt));
        if (obj is Xdm.XsDateTime xdt)
            return HashCode.Combine(xdt.HasTimezone, SameKeyDateTimeUtcTicks(xdt));
        if (obj is Xdm.XsDate xd)
            return HashCode.Combine(xd.Timezone.HasValue, SameKeyDateUtcTicks(xd));
        // xs:anyURI and xs:string must share hash codes for cross-type lookup
        if (obj is Xdm.XsAnyUri au)
            return au.Value.GetHashCode();
        // xs:untypedAtomic and xs:string must share hash codes
        if (obj is Xdm.XsUntypedAtomic ua)
            return ua.Value.GetHashCode();
        // Duration types must share hash codes for cross-type lookup
        if (IsDuration(obj))
        {
            var (m, t) = GetDurationComponents(obj);
            return HashCode.Combine(m, t);
        }
        // QName: hash by local name + namespace
        if (obj is QName q)
            return HashCode.Combine(q.LocalName, q.ResolvedNamespace ?? q.Namespace.ToString());
        return obj.GetHashCode();
    }

    /// <summary>
    /// A hash of the numeric's exact mathematical VALUE, whatever CLR type carries it.
    /// <see cref="Equals(object?, object?)"/> compares exact values across int, long,
    /// BigInteger, decimal, float and double, so anything weaker lets two keys it calls equal
    /// land in different buckets — a lookup that silently misses.
    /// </summary>
    /// <remarks>
    /// The previous hash went through <c>Convert.ToDouble</c>, which throws for BigInteger
    /// (it is not IConvertible), so the catch hashed a BigInteger by its own hash code:
    /// <c>xs:integer("10")</c>, which is a BigInteger when cast from text, missed a map keyed
    /// by the literal 10 (QT3 same-key-009). Integral decimals above 15 significant digits
    /// missed too, because <c>(decimal)double</c> rounds to 15 digits. Rule now: integral
    /// values hash as long, or as BigInteger beyond long's range; non-integral values hash as
    /// the double they exactly equal, if one exists, else as themselves (then only an equal
    /// decimal can match them).
    /// </remarks>
    private static int ExactNumericHash(object x)
    {
        switch (x)
        {
            case int i:
                return ((long)i).GetHashCode();
            case long l:
                return l.GetHashCode();
            case System.Numerics.BigInteger b:
                return b >= long.MinValue && b <= long.MaxValue ? ((long)b).GetHashCode() : b.GetHashCode();
            case decimal m:
                if (decimal.Truncate(m) == m)
                {
                    return m >= long.MinValue && m <= long.MaxValue
                        ? ((long)m).GetHashCode()
                        : ((System.Numerics.BigInteger)m).GetHashCode();
                }
                return TryExactDouble(m, out var exact) ? exact.GetHashCode() : m.GetHashCode();
            default:
                // Finite float or double (NaN and the infinities are hashed by the caller).
                var d = x is float f ? f : (double)x;
                if (Math.Floor(d) == d)
                {
                    // 2^63 is exactly representable, so this bound is exact; -0.0 hashes as 0.
                    return d >= -9223372036854775808.0 && d < 9223372036854775808.0
                        ? ((long)d).GetHashCode()
                        : new System.Numerics.BigInteger(d).GetHashCode();
                }
                return d.GetHashCode();
        }
    }

    /// <summary>
    /// The double exactly equal to a non-integral decimal, if there is one. Computed rather
    /// than cast: <c>(double)m</c> rounds, and a rounded value would hash an equal key apart.
    /// A decimal N/10^s equals a double only if 5^s divides N (the value is then K/2^s) and K,
    /// with its factors of two removed, fits the double's 53-bit significand.
    /// </summary>
    private static bool TryExactDouble(decimal m, out double value)
    {
        value = 0;
        var bits = decimal.GetBits(m);
        var scale = (bits[3] >> 16) & 0xff;
        var n = (new System.Numerics.BigInteger((uint)bits[2]) << 64)
              | (new System.Numerics.BigInteger((uint)bits[1]) << 32)
              | new System.Numerics.BigInteger((uint)bits[0]);
        var k = System.Numerics.BigInteger.DivRem(n, System.Numerics.BigInteger.Pow(5, scale), out var remainder);
        if (!remainder.IsZero || k.IsZero) return false;
        var exponent = -scale;
        while (k.IsEven)
        {
            k >>= 1;
            exponent++;
        }
        if (k.GetBitLength() > 53) return false;
        value = Math.ScaleB((double)k, exponent);
        if (m < 0) value = -value;
        return true;
    }

    private static bool IsNumeric(object x)
        => x is int or long or double or float or decimal or System.Numerics.BigInteger;

    /// <summary>
    /// Same-key UTC ticks for xs:time: ticks − offset, using Z (zero offset) when
    /// the value has no explicit timezone, per XPath 3.1 §17.1.1.
    /// </summary>
    private static long SameKeyTimeUtcTicks(Xdm.XsTime t)
        => t.Time.Ticks - (t.Timezone ?? TimeSpan.Zero).Ticks;

    /// <summary>
    /// Same-key UTC ticks for xs:date: midnight UTC instant using Z when no timezone is set.
    /// </summary>
    private static long SameKeyDateUtcTicks(Xdm.XsDate d)
    {
        if (d.ExtendedYear.HasValue)
        {
            // Extended year values fall outside DateTime range — approximate ordering.
            return d.EffectiveYear * 365L * TimeSpan.TicksPerDay
                + d.Date.Month * 31L * TimeSpan.TicksPerDay
                + d.Date.Day * TimeSpan.TicksPerDay
                - (d.Timezone ?? TimeSpan.Zero).Ticks;
        }
        var dt = d.Date.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(dt, d.Timezone ?? TimeSpan.Zero).UtcTicks;
    }

    /// <summary>
    /// Same-key UTC ticks for xs:dateTime: UTC instant using Z when no timezone is set.
    /// </summary>
    private static long SameKeyDateTimeUtcTicks(Xdm.XsDateTime dt)
    {
        if (dt.HasTimezone) return dt.Value.UtcTicks;
        // Re-interpret the local wall clock as UTC
        return new DateTimeOffset(dt.Value.DateTime, TimeSpan.Zero).UtcTicks;
    }

    /// <summary>
    /// Compares two numerics by their exact mathematical value per op:same-key semantics.
    /// Unlike the double-coerced equality used for fn:eq, this returns true only when the
    /// two values represent identical mathematical values. For example:
    ///   xs:decimal("1.1") and xs:double("1.1") → false (double(1.1) ≠ 11/10 exactly)
    ///   xs:integer(1) and xs:double(1.0)         → true
    ///   xs:decimal("1.0") and xs:integer(1)      → true
    /// </summary>
    private static bool ExactNumericEquals(object x, object y)
    {
        // Same CLR type: use direct equality
        if (x.GetType() == y.GetType())
            return x.Equals(y);

        // double vs float: both IEEE, promote float to double and compare as doubles
        if ((x is double or float) && (y is double or float))
        {
            var dx = Convert.ToDouble(x, System.Globalization.CultureInfo.InvariantCulture);
            var dy = Convert.ToDouble(y, System.Globalization.CultureInfo.InvariantCulture);
            return dx == dy;
        }

        // Pure integral types (int/long/BigInteger): compare via BigInteger
        if (IsIntegral(x) && IsIntegral(y))
            return ToBigInteger(x) == ToBigInteger(y);

        // Mixed integral and decimal: convert integral to decimal when in range
        if ((IsIntegral(x) && y is decimal dy2) || (x is decimal && IsIntegral(y)))
        {
            try
            {
                var xd = x is decimal xdec ? xdec : IntegralToDecimal(x);
                var yd = y is decimal ydec ? ydec : IntegralToDecimal(y);
                return xd == yd;
            }
            catch { return false; }
        }

        // Mixed with IEEE float/double: they're equal only if the float/double represents
        // an integer/decimal value EXACTLY. We check by round-tripping.
        if ((x is double or float) || (y is double or float))
        {
            var flt = (x is double or float) ? x : y;
            var other = (x is double or float) ? y : x;
            double fd = Convert.ToDouble(flt, System.Globalization.CultureInfo.InvariantCulture);
            if (double.IsNaN(fd) || double.IsInfinity(fd)) return false;

            if (IsIntegral(other))
            {
                // Integer vs double: equal iff double is finite and integer-valued and matches
                if (Math.Floor(fd) != fd) return false;
                try
                {
                    var otherBig = ToBigInteger(other);
                    var fltBig = new System.Numerics.BigInteger(fd);
                    return otherBig == fltBig;
                }
                catch { return false; }
            }
            if (other is decimal odec)
            {
                // Decimal vs double: compare EXACTLY by decomposing both to rational form.
                // The naive `(decimal)fd == odec` round-trip lies because .NET's cast rounds
                // the double's infinite binary fraction to 15 significant digits. For example,
                // (decimal)(double)1.1 yields exactly 1.1m even though the double is
                // 1.1000000000000000888... — they are NOT mathematically equal.
                return DoubleEqualsDecimalExactly(fd, odec);
            }
        }
        return false;
    }

    /// <summary>
    /// Exact mathematical equality between an IEEE 754 double and a .NET decimal.
    /// Decomposes each into a rational number (mantissa × 2^exp vs. unscaled × 10^-scale)
    /// then compares via BigInteger arithmetic — no lossy round-trip through either format.
    /// </summary>
    private static bool DoubleEqualsDecimalExactly(double d, decimal m)
    {
        if (double.IsNaN(d) || double.IsInfinity(d)) return false;
        if (d == 0.0) return m == 0m;
        if (m == 0m) return false;

        // Sign check
        bool dNeg = double.IsNegative(d);
        bool mNeg = m < 0m;
        if (dNeg != mNeg) return false;

        // Decompose double: d = sign × mantissa × 2^exp
        long bits = BitConverter.DoubleToInt64Bits(d);
        long mantissaBits = bits & 0x000fffffffffffffL;
        int rawExp = (int)((bits >> 52) & 0x7ffL);
        long dMantissaL;
        int dExp;
        if (rawExp == 0)
        {
            // Subnormal: no implicit leading 1-bit
            dMantissaL = mantissaBits;
            dExp = -1074;
        }
        else
        {
            dMantissaL = mantissaBits | 0x0010000000000000L;
            dExp = rawExp - 1075;
        }
        var dMantissa = new System.Numerics.BigInteger(dMantissaL);

        // Decompose decimal: |m| = unscaled / 10^scale (unscaled is 96-bit unsigned)
        int[] bitsDec = decimal.GetBits(m);
        int scale = (bitsDec[3] >> 16) & 0xff;
        var lo = (uint)bitsDec[0];
        var mid = (uint)bitsDec[1];
        var hi = (uint)bitsDec[2];
        var unscaled = (new System.Numerics.BigInteger(hi) << 64)
                     + (new System.Numerics.BigInteger(mid) << 32)
                     + new System.Numerics.BigInteger(lo);

        // Equality: dMantissa × 2^dExp = unscaled / 10^scale
        //        ⇔ dMantissa × 2^(dExp+scale) × 5^scale = unscaled
        // Avoid negative bit-shifts by moving the negative-power side to the other term.
        int shift = dExp + scale;
        var fivePow = System.Numerics.BigInteger.Pow(5, scale);
        System.Numerics.BigInteger lhs, rhs;
        if (shift >= 0)
        {
            lhs = dMantissa * (System.Numerics.BigInteger.One << shift) * fivePow;
            rhs = unscaled;
        }
        else
        {
            lhs = dMantissa * fivePow;
            rhs = unscaled * (System.Numerics.BigInteger.One << (-shift));
        }
        return lhs == rhs;
    }

    private static bool IsIntegral(object x)
        => x is int or long or System.Numerics.BigInteger;

    private static System.Numerics.BigInteger ToBigInteger(object x) => x switch
    {
        int i => new System.Numerics.BigInteger(i),
        long l => new System.Numerics.BigInteger(l),
        System.Numerics.BigInteger b => b,
        _ => throw new ArgumentException($"Not an integral: {x.GetType()}")
    };

    private static decimal IntegralToDecimal(object x) => x switch
    {
        int i => (decimal)i,
        long l => (decimal)l,
        System.Numerics.BigInteger b => (decimal)b,
        _ => throw new ArgumentException($"Not an integral: {x.GetType()}")
    };

    private static bool IsNaN(object x)
        => x is double d && double.IsNaN(d) || x is float f && float.IsNaN(f);

    private static bool IsPositiveInfinity(object x)
        => x is double d && double.IsPositiveInfinity(d) || x is float f && float.IsPositiveInfinity(f);

    private static bool IsNegativeInfinity(object x)
        => x is double d && double.IsNegativeInfinity(d) || x is float f && float.IsNegativeInfinity(f);

    private static bool IsDuration(object x) =>
        x is Xdm.XsDuration or Xdm.YearMonthDuration or TimeSpan or Xdm.DayTimeDuration;

    private static (int months, long ticks) GetDurationComponents(object dur) => dur switch
    {
        Xdm.XsDuration d => (d.TotalMonths, d.DayTime.Ticks),
        Xdm.YearMonthDuration ymd => (ymd.TotalMonths, 0),
        TimeSpan ts => (0, ts.Ticks),
        Xdm.DayTimeDuration dtd => (0, (long)(dtd.TotalSeconds * TimeSpan.TicksPerSecond)),
        _ => (0, 0)
    };
}
