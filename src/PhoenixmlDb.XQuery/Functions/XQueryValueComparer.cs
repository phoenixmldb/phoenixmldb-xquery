using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// Equality comparer implementing XQuery value equality semantics.
/// Handles numeric type coercion (12 == 12.0), NaN handling, etc.
/// </summary>
internal sealed class XQueryValueComparer : IEqualityComparer<object?>
{
    public static readonly XQueryValueComparer Instance = new();

    public new bool Equals(object? x, object? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null) return x is null && y is null;

        // Unwrap XsTypedString to plain string for comparison
        if (x is Xdm.XsTypedString tsx) x = tsx.Value;
        if (y is Xdm.XsTypedString tsy) y = tsy.Value;

        // Unwrap derived-integer-typed values to their underlying long. The XSD-subtype tag
        // (xs:short, xs:positiveInteger, …) matters for instance-of/serialization, but for
        // value equality (distinct-values, index-of, group-by) a derived integer must compare
        // identically to a bare xs:integer of the same value. (cbcl-distinct-values-002b)
        if (x is Xdm.XsTypedInteger tix) x = tix.Value;
        if (y is Xdm.XsTypedInteger tiy) y = tiy.Value;

        // Handle numeric comparisons with type coercion
        // XQuery type promotion rules: decimal+decimal → decimal, float+float → float,
        // double+anything → double, float+decimal → float, float+integer → float,
        // decimal+integer → decimal, integer+integer → integer.
        if (IsNumericValue(x) && IsNumericValue(y))
        {
            return NumericEquals(x, y);
        }

        // xs:dateTime comparison — apply implicit timezone when one side has no timezone
        if (x is Xdm.XsDateTime dtx && y is Xdm.XsDateTime dty)
            return DateTimeEqualsWithImplicitTimezone(dtx, dty);

        // xs:time comparison
        if (x is Xdm.XsTime tx && y is Xdm.XsTime ty)
            return tx.CompareTo(ty) == 0;

        // xs:date comparison
        if (x is Xdm.XsDate datex && y is Xdm.XsDate datey)
            return datex.CompareTo(datey) == 0;

        // xs:gYear / xs:gYearMonth / xs:gMonth / xs:gMonthDay / xs:gDay comparison:
        // per F&O §3.5, values with no explicit timezone are treated as if they had
        // the implicit (system) timezone for equality purposes.
        if (x is Xdm.XsGYear gya && y is Xdm.XsGYear gyb)
            return GYearFamilyEquals(gya.Value, gyb.Value);
        if (x is Xdm.XsGYearMonth gyma && y is Xdm.XsGYearMonth gymb)
            return GYearFamilyEquals(gyma.Value, gymb.Value);
        if (x is Xdm.XsGMonth gma && y is Xdm.XsGMonth gmb)
            return GYearFamilyEquals(gma.Value, gmb.Value);
        if (x is Xdm.XsGMonthDay gmda && y is Xdm.XsGMonthDay gmdb)
            return GYearFamilyEquals(gmda.Value, gmdb.Value);
        if (x is Xdm.XsGDay gda && y is Xdm.XsGDay gdb)
            return GYearFamilyEquals(gda.Value, gdb.Value);

        // Duration cross-type comparison
        if (IsDuration(x) && IsDuration(y))
            return DurationEquals(x, y);

        // xs:hexBinary / xs:base64Binary comparison (XdmValue with byte[] payload)
        if (x is XdmValue vx && y is XdmValue vy
            && vx.Type == vy.Type
            && (vx.Type == XdmType.HexBinary || vx.Type == XdmType.Base64Binary))
        {
            return vx.RawValue is byte[] bx && vy.RawValue is byte[] by2
                && bx.AsSpan().SequenceEqual(by2);
        }

        // xs:untypedAtomic / string cross-type
        // Per F&O fn:distinct-values: xs:untypedAtomic is compared as xs:string.
        // xs:string and xs:untypedAtomic are therefore merged as the same distinct value.
        var sx = x is XsUntypedAtomic uax ? uax.Value : x as string;
        var sy = y is XsUntypedAtomic uay ? uay.Value : y as string;
        if (sx != null && sy != null) return sx == sy;

        // xs:anyURI: only equal to another xs:anyURI with the same value.
        // Per F&O §3.5.2, the eq operator is NOT defined between xs:anyURI and xs:string,
        // so distinct-values treats them as distinct values.
        if (x is XsAnyUri ax && y is XsAnyUri ay2) return ax.Value == ay2.Value;
        if (x is XsAnyUri || y is XsAnyUri) return false;

        // Fall back to default equality for non-numeric types
        return object.Equals(x, y);
    }

    public int GetHashCode(object? obj)
    {
        if (obj is null) return 0;

        // Unwrap derived-integer-typed values so they hash identically to the bare long
        // (consistent with Equals above).
        if (obj is Xdm.XsTypedInteger ti) obj = ti.Value;

        // Normalize numeric values for consistent hashing.
        // The hash must be consistent with NumericEquals: if NumericEquals(a,b) then
        // GetHashCode(a) == GetHashCode(b). NumericEquals has three promotion paths:
        //   double+anything → double, float+non-double → float, decimal+integer → decimal
        // Hashing via float (Convert.ToSingle) satisfies all paths:
        //   - int(1), float(1f), double(1.0), decimal(1m) all → 1.0f → same hash
        //   - float(INF) and double(INF) both → float.PositiveInfinity → same hash
        //   - decimal(1.2) and float(1.2) both → 1.2f → same hash
        // Two values with the same float hash but different actual values (e.g.,
        // double(1.0000001) vs double(1.00000011)) will collide in the hash bucket
        // but Equals distinguishes them — this is correct behavior.
        if (IsNumericValue(obj))
        {
            var fv = Convert.ToSingle(obj, System.Globalization.CultureInfo.InvariantCulture);
            if (float.IsNaN(fv)) return 0;
            return fv.GetHashCode();
        }

        // Normalize dateTime/time/date to UTC-based hash (apply implicit timezone for no-tz values)
        if (obj is Xdm.XsDateTime xdt)
        {
            var dto = xdt.HasTimezone ? xdt.Value : new DateTimeOffset(xdt.Value.DateTime, DateTimeOffset.Now.Offset);
            return dto.ToUniversalTime().GetHashCode();
        }
        if (obj is Xdm.XsTime xt) return xt.ToUtcTicks().GetHashCode();
        if (obj is Xdm.XsDate xd) return xd.ToUtcTicks().GetHashCode();

        // xs:gYear / gYearMonth / gMonth / gMonthDay / gDay — hash on the core (non-tz) portion
        // so that "2015Z" and "2015" land in the same bucket when implicit timezone would make
        // them equal. Equals() narrows within the bucket.
        if (obj is Xdm.XsGYear gyh) return HashCode.Combine(typeof(Xdm.XsGYear), GCoreOf(gyh.Value));
        if (obj is Xdm.XsGYearMonth gymh) return HashCode.Combine(typeof(Xdm.XsGYearMonth), GCoreOf(gymh.Value));
        if (obj is Xdm.XsGMonth gmh) return HashCode.Combine(typeof(Xdm.XsGMonth), GCoreOf(gmh.Value));
        if (obj is Xdm.XsGMonthDay gmdh) return HashCode.Combine(typeof(Xdm.XsGMonthDay), GCoreOf(gmdh.Value));
        if (obj is Xdm.XsGDay gdh) return HashCode.Combine(typeof(Xdm.XsGDay), GCoreOf(gdh.Value));

        // Normalize duration to total months + day-time ticks
        if (obj is Xdm.XsDuration dur) return HashCode.Combine(dur.TotalMonths, dur.DayTime.Ticks);
        if (obj is Xdm.YearMonthDuration ymd) return HashCode.Combine(ymd.TotalMonths, 0);
        if (obj is Xdm.DayTimeDuration dtd) return HashCode.Combine(0, dtd.ToTimeSpan().Ticks);
        if (obj is TimeSpan ts) return HashCode.Combine(0, ts.Ticks);

        // Normalize binary values to content-based hash
        if (obj is XdmValue v && (v.Type == XdmType.HexBinary || v.Type == XdmType.Base64Binary)
            && v.RawValue is byte[] bytes)
        {
            var hash = new HashCode();
            foreach (var b in bytes) hash.Add(b);
            return hash.ToHashCode();
        }

        // Normalize untypedAtomic to string hash (per F&O, compared as xs:string in distinct-values)
        if (obj is XsUntypedAtomic ua) return ua.Value.GetHashCode();
        // XsTypedString (xs:normalizedString, xs:token, etc.) should hash like plain string
        if (obj is Xdm.XsTypedString typedStr) return typedStr.Value.GetHashCode();
        // xs:anyURI is distinct from xs:string in distinct-values — use a distinct hash bucket
        if (obj is XsAnyUri uri) return HashCode.Combine(typeof(XsAnyUri), uri.Value);

        return obj.GetHashCode();
    }

    internal static bool IsNumericValue(object? obj, Ast.ExecutionContext? context = null)
    {
        return obj is byte or sbyte or short or ushort or int or uint or long or ulong
            or float or double or decimal;
    }

    private static bool IsDuration(object? obj, Ast.ExecutionContext? context = null)
        => obj is Xdm.XsDuration or Xdm.YearMonthDuration or Xdm.DayTimeDuration or TimeSpan;

    /// <summary>
    /// Compares two xs:gYear-family lexical values using value-equality semantics:
    /// when one has an explicit timezone and the other does not, the implicit
    /// (system) timezone is applied to the untimezoned value.
    /// </summary>
    internal static bool GYearFamilyEquals(string a, string b, Ast.ExecutionContext? context = null)
    {
        var (aCore, aTz, aHasTz) = ParseGValue(a);
        var (bCore, bTz, bHasTz) = ParseGValue(b);
        if (aCore != bCore) return false;
        if (aHasTz && bHasTz) return aTz == bTz;
        if (!aHasTz && !bHasTz) return true;
        var implicitTz = DateTimeOffset.Now.Offset;
        return (aHasTz ? aTz : implicitTz) == (bHasTz ? bTz : implicitTz);
    }

    /// <summary>Returns the core (non-timezone) portion of a gYear-family lexical value.</summary>
    internal static string GCoreOf(string s, Ast.ExecutionContext? context = null)
    {
        var (core, _, _) = ParseGValue(s);
        return core;
    }

    /// <summary>
    /// Parses a gYear-family lexical value, separating the date core from any trailing
    /// timezone suffix (Z or ±HH:MM). Assumes the input is already syntactically valid.
    /// </summary>
    private static (string core, TimeSpan tz, bool hasTz) ParseGValue(string s)
    {
        if (string.IsNullOrEmpty(s)) return (s, TimeSpan.Zero, false);
        if (s[^1] == 'Z') return (s[..^1], TimeSpan.Zero, true);
        // Check for ±HH:MM at the end (6 chars). Guard against a leading-year '-' being
        // misread as tz by requiring ':' at position length-3.
        if (s.Length >= 7 && s[^3] == ':' && (s[^6] == '+' || s[^6] == '-'))
        {
            int sign = s[^6] == '-' ? -1 : 1;
            int hh = (s[^5] - '0') * 10 + (s[^4] - '0');
            int mm = (s[^2] - '0') * 10 + (s[^1] - '0');
            return (s[..^6], new TimeSpan(sign * hh, sign * mm, 0), true);
        }
        return (s, TimeSpan.Zero, false);
    }

    private static bool DurationEquals(object a, object b, Ast.ExecutionContext? context = null)
    {
        // Convert both to (months, dayTimeTicks) and compare
        var (am, at) = GetDurationComponents(a);
        var (bm, bt) = GetDurationComponents(b);
        return am == bm && at == bt;
    }

    private static (int months, long ticks) GetDurationComponents(object dur) => dur switch
    {
        Xdm.XsDuration d => (d.TotalMonths, d.DayTime.Ticks),
        Xdm.YearMonthDuration ymd => (ymd.TotalMonths, 0),
        Xdm.DayTimeDuration dtd => (0, dtd.ToTimeSpan().Ticks),
        TimeSpan ts => (0, ts.Ticks),
        _ => (0, 0)
    };

    /// <summary>
    /// Compares two numeric values using XQuery type promotion rules:
    /// decimal+decimal → compare as decimal; double+anything → compare as double;
    /// float+{decimal,integer} → compare as float; decimal+integer → compare as decimal.
    /// </summary>
    internal static bool NumericEquals(object x, object y, Ast.ExecutionContext? context = null)
    {
        // NaN handling first
        bool xNaN = (x is double xd2 && double.IsNaN(xd2)) || (x is float xf2 && float.IsNaN(xf2));
        bool yNaN = (y is double yd2 && double.IsNaN(yd2)) || (y is float yf2 && float.IsNaN(yf2));
        if (xNaN && yNaN) return true;
        if (xNaN || yNaN) return false;

        // If either is double, promote both to double
        if (x is double || y is double)
        {
            var dx = Convert.ToDouble(x, System.Globalization.CultureInfo.InvariantCulture);
            var dy = Convert.ToDouble(y, System.Globalization.CultureInfo.InvariantCulture);
            return dx == dy;
        }

        // If either is float, promote the other to float (not double)
        if (x is float || y is float)
        {
            var fx = Convert.ToSingle(x, System.Globalization.CultureInfo.InvariantCulture);
            var fy = Convert.ToSingle(y, System.Globalization.CultureInfo.InvariantCulture);
            return fx == fy;
        }

        // If either is decimal, promote both to decimal (preserves full precision)
        if (x is decimal || y is decimal)
        {
            var mx = Convert.ToDecimal(x, System.Globalization.CultureInfo.InvariantCulture);
            var my = Convert.ToDecimal(y, System.Globalization.CultureInfo.InvariantCulture);
            return mx == my;
        }

        // Both are integer types — compare as long
        var lx = Convert.ToInt64(x, System.Globalization.CultureInfo.InvariantCulture);
        var ly = Convert.ToInt64(y, System.Globalization.CultureInfo.InvariantCulture);
        return lx == ly;
    }

    /// <summary>
    /// Compares two xs:dateTime values, applying the implicit timezone to any value
    /// that doesn't have an explicit timezone (per XPath F&amp;O §10.4).
    /// </summary>
    private static bool DateTimeEqualsWithImplicitTimezone(Xdm.XsDateTime a, Xdm.XsDateTime b, Ast.ExecutionContext? context = null)
    {
        // When both have timezones or both lack them, standard comparison works
        if (a.HasTimezone == b.HasTimezone)
            return a.CompareTo(b) == 0;

        // One has a timezone and one doesn't — apply the implicit (system) timezone
        var implicitTz = DateTimeOffset.Now.Offset;
        var aDto = a.HasTimezone ? a.Value : new DateTimeOffset(a.Value.DateTime, implicitTz);
        var bDto = b.HasTimezone ? b.Value : new DateTimeOffset(b.Value.DateTime, implicitTz);
        return aDto.ToUniversalTime() == bDto.ToUniversalTime();
    }
}
