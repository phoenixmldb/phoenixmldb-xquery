using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

internal static class SequenceArgValidator
{
    /// <summary>
    /// Validates that a value is numeric (or untypedAtomic/boolean which promote to double).
    /// Strings that are not valid xs:double values raise XPTY0004.
    /// </summary>
    internal static void RequireNumeric(object? value, string functionName, int paramPosition)
    {
        var atomized = QueryExecutionContext.AtomizeSingle(value);
        if (atomized is null or int or long or double or float or decimal
            or System.Numerics.BigInteger or bool or XsUntypedAtomic)
            return;
        if (atomized is string)
            throw new XQueryRuntimeException("XPTY0004",
                $"Required item type of {paramPosition}{(paramPosition == 2 ? "nd" : "rd")} argument of {functionName}() is xs:double; got xs:string");
    }
}

/// <summary>
/// fn:empty($arg) as xs:boolean
/// </summary>
public sealed class EmptyFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "empty");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg == null)
            return ValueTask.FromResult<object?>(true);

        if (arg is IEnumerable<object?> seq)
            return ValueTask.FromResult<object?>(!seq.Any());

        return ValueTask.FromResult<object?>(false);
    }
}

/// <summary>
/// fn:exists($arg) as xs:boolean
/// </summary>
public sealed class ExistsFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "exists");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg == null)
            return ValueTask.FromResult<object?>(false);

        if (arg is IEnumerable<object?> seq)
            return ValueTask.FromResult<object?>(seq.Any());

        return ValueTask.FromResult<object?>(true);
    }
}

/// <summary>
/// fn:head($arg) as item()?
/// </summary>
public sealed class HeadFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "head");
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalItem;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg == null)
            return ValueTask.FromResult<object?>(null);

        if (arg is IEnumerable<object?> seq)
            return ValueTask.FromResult<object?>(seq.FirstOrDefault());

        return ValueTask.FromResult<object?>(arg);
    }
}

/// <summary>
/// fn:tail($arg) as item()*
/// </summary>
public sealed class TailFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "tail");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg == null)
            return ValueTask.FromResult<object?>(Array.Empty<object>());

        if (arg is IEnumerable<object?> seq)
            return ValueTask.FromResult<object?>(seq.Skip(1));

        return ValueTask.FromResult<object?>(Array.Empty<object>());
    }
}

/// <summary>
/// fn:reverse($arg) as item()*
/// </summary>
public sealed class ReverseFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "reverse");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg == null)
            return ValueTask.FromResult<object?>(Array.Empty<object>());

        if (arg is IEnumerable<object?> seq)
            return ValueTask.FromResult<object?>(seq.Reverse().ToArray());

        return ValueTask.FromResult<object?>(new[] { arg });
    }
}

/// <summary>
/// fn:distinct-values($arg) as xs:anyAtomicType*
/// Atomizes the input sequence and returns distinct atomic values.
/// </summary>
public sealed class DistinctValuesFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "distinct-values");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg == null)
            return ValueTask.FromResult<object?>(Array.Empty<object>());

        var comparison = CollationHelper.GetDefaultComparison(context);
        IEqualityComparer<object?> comparer = comparison == StringComparison.Ordinal
            ? XQueryValueComparer.Instance
            : new CollationValueComparer(comparison);

        if (arg is IEnumerable<object?> seq)
        {
            // Atomize each item and then get distinct values using XQuery value equality
            var atomized = seq.Select(AtomizeItem).Distinct(comparer).ToArray();
            return ValueTask.FromResult<object?>(atomized);
        }

        return ValueTask.FromResult<object?>(new[] { AtomizeItem(arg) });
    }

    internal static object? AtomizeItem(object? item)
    {
        return item switch
        {
            null => null,
            XdmElement elem => elem.StringValue,
            XdmAttribute attr => attr.Value,
            XdmText text => text.Value,
            XdmComment comment => comment.Value,
            XdmProcessingInstruction pi => pi.Value,
            XdmDocument doc => doc.StringValue,
            IDictionary<object, object?> => throw new XQueryException("FOTY0013", "Atomization is not defined for maps"),
            List<object?> => throw new XQueryException("FOTY0013", "Atomization is not defined for arrays"),
            XQueryFunction => throw new XQueryException("FOTY0013", "Atomization is not defined for function items"),
            _ => item // Already atomic
        };
    }
}

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

    internal static bool IsNumericValue(object? obj)
    {
        return obj is byte or sbyte or short or ushort or int or uint or long or ulong
            or float or double or decimal;
    }

    private static bool IsDuration(object? obj)
        => obj is Xdm.XsDuration or Xdm.YearMonthDuration or Xdm.DayTimeDuration or TimeSpan;

    /// <summary>
    /// Compares two xs:gYear-family lexical values using value-equality semantics:
    /// when one has an explicit timezone and the other does not, the implicit
    /// (system) timezone is applied to the untimezoned value.
    /// </summary>
    internal static bool GYearFamilyEquals(string a, string b)
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
    internal static string GCoreOf(string s)
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

    private static bool DurationEquals(object a, object b)
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
    internal static bool NumericEquals(object x, object y)
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
    private static bool DateTimeEqualsWithImplicitTimezone(Xdm.XsDateTime a, Xdm.XsDateTime b)
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

/// <summary>
/// Value comparer that uses a collation for string comparisons.
/// </summary>
internal sealed class CollationValueComparer : IEqualityComparer<object?>
{
    private readonly StringComparison _comparison;

    public CollationValueComparer(StringComparison comparison) => _comparison = comparison;

    public new bool Equals(object? x, object? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null) return x is null && y is null;

        if (x is Xdm.XsTypedString tsx) x = tsx.Value;
        if (y is Xdm.XsTypedString tsy) y = tsy.Value;

        if (x is string sx && y is string sy)
            return string.Equals(sx, sy, _comparison);

        if (XQueryValueComparer.IsNumericValue(x) && XQueryValueComparer.IsNumericValue(y))
            return XQueryValueComparer.NumericEquals(x, y);

        return object.Equals(x, y);
    }

    public int GetHashCode(object? obj)
    {
        if (obj is null) return 0;
        if (obj is Xdm.XsTypedString ts2) obj = ts2.Value;
        if (obj is string s && _comparison is StringComparison.OrdinalIgnoreCase or StringComparison.InvariantCultureIgnoreCase)
            return StringComparer.OrdinalIgnoreCase.GetHashCode(s);
        if (XQueryValueComparer.IsNumericValue(obj))
        {
            if (obj is double d)
            {
                if (double.IsNaN(d)) return 0;
                return d.GetHashCode();
            }
            var fv = Convert.ToSingle(obj, System.Globalization.CultureInfo.InvariantCulture);
            if (float.IsNaN(fv)) return 0;
            return fv.GetHashCode();
        }
        return obj.GetHashCode();
    }
}

/// <summary>
/// fn:subsequence($sourceSeq, $startingLoc) as item()*
/// </summary>
public sealed class SubsequenceFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "subsequence");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "sourceSeq"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "startingLoc"), Type = XdmSequenceType.Double }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var source = arguments[0];
        // XPTY0004: $startingLoc is xs:double (required) — empty sequence is a type error
        if (arguments[1] is null)
            throw new XQueryRuntimeException("XPTY0004",
                "An empty sequence is not allowed as the 2nd argument of subsequence()");
        var startingLoc = QueryExecutionContext.ToDouble(arguments[1]);

        // Per XPath spec: if startingLoc is NaN, result is empty
        if (source == null || double.IsNaN(startingLoc))
            return ValueTask.FromResult<object?>(Array.Empty<object>());

        var seq = source is object?[] arr ? arr : source is IEnumerable<object?> s ? s.ToArray() : new[] { source };

        if (double.IsNegativeInfinity(startingLoc))
            return ValueTask.FromResult<object?>(seq);
        if (double.IsPositiveInfinity(startingLoc))
            return ValueTask.FromResult<object?>(Array.Empty<object>());

        // XPath uses 1-based indexing; round .5 towards positive infinity
        var startIndex = (int)Math.Round(startingLoc, MidpointRounding.AwayFromZero) - 1;
        if (startIndex < 0) startIndex = 0;
        if (startIndex >= seq.Length)
            return ValueTask.FromResult<object?>(Array.Empty<object>());

        if (startIndex == 0)
            return ValueTask.FromResult<object?>(seq);

        var result = new object?[seq.Length - startIndex];
        Array.Copy(seq, startIndex, result, 0, result.Length);
        return ValueTask.FromResult<object?>(result);
    }
}

/// <summary>
/// fn:subsequence($sourceSeq, $startingLoc, $length) as item()*
/// </summary>
public sealed class Subsequence3Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "subsequence");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "sourceSeq"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "startingLoc"), Type = XdmSequenceType.Double },
        new() { Name = new QName(NamespaceId.None, "length"), Type = XdmSequenceType.Double }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var source = arguments[0];
        // XPTY0004: $startingLoc and $length are xs:double (required) — empty sequence is a type error
        if (arguments[1] is null)
            throw new XQueryRuntimeException("XPTY0004",
                "An empty sequence is not allowed as the 2nd argument of subsequence()");
        if (arguments[2] is null)
            throw new XQueryRuntimeException("XPTY0004",
                "An empty sequence is not allowed as the 3rd argument of subsequence()");
        SequenceArgValidator.RequireNumeric(arguments[1], "subsequence", 2);
        SequenceArgValidator.RequireNumeric(arguments[2], "subsequence", 3);
        var startingLoc = QueryExecutionContext.ToDouble(arguments[1]);
        var length = QueryExecutionContext.ToDouble(arguments[2]);

        if (source == null)
            return ValueTask.FromResult<object?>(Array.Empty<object>());

        // Per XPath spec: if startingLoc or length is NaN, result is empty
        if (double.IsNaN(startingLoc) || double.IsNaN(length))
            return ValueTask.FromResult<object?>(Array.Empty<object>());

        var seq = source is object?[] arr ? arr : source is IEnumerable<object?> s ? s.ToArray() : new[] { source };

        // Per XPath spec: use double arithmetic for position/length to handle INF correctly
        // Items at position P (1-based) where round(startingLoc) <= P < round(startingLoc) + round(length)
        double roundedStart = Math.Round(startingLoc, MidpointRounding.AwayFromZero);
        double roundedLen = Math.Round(length, MidpointRounding.AwayFromZero);
        double endPos = roundedStart + roundedLen;

        // Convert to 0-based array indices, clamping to valid range
        int startIdx = roundedStart < 1 ? 0 : (roundedStart > seq.Length ? seq.Length : (int)roundedStart - 1);
        int endIdx = endPos < 1 ? 0 : (endPos > seq.Length + 1 ? seq.Length : (int)endPos - 1);

        if (startIdx >= endIdx)
            return ValueTask.FromResult<object?>(Array.Empty<object>());

        var count = endIdx - startIdx;
        var result = new object?[count];
        Array.Copy(seq, startIdx, result, 0, count);
        return ValueTask.FromResult<object?>(result);
    }
}

/// <summary>
/// fn:insert-before($target, $position, $inserts) as item()*
/// </summary>
public sealed class InsertBeforeFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "insert-before");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "target"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "position"), Type = XdmSequenceType.Integer },
        new() { Name = new QName(NamespaceId.None, "inserts"), Type = XdmSequenceType.ZeroOrMoreItems }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var target = arguments[0];
        var posArg = arguments[1];
        // XPTY0004: position must be xs:integer
        if (posArg == null || posArg is double or float or decimal or string or Xdm.XsAnyUri)
            throw new Execution.XQueryRuntimeException("XPTY0004",
                $"fn:insert-before: position must be xs:integer, got {posArg?.GetType().Name ?? "empty sequence"}");
        var position = QueryExecutionContext.ToInt(posArg);
        var inserts = arguments[2];

        var targetArr = target is object?[] ta ? ta : target is IEnumerable<object?> t ? t.ToArray() : target != null ? [target] : Array.Empty<object?>();
        var insertsArr = inserts is object?[] ia ? ia : inserts is IEnumerable<object?> i ? i.ToArray() : inserts != null ? [inserts] : Array.Empty<object?>();

        // XPath uses 1-based indexing
        var insertIndex = position - 1;
        if (insertIndex < 0) insertIndex = 0;
        if (insertIndex > targetArr.Length) insertIndex = targetArr.Length;

        var result = new object?[targetArr.Length + insertsArr.Length];
        Array.Copy(targetArr, 0, result, 0, insertIndex);
        Array.Copy(insertsArr, 0, result, insertIndex, insertsArr.Length);
        Array.Copy(targetArr, insertIndex, result, insertIndex + insertsArr.Length, targetArr.Length - insertIndex);
        return ValueTask.FromResult<object?>(result);
    }
}

/// <summary>
/// fn:remove($target, $position) as item()*
/// </summary>
public sealed class RemoveFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "remove");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "target"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "position"), Type = XdmSequenceType.Integer }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var target = arguments[0];
        var posArg = arguments[1];
        // XPTY0004: position must be xs:integer
        if (posArg is double or float or decimal or string or Xdm.XsAnyUri)
            throw new Execution.XQueryRuntimeException("XPTY0004",
                $"fn:remove: position must be xs:integer, got {posArg.GetType().Name}");
        var position = QueryExecutionContext.ToInt(posArg);

        if (target == null)
            return ValueTask.FromResult<object?>(Array.Empty<object>());

        var seq = target is object?[] arr ? arr : target is IEnumerable<object?> t ? t.ToArray() : [target];

        // XPath uses 1-based indexing
        var removeIndex = position - 1;
        if (removeIndex < 0 || removeIndex >= seq.Length)
            return ValueTask.FromResult<object?>(seq);

        var result = new object?[seq.Length - 1];
        Array.Copy(seq, 0, result, 0, removeIndex);
        Array.Copy(seq, removeIndex + 1, result, removeIndex, seq.Length - removeIndex - 1);
        return ValueTask.FromResult<object?>(result);
    }
}

/// <summary>
/// fn:index-of($seq, $search) as xs:integer*
/// </summary>
public sealed class IndexOfFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "index-of");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Integer, Occurrence = Occurrence.ZeroOrMore };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "search"), Type = XdmSequenceType.Item }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var seq = arguments[0];
        var search = QueryExecutionContext.Atomize(arguments[1]);
        var comparison = CollationHelper.GetDefaultComparison(context);

        // Per spec: $search must be a single atomic value
        if (search == null || (search is object?[] searchArr && searchArr.Length == 0))
            throw new XQueryException("XPTY0004", "fn:index-of() search value must be a single atomic value, got empty sequence");

        if (seq == null)
            return ValueTask.FromResult<object?>(Array.Empty<object>());

        var items = seq is IEnumerable<object?> s ? s : (IEnumerable<object?>)[seq];
        var result = new List<object>();
        var index = 0;

        foreach (var item in items)
        {
            index++;
            var atomized = QueryExecutionContext.Atomize(item);

            // NaN is never equal to anything (including NaN) per IEEE 754/XPath
            if (IsNaN(atomized) || IsNaN(search))
                continue;

            // String-like comparison: xs:string, xs:untypedAtomic, xs:anyURI all compare as strings
            var itemStr = atomized is string si ? si : atomized is Xdm.XsUntypedAtomic ua ? ua.Value : atomized is Xdm.XsAnyUri aau ? aau.Value : atomized is Xdm.XsTypedString tsi ? tsi.Value : null;
            var searchStr = search is string ss ? ss : search is Xdm.XsUntypedAtomic sua ? sua.Value : search is Xdm.XsAnyUri sau ? sau.Value : search is Xdm.XsTypedString tss ? tss.Value : null;

            if (itemStr != null && searchStr != null)
            {
                if (string.Equals(itemStr, searchStr, comparison))
                    result.Add((long)index);
            }
            else if (Equals(atomized, search))
                result.Add((long)index); // XPath uses 1-based indexing, xs:integer = long
        }

        return ValueTask.FromResult<object?>(result.ToArray());
    }

    private static bool IsNaN(object? value) =>
        (value is double d && double.IsNaN(d)) ||
        (value is float f && float.IsNaN(f));
}

/// <summary>
/// fn:deep-equal($arg1, $arg2) as xs:boolean
/// </summary>
public sealed class DeepEqualFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "deep-equal");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "parameter1"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "parameter2"), Type = XdmSequenceType.ZeroOrMoreItems }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var comparison = CollationHelper.GetDefaultComparison(context);
        return DeepEqualWithComparison(arguments[0], arguments[1], comparison, context.NodeStore);
    }

    internal static ValueTask<object?> DeepEqualWithComparison(object? arg1, object? arg2, StringComparison comparison,
        INodeStore? nodeStore = null)
    {
        using var enumA = ToEnumerable(arg1).GetEnumerator();
        using var enumB = ToEnumerable(arg2).GetEnumerator();

        while (true)
        {
            var hasA = enumA.MoveNext();
            var hasB = enumB.MoveNext();

            if (!hasA && !hasB)
                return ValueTask.FromResult<object?>(true);
            if (hasA != hasB)
                return ValueTask.FromResult<object?>(false);
            // FOTY0015: function items cannot be compared with deep-equal
            if (enumA.Current is Ast.XQueryFunction || enumB.Current is Ast.XQueryFunction)
                throw new XQueryException("FOTY0015", "fn:deep-equal cannot be applied to function items");
            if (!Execution.TypeCastHelper.DeepEquals(enumA.Current, enumB.Current, comparison, nodeStore))
                return ValueTask.FromResult<object?>(false);
        }
    }

    private static IEnumerable<object?> ToEnumerable(object? arg)
    {
        // XDM arrays (List<object?>) are single items — do not flatten into their members.
        // deep-equal([1], 1) must be false: an array is never equal to an atomic value.
        if (arg is List<object?>)
            return [arg];
        if (arg is IEnumerable<object?> seq)
            return seq;
        if (arg == null)
            return Enumerable.Empty<object?>();
        return [arg];
    }
}

/// <summary>
/// fn:deep-equal($arg1, $arg2, $collation) as xs:boolean
/// </summary>
public sealed class DeepEqual3Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "deep-equal");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "parameter1"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "parameter2"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "collation"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var comparison = CollationHelper.GetStringComparison(arguments[2]?.ToString());
        return DeepEqualFunction.DeepEqualWithComparison(arguments[0], arguments[1], comparison, context.NodeStore);
    }
}

/// <summary>
/// fn:index-of($seq, $search, $collation) as xs:integer*
/// </summary>
public sealed class IndexOf3Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "index-of");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Integer, Occurrence = Occurrence.ZeroOrMore };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "search"), Type = XdmSequenceType.Item },
        new() { Name = new QName(NamespaceId.None, "collation"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var seq = arguments[0];
        var search = QueryExecutionContext.Atomize(arguments[1]);
        var collationArg = arguments[2];

        // Per spec: $collation is xs:string (exactly one)
        if (collationArg == null || (collationArg is object?[] ca && ca.Length == 0))
            throw new XQueryException("XPTY0004", "fn:index-of() collation argument must be a string, got empty sequence");

        var comparison = CollationHelper.GetStringComparison(collationArg.ToString());

        // Per spec: $search must be a single atomic value
        if (search == null || (search is object?[] searchArr && searchArr.Length == 0))
            throw new XQueryException("XPTY0004", "fn:index-of() search value must be a single atomic value, got empty sequence");

        if (seq == null)
            return ValueTask.FromResult<object?>(Array.Empty<object>());

        var items = seq is IEnumerable<object?> s ? s : (IEnumerable<object?>)[seq];
        var result = new List<object>();
        var index = 0;

        foreach (var item in items)
        {
            index++;
            var atomized = QueryExecutionContext.Atomize(item);

            // String-like comparison: xs:string, xs:untypedAtomic, xs:anyURI all compare as strings
            var itemStr = atomized is string si ? si : atomized is Xdm.XsUntypedAtomic ua ? ua.Value : atomized is Xdm.XsAnyUri aau ? aau.Value : atomized is Xdm.XsTypedString tsi ? tsi.Value : null;
            var searchStr = search is string ss ? ss : search is Xdm.XsUntypedAtomic sua ? sua.Value : search is Xdm.XsAnyUri sau ? sau.Value : search is Xdm.XsTypedString tss ? tss.Value : null;

            if (itemStr != null && searchStr != null)
            {
                if (string.Equals(itemStr, searchStr, comparison))
                    result.Add((long)index);
            }
            else if (Equals(atomized, search))
                result.Add((long)index);
        }

        return ValueTask.FromResult<object?>(result.ToArray());
    }
}

/// <summary>
/// fn:distinct-values($arg, $collation) as xs:anyAtomicType*
/// </summary>
public sealed class DistinctValues2Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "distinct-values");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "collation"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg == null)
            return ValueTask.FromResult<object?>(Array.Empty<object>());

        var comparison = CollationHelper.GetStringComparison(arguments[1]?.ToString());

        if (arg is IEnumerable<object?> seq)
        {
            var comparer = new CollationValueComparer(comparison);
            var atomized = seq.Select(DistinctValuesFunction.AtomizeItem).Distinct(comparer).ToArray();
            return ValueTask.FromResult<object?>(atomized);
        }

        return ValueTask.FromResult<object?>(new[] { DistinctValuesFunction.AtomizeItem(arg) });
    }
}

/// <summary>
/// fn:zero-or-one($arg) as item()?
/// </summary>
public sealed class ZeroOrOneFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "zero-or-one");
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalItem;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is IEnumerable<object?> seq)
        {
            using var enumerator = seq.GetEnumerator();
            if (!enumerator.MoveNext())
                return ValueTask.FromResult<object?>(null);
            var first = enumerator.Current;
            if (enumerator.MoveNext())
                throw new Execution.XQueryRuntimeException("FORG0003",
                    "fn:zero-or-one called with a sequence of more than one item");
            return ValueTask.FromResult<object?>(first);
        }
        return ValueTask.FromResult<object?>(arg);
    }
}

/// <summary>
/// fn:one-or-more($arg) as item()+
/// </summary>
public sealed class OneOrMoreFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "one-or-more");
    public override XdmSequenceType ReturnType => XdmSequenceType.OneOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg == null)
            throw new Execution.XQueryRuntimeException("FORG0004",
                "fn:one-or-more called with empty sequence");
        if (arg is IEnumerable<object?> seq)
        {
            using var enumerator = seq.GetEnumerator();
            if (!enumerator.MoveNext())
                throw new Execution.XQueryRuntimeException("FORG0004",
                    "fn:one-or-more called with empty sequence");
            // Safe to return the original arg — FunctionCallOperator already materializes arguments
            return ValueTask.FromResult<object?>(arg);
        }
        return ValueTask.FromResult<object?>(arg);
    }
}

/// <summary>
/// fn:exactly-one($arg) as item()
/// </summary>
public sealed class ExactlyOneFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "exactly-one");
    public override XdmSequenceType ReturnType => XdmSequenceType.Item;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is IEnumerable<object?> seq)
        {
            using var enumerator = seq.GetEnumerator();
            if (!enumerator.MoveNext())
                throw new Execution.XQueryRuntimeException("FORG0005",
                    "fn:exactly-one called with a sequence of 0 items");
            var first = enumerator.Current;
            if (enumerator.MoveNext())
                throw new Execution.XQueryRuntimeException("FORG0005",
                    "fn:exactly-one called with a sequence of more than one item");
            return ValueTask.FromResult(first);
        }
        if (arg == null)
        {
            throw new Execution.XQueryRuntimeException("FORG0005",
                "fn:exactly-one called with empty sequence");
        }
        return ValueTask.FromResult<object?>(arg);
    }
}

/// <summary>
/// fn:unordered($arg) as item()*
/// </summary>
public sealed class UnorderedFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "unordered");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        // fn:unordered is an optimization hint — just pass through
        return ValueTask.FromResult(arguments[0]);
    }
}

/// <summary>
/// fn:round-half-to-even($arg) as numeric?
/// </summary>
public sealed class RoundHalfToEvenFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "round-half-to-even");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        return RoundHalfToEven2Function.RoundHalfToEvenImpl(arguments[0], 0);
    }
}

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

    internal static ValueTask<object?> RoundHalfToEvenImpl(object? arg, int precision)
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

    private static decimal RoundDecimal(decimal val, int precision)
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

    private static long RoundIntegerNeg(long val, int precision)
    {
        var scale = (long)Math.Pow(10, -precision);
        var half = scale / 2;
        var remainder = val % scale;
        var truncated = val - remainder;
        if (Math.Abs(remainder) > half) return truncated + (val > 0 ? scale : -scale);
        if (Math.Abs(remainder) == half) return truncated % (2 * scale) == 0 ? truncated : truncated + (val > 0 ? scale : -scale);
        return truncated;
    }

    private static double RoundDouble(double val, int precision)
    {
        if (double.IsNaN(val) || double.IsInfinity(val) || val == 0.0) return val;
        if (precision < 0)
        {
            var scale = Math.Pow(10, -precision);
            return Math.Round(val / scale, MidpointRounding.ToEven) * scale;
        }
        if (precision > 15)
        {
            int shift = precision - 15;
            var scaleFactor = Math.Pow(10, shift);
            var scaled = val * scaleFactor;
            if (double.IsInfinity(scaled)) return val;
            return Math.Round(scaled, 15, MidpointRounding.ToEven) / scaleFactor;
        }
        return Math.Round(val, precision, MidpointRounding.ToEven);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// XPath/XQuery 4.0 new functions
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// fn:identity($arg as item()*) as item()* — returns the argument unchanged (XPath 4.0).
/// </summary>
public sealed class IdentityFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "identity");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
        => ValueTask.FromResult(arguments[0]);
}

/// <summary>
/// fn:replicate($seq as item()*, $count as xs:integer) as item()* — repeats a sequence (XPath 4.0).
/// </summary>
public sealed class ReplicateFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "replicate");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "count"), Type = XdmSequenceType.Integer }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var seq = arguments[0];
        var count = Convert.ToInt32(arguments[1]);
        if (count <= 0 || seq == null) return ValueTask.FromResult<object?>(Array.Empty<object>());

        var items = seq is object?[] arr ? arr : new[] { seq };
        if (count == 1) return ValueTask.FromResult<object?>(seq);

        var result = new List<object?>(items.Length * count);
        for (var i = 0; i < count; i++)
            result.AddRange(items);
        return ValueTask.FromResult<object?>(result.ToArray());
    }
}

/// <summary>
/// fn:foot($seq as item()*) as item()? — returns the last item (XPath 4.0).
/// </summary>
public sealed class FootFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "foot");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg == null) return ValueTask.FromResult<object?>(null);
        if (arg is object?[] arr) return ValueTask.FromResult<object?>(arr.Length > 0 ? arr[^1] : null);
        if (arg is IEnumerable<object?> seq) return ValueTask.FromResult<object?>(seq.LastOrDefault());
        return ValueTask.FromResult<object?>(arg); // single item
    }
}

/// <summary>
/// fn:trunk($seq as item()*) as item()* — all items except the last (XPath 4.0).
/// </summary>
public sealed class TrunkFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "trunk");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg == null) return ValueTask.FromResult<object?>(Array.Empty<object>());
        if (arg is object?[] arr) return ValueTask.FromResult<object?>(arr.Length > 1 ? arr[..^1] : Array.Empty<object?>());
        if (arg is IEnumerable<object?> seq)
        {
            var list = seq.ToList();
            return ValueTask.FromResult<object?>(list.Count > 1 ? list.GetRange(0, list.Count - 1).ToArray() : Array.Empty<object?>());
        }
        return ValueTask.FromResult<object?>(Array.Empty<object>()); // single item → empty
    }
}

/// <summary>
/// fn:void($arg as item()*) as empty-sequence() — discards input, returns empty (XPath 4.0).
/// </summary>
public sealed class VoidFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "void");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
        => ValueTask.FromResult<object?>(null);
}

/// <summary>
/// fn:is-NaN($arg as xs:anyAtomicType) as xs:boolean — tests for NaN (XPath 4.0).
/// </summary>
public sealed class IsNaNFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "is-NaN");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.Item }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var val = QueryExecutionContext.Atomize(arguments[0]);
        var isNaN = val is double d && double.IsNaN(d)
            || val is float f && float.IsNaN(f);
        return ValueTask.FromResult<object?>(isNaN);
    }
}

/// <summary>
/// fn:characters($arg as xs:string?) as xs:string* — splits string into characters (XPath 4.0).
/// </summary>
public sealed class CharactersFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "characters");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.String, Occurrence = Occurrence.ZeroOrMore };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalString }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var str = arguments[0]?.ToString();
        if (string.IsNullOrEmpty(str)) return ValueTask.FromResult<object?>(Array.Empty<string>());
        // Use StringInfo to handle surrogate pairs correctly
        var chars = new List<string>();
        var enumerator = System.Globalization.StringInfo.GetTextElementEnumerator(str);
        while (enumerator.MoveNext())
            chars.Add(enumerator.GetTextElement());
        return ValueTask.FromResult<object?>(chars.ToArray());
    }
}

/// <summary>
/// fn:items-at($seq as item()*, $positions as xs:integer*) as item()* — items at positions (XPath 4.0).
/// </summary>
public sealed class ItemsAtFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "items-at");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "positions"), Type = new() { ItemType = ItemType.Integer, Occurrence = Occurrence.ZeroOrMore } }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var seq = arguments[0];
        var positions = arguments[1];
        if (seq == null || positions == null) return ValueTask.FromResult<object?>(Array.Empty<object>());

        var items = seq is object?[] arr ? arr : new[] { seq };
        var posArray = positions is object?[] pa ? pa : new[] { positions };

        var result = new List<object?>();
        foreach (var pos in posArray)
        {
            var idx = Convert.ToInt32(pos) - 1; // 1-based → 0-based
            if (idx >= 0 && idx < items.Length)
                result.Add(items[idx]);
        }
        return ValueTask.FromResult<object?>(result.Count == 1 ? result[0] : result.ToArray());
    }
}

/// <summary>
/// fn:slice($seq as item()*, $start as xs:integer?, $end as xs:integer?,
///          $step as xs:integer?) as item()* — flexible subsequence (XPath 4.0).
/// </summary>
public sealed class SliceFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "slice");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "start"), Type = new() { ItemType = ItemType.Integer, Occurrence = Occurrence.ZeroOrOne } },
        new() { Name = new QName(NamespaceId.None, "end"), Type = new() { ItemType = ItemType.Integer, Occurrence = Occurrence.ZeroOrOne } },
        new() { Name = new QName(NamespaceId.None, "step"), Type = new() { ItemType = ItemType.Integer, Occurrence = Occurrence.ZeroOrOne } }
    ];
    public override bool IsVariadic => true;
    public override int MaxArity => 4;

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var seq = arguments[0];
        if (seq == null) return ValueTask.FromResult<object?>(Array.Empty<object>());
        var items = seq is object?[] arr ? arr : new[] { seq };
        var len = items.Length;

        var start = arguments.Count > 1 && arguments[1] != null ? Convert.ToInt32(arguments[1]) : 1;
        var end = arguments.Count > 2 && arguments[2] != null ? Convert.ToInt32(arguments[2]) : len;
        var step = arguments.Count > 3 && arguments[3] != null ? Convert.ToInt32(arguments[3]) : 1;

        // Handle negative indices (Python-style: -1 = last)
        if (start < 0) start = len + start + 1;
        if (end < 0) end = len + end + 1;
        if (step == 0) return ValueTask.FromResult<object?>(Array.Empty<object>());

        var result = new List<object?>();
        if (step > 0)
        {
            for (var i = Math.Max(1, start); i <= Math.Min(len, end); i += step)
                result.Add(items[i - 1]);
        }
        else
        {
            for (var i = Math.Min(len, start); i >= Math.Max(1, end); i += step)
                result.Add(items[i - 1]);
        }
        return ValueTask.FromResult<object?>(result.Count == 1 ? result[0] : result.ToArray());
    }
}

/// <summary>
/// fn:all-equal($seq as xs:anyAtomicType*) as xs:boolean — tests if all items are equal (XPath 4.0).
/// </summary>
public sealed class AllEqualFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "all-equal");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var seq = arguments[0];
        if (seq == null) return ValueTask.FromResult<object?>(true);
        var items = seq is object?[] arr ? arr : new[] { seq };
        if (items.Length <= 1) return ValueTask.FromResult<object?>(true);

        var first = QueryExecutionContext.Atomize(items[0]);
        for (var i = 1; i < items.Length; i++)
        {
            var item = QueryExecutionContext.Atomize(items[i]);
            if (!Equals(first?.ToString(), item?.ToString()))
                return ValueTask.FromResult<object?>(false);
        }
        return ValueTask.FromResult<object?>(true);
    }
}

/// <summary>
/// fn:all-different($seq as xs:anyAtomicType*) as xs:boolean — all items distinct (XPath 4.0).
/// </summary>
public sealed class AllDifferentFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "all-different");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var seq = arguments[0];
        if (seq == null) return ValueTask.FromResult<object?>(true);
        var items = seq is object?[] arr ? arr : new[] { seq };

        var seen = new HashSet<string>();
        foreach (var item in items)
        {
            var val = QueryExecutionContext.Atomize(item)?.ToString() ?? "";
            if (!seen.Add(val)) return ValueTask.FromResult<object?>(false);
        }
        return ValueTask.FromResult<object?>(true);
    }
}

/// <summary>
/// fn:index-where($seq as item()*, $pred as function(item()) as xs:boolean) as xs:integer*
/// Returns positions of items matching a predicate (XPath 4.0).
/// </summary>
public sealed class IndexWhereFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "index-where");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Integer, Occurrence = Occurrence.ZeroOrMore };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "pred"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var seq = arguments[0];
        var pred = arguments[1] as XQueryFunction;
        if (seq == null || pred == null) return Array.Empty<object>();

        var items = seq is object?[] arr ? arr : new[] { seq };
        var result = new List<object?>();

        for (var i = 0; i < items.Length; i++)
        {
            var match = await pred.InvokeAsync([items[i]], context).ConfigureAwait(false);
            if (match is true || (match is not false && match != null))
                result.Add((long)(i + 1));
        }
        return result.Count == 1 ? result[0] : result.ToArray();
    }
}

/// <summary>
/// fn:scan-left($seq as item()*, $zero as item()*, $f as function(item()*, item()) as item()*)
/// Cumulative left reduction — returns all intermediate results (XPath 4.0).
/// </summary>
public sealed class ScanLeftFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "scan-left");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "zero"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "f"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var seq = arguments[0];
        var accumulator = arguments[1];
        var fn = arguments[2] as XQueryFunction;
        if (fn == null) return Array.Empty<object>();

        var items = seq is object?[] arr ? arr : (seq != null ? new[] { seq } : Array.Empty<object?>());
        var results = new List<object?> { accumulator };

        foreach (var item in items)
        {
            accumulator = await fn.InvokeAsync([accumulator, item], context).ConfigureAwait(false);
            results.Add(accumulator);
        }
        return results.ToArray();
    }
}

/// <summary>
/// fn:scan-right($seq as item()*, $zero as item()*, $f as function(item(), item()*) as item()*)
/// Cumulative right reduction — returns all intermediate results (XPath 4.0).
/// </summary>
public sealed class ScanRightFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "scan-right");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "zero"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "f"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var seq = arguments[0];
        var accumulator = arguments[1];
        var fn = arguments[2] as XQueryFunction;
        if (fn == null) return Array.Empty<object>();

        var items = seq is object?[] arr ? arr : (seq != null ? new[] { seq } : Array.Empty<object?>());
        var results = new List<object?>();

        // Process from right to left
        for (var i = items.Length - 1; i >= 0; i--)
        {
            accumulator = await fn.InvokeAsync([items[i], accumulator], context).ConfigureAwait(false);
            results.Insert(0, accumulator);
        }
        results.Add(arguments[1]); // Add initial value at the end (rightmost)
        return results.ToArray();
    }
}

/// <summary>
/// fn:duplicate-values($seq as xs:anyAtomicType*) as xs:anyAtomicType*
/// Returns values that appear more than once (XPath 4.0).
/// </summary>
public sealed class DuplicateValuesFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "duplicate-values");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var seq = arguments[0];
        if (seq == null) return ValueTask.FromResult<object?>(Array.Empty<object>());
        var items = seq is object?[] arr ? arr : new[] { seq };

        var seen = new HashSet<string>();
        var duplicates = new HashSet<string>();
        var result = new List<object?>();

        foreach (var item in items)
        {
            var val = QueryExecutionContext.Atomize(item)?.ToString() ?? "";
            if (!seen.Add(val) && duplicates.Add(val))
                result.Add(item);
        }
        return ValueTask.FromResult<object?>(result.Count == 1 ? result[0] : result.ToArray());
    }
}

/// <summary>
/// fn:atomic-equal($a as xs:anyAtomicType, $b as xs:anyAtomicType) as xs:boolean
/// Tests atomic value equality without type promotion (XPath 4.0).
/// </summary>
public sealed class AtomicEqualFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "atomic-equal");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "a"), Type = XdmSequenceType.Item },
        new() { Name = new QName(NamespaceId.None, "b"), Type = XdmSequenceType.Item }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var a = QueryExecutionContext.Atomize(arguments[0]);
        var b = QueryExecutionContext.Atomize(arguments[1]);
        if (a == null && b == null) return ValueTask.FromResult<object?>(true);
        if (a == null || b == null) return ValueTask.FromResult<object?>(false);
        // Strict equality: same type and same value
        if (a.GetType() != b.GetType()) return ValueTask.FromResult<object?>(false);
        return ValueTask.FromResult<object?>(Equals(a, b));
    }
}

/// <summary>
/// fn:contains-subsequence($seq as item()*, $subseq as item()*) as xs:boolean
/// Tests if a sequence contains another as a contiguous subsequence (XPath 4.0).
/// </summary>
public sealed class ContainsSubsequenceFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "contains-subsequence");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "subseq"), Type = XdmSequenceType.ZeroOrMoreItems }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var seq = arguments[0] is object?[] a1 ? a1 : (arguments[0] != null ? new[] { arguments[0] } : Array.Empty<object?>());
        var sub = arguments[1] is object?[] a2 ? a2 : (arguments[1] != null ? new[] { arguments[1] } : Array.Empty<object?>());

        if (sub.Length == 0) return ValueTask.FromResult<object?>(true);
        if (sub.Length > seq.Length) return ValueTask.FromResult<object?>(false);

        for (var i = 0; i <= seq.Length - sub.Length; i++)
        {
            var match = true;
            for (var j = 0; j < sub.Length; j++)
            {
                if (!Equals(QueryExecutionContext.Atomize(seq[i + j])?.ToString(),
                            QueryExecutionContext.Atomize(sub[j])?.ToString()))
                { match = false; break; }
            }
            if (match) return ValueTask.FromResult<object?>(true);
        }
        return ValueTask.FromResult<object?>(false);
    }
}

/// <summary>
/// fn:starts-with-subsequence($seq as item()*, $subseq as item()*) as xs:boolean (XPath 4.0).
/// </summary>
public sealed class StartsWithSubsequenceFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "starts-with-subsequence");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "subseq"), Type = XdmSequenceType.ZeroOrMoreItems }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var seq = arguments[0] is object?[] a1 ? a1 : (arguments[0] != null ? new[] { arguments[0] } : Array.Empty<object?>());
        var sub = arguments[1] is object?[] a2 ? a2 : (arguments[1] != null ? new[] { arguments[1] } : Array.Empty<object?>());

        if (sub.Length == 0) return ValueTask.FromResult<object?>(true);
        if (sub.Length > seq.Length) return ValueTask.FromResult<object?>(false);

        for (var j = 0; j < sub.Length; j++)
        {
            if (!Equals(QueryExecutionContext.Atomize(seq[j])?.ToString(),
                        QueryExecutionContext.Atomize(sub[j])?.ToString()))
                return ValueTask.FromResult<object?>(false);
        }
        return ValueTask.FromResult<object?>(true);
    }
}

/// <summary>
/// fn:insert-separator($seq as item()*, $sep as item()*) as item()* — inserts separator between items (XPath 4.0).
/// </summary>
public sealed class InsertSeparatorFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "insert-separator");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "sep"), Type = XdmSequenceType.ZeroOrMoreItems }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var seq = arguments[0];
        var sep = arguments[1];
        if (seq == null) return ValueTask.FromResult<object?>(Array.Empty<object>());
        var items = seq is object?[] arr ? arr : new[] { seq };
        if (items.Length <= 1) return ValueTask.FromResult<object?>(seq);

        var result = new List<object?>();
        for (var i = 0; i < items.Length; i++)
        {
            if (i > 0)
            {
                if (sep is object?[] sepArr) result.AddRange(sepArr);
                else if (sep != null) result.Add(sep);
            }
            result.Add(items[i]);
        }
        return ValueTask.FromResult<object?>(result.ToArray());
    }
}

/// <summary>
/// fn:highest($seq as item()*, $key as function(item()) as xs:anyAtomicType?) as item()*
/// Returns items with the highest key value (XPath 4.0).
/// </summary>
public sealed class HighestFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "highest");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "key"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ZeroOrOne } }
    ];
    public override bool IsVariadic => true;
    public override int MaxArity => 2;

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var seq = arguments[0];
        if (seq == null) return Array.Empty<object>();
        var items = seq is object?[] arr ? arr : new[] { seq };
        var keyFn = arguments.Count > 1 ? arguments[1] as XQueryFunction : null;

        if (items.Length == 0) return Array.Empty<object>();

        // Compute keys
        var keys = new List<(object? Item, double Key)>();
        foreach (var item in items)
        {
            var key = keyFn != null
                ? await keyFn.InvokeAsync([item], context).ConfigureAwait(false)
                : item;
            keys.Add((item, Convert.ToDouble(QueryExecutionContext.Atomize(key))));
        }

        var maxKey = keys.Max(k => k.Key);
        var result = keys.Where(k => k.Key == maxKey).Select(k => k.Item).ToList();
        return result.Count == 1 ? result[0] : result.ToArray();
    }
}

/// <summary>
/// fn:lowest($seq as item()*, $key as function(item()) as xs:anyAtomicType?) as item()*
/// Returns items with the lowest key value (XPath 4.0).
/// </summary>
public sealed class LowestFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "lowest");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "key"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ZeroOrOne } }
    ];
    public override bool IsVariadic => true;
    public override int MaxArity => 2;

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var seq = arguments[0];
        if (seq == null) return Array.Empty<object>();
        var items = seq is object?[] arr ? arr : new[] { seq };
        var keyFn = arguments.Count > 1 ? arguments[1] as XQueryFunction : null;

        if (items.Length == 0) return Array.Empty<object>();

        var keys = new List<(object? Item, double Key)>();
        foreach (var item in items)
        {
            var key = keyFn != null
                ? await keyFn.InvokeAsync([item], context).ConfigureAwait(false)
                : item;
            keys.Add((item, Convert.ToDouble(QueryExecutionContext.Atomize(key))));
        }

        var minKey = keys.Min(k => k.Key);
        var result = keys.Where(k => k.Key == minKey).Select(k => k.Item).ToList();
        return result.Count == 1 ? result[0] : result.ToArray();
    }
}

/// <summary>
/// fn:sort-with($seq as item()*, $comparator as function(item(), item()) as xs:integer) as item()*
/// Sorts a sequence using a custom comparator (XPath 4.0).
/// </summary>
public sealed class SortWithFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "sort-with");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "comparator"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var seq = arguments[0];
        var cmp = arguments[1] as XQueryFunction;
        if (seq == null || cmp == null) return Array.Empty<object>();
        var items = seq is object?[] arr ? arr.ToList() : new List<object?> { seq };

        // Insertion sort with async comparator
        for (var i = 1; i < items.Count; i++)
        {
            var key = items[i];
            var j = i - 1;
            while (j >= 0)
            {
                var cmpResult = await cmp.InvokeAsync([items[j], key], context).ConfigureAwait(false);
                if (Convert.ToInt32(cmpResult) <= 0) break;
                items[j + 1] = items[j];
                j--;
            }
            items[j + 1] = key;
        }
        return items.Count == 1 ? items[0] : items.ToArray();
    }
}

/// <summary>
/// fn:transitive-closure($seq as item()*, $fn as function(item()) as item()*)  as item()*
/// Computes the transitive closure of a function over a sequence (XPath 4.0).
/// </summary>
public sealed class TransitiveClosureFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "transitive-closure");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "fn"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var seq = arguments[0];
        var fn = arguments[1] as XQueryFunction;
        if (seq == null || fn == null) return Array.Empty<object>();

        var items = seq is object?[] arr ? arr.ToList() : new List<object?> { seq };
        var result = new List<object?>(items);
        var seen = new HashSet<string>(items.Select(i => QueryExecutionContext.Atomize(i)?.ToString() ?? ""));
        var queue = new Queue<object?>(items);

        while (queue.Count > 0)
        {
            var item = queue.Dequeue();
            var next = await fn.InvokeAsync([item], context).ConfigureAwait(false);
            var nextItems = next is object?[] na ? na : (next != null ? new[] { next } : Array.Empty<object?>());
            foreach (var ni in nextItems)
            {
                var key = QueryExecutionContext.Atomize(ni)?.ToString() ?? "";
                if (seen.Add(key))
                {
                    result.Add(ni);
                    queue.Enqueue(ni);
                }
            }
        }
        return result.ToArray();
    }
}

/// <summary>
/// fn:partition($seq as item()*, $pred as function(item()) as xs:boolean) as array(array(item()*))*
/// Splits a sequence into runs where the predicate has the same value (XPath 4.0).
/// </summary>
public sealed class PartitionFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "partition");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "pred"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var seq = arguments[0];
        var pred = arguments[1] as XQueryFunction;
        if (seq == null || pred == null) return Array.Empty<object>();

        var items = seq is object?[] arr ? arr : new[] { seq };
        if (items.Length == 0) return Array.Empty<object>();

        var result = new List<object?>();
        var currentGroup = new List<object?>();
        bool? lastMatch = null;

        foreach (var item in items)
        {
            var match = await pred.InvokeAsync([item], context).ConfigureAwait(false);
            var isMatch = match is true || (match is not false && match != null);

            if (lastMatch.HasValue && isMatch != lastMatch.Value)
            {
                result.Add(currentGroup.ToArray());
                currentGroup = [];
            }
            currentGroup.Add(item);
            lastMatch = isMatch;
        }
        if (currentGroup.Count > 0)
            result.Add(currentGroup.ToArray());

        return result.ToArray();
    }
}

/// <summary>
/// fn:iterate-while($seed as item()*, $fn as function(item()*) as item()*,
///                  $pred as function(item()*) as xs:boolean) as item()*
/// Iteratively applies fn while pred returns true (XPath 4.0).
/// </summary>
public sealed class IterateWhileFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "iterate-while");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "seed"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "fn"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "pred"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var current = arguments[0];
        var fn = arguments[1] as XQueryFunction;
        var pred = arguments[2] as XQueryFunction;
        if (fn == null || pred == null) return current;

        const int maxIterations = 10000;
        for (var i = 0; i < maxIterations; i++)
        {
            var shouldContinue = await pred.InvokeAsync([current], context).ConfigureAwait(false);
            if (!(shouldContinue is true || (shouldContinue is not false && shouldContinue != null)))
                break;
            current = await fn.InvokeAsync([current], context).ConfigureAwait(false);
        }
        return current;
    }
}

/// <summary>
/// fn:uniform($seq as xs:anyAtomicType*) as xs:boolean
/// Tests whether all values in a sequence are the same (using deep-equal) (XPath 4.0).
/// </summary>
public sealed class UniformFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "uniform");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var seq = arguments[0];
        if (seq == null) return ValueTask.FromResult<object?>(true);
        var items = seq is object?[] arr ? arr : new[] { seq };
        if (items.Length <= 1) return ValueTask.FromResult<object?>(true);

        var first = QueryExecutionContext.Atomize(items[0]);
        for (var i = 1; i < items.Length; i++)
        {
            var item = QueryExecutionContext.Atomize(items[i]);
            if (!Equals(first, item) && !Equals(first?.ToString(), item?.ToString()))
                return ValueTask.FromResult<object?>(false);
        }
        return ValueTask.FromResult<object?>(true);
    }
}

/// <summary>
/// fn:divide-decimals($dividend as xs:decimal, $divisor as xs:decimal,
///                    $scale as xs:integer) as xs:decimal
/// Decimal division with explicit scale (XPath 4.0).
/// </summary>
public sealed class DivideDecimalsFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "divide-decimals");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Decimal, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "dividend"), Type = new() { ItemType = ItemType.Decimal, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "divisor"), Type = new() { ItemType = ItemType.Decimal, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "scale"), Type = XdmSequenceType.Integer }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var dividend = Convert.ToDecimal(arguments[0]);
        var divisor = Convert.ToDecimal(arguments[1]);
        var scale = Convert.ToInt32(arguments[2]);
        if (divisor == 0) throw new InvalidOperationException("FOAR0002: Division by zero");
        var result = Math.Round(dividend / divisor, scale, MidpointRounding.AwayFromZero);
        return ValueTask.FromResult<object?>(result);
    }
}

/// <summary>
/// fn:default-language() as xs:language — returns system default language (XPath 4.0).
/// </summary>
public sealed class DefaultLanguageFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "default-language");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var lang = (context as Execution.QueryExecutionContext)?.DefaultLanguage
            ?? System.Globalization.CultureInfo.CurrentCulture.Name;
        return ValueTask.FromResult<object?>(lang);
    }
}

/// <summary>
/// fn:pin($value as item()*) as item()* — identity that prevents optimizer from inlining (XPath 4.0).
/// </summary>
public sealed class PinFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "pin");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "value"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
        => ValueTask.FromResult(arguments[0]);
}

/// <summary>
/// fn:collation-key($value as xs:string, $collation as xs:string?) as xs:base64Binary
/// Returns a binary key for collation-based comparison (XPath 4.0).
/// </summary>
public sealed class CollationKey1Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "collation-key");
    public override XdmSequenceType ReturnType => XdmSequenceType.Item;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "value"), Type = XdmSequenceType.String }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = Execution.QueryExecutionContext.Atomize(arguments[0]);
        // Per spec: $value must be xs:string (xs:untypedAtomic and xs:anyURI promote to string, other types are XPTY0004)
        if (arg is Xdm.XsUntypedAtomic ua)
            arg = ua.Value;
        if (arg is Xdm.XsAnyUri anyUri)
            arg = anyUri.Value;
        if (arg is not string)
            throw new XQueryException("XPTY0004",
                $"First argument to fn:collation-key must be xs:string, got {arg?.GetType().Name ?? "empty sequence"}");
        var collation = CollationHelper.GetDefaultComparison(context) == StringComparison.Ordinal
            ? null : (context is Execution.QueryExecutionContext qec ? qec.DefaultCollation : null);
        return ValueTask.FromResult<object?>(ComputeCollationKey((string)arg, collation));
    }

    internal static Xdm.XdmValue ComputeCollationKey(string value, string? collationUri)
    {
        if (collationUri != null && collationUri.StartsWith("http://www.w3.org/2013/collation/UCA", StringComparison.Ordinal))
        {
            var (compareInfo, options) = CollationHelper.GetUcaCollation(collationUri);
            var sortKey = compareInfo.GetSortKey(value, options);
            var caseFirst = CollationHelper.GetCaseFirst(collationUri);
            if (caseFirst is "lower" or "upper")
            {
                // Generate a case-insensitive sort key for the primary/secondary levels,
                // then append a case-level suffix so that caseFirst ordering is applied
                // correctly in byte-wise comparison.
                var ciKey = compareInfo.GetSortKey(value, options | System.Globalization.CompareOptions.IgnoreCase);
                var keyData = ciKey.KeyData;
                var suffix = new byte[value.Length + 1];
                suffix[0] = 0x01; // separator
                for (int i = 0; i < value.Length; i++)
                    suffix[i + 1] = caseFirst == "lower"
                        ? (byte)(char.IsUpper(value[i]) ? 1 : 0)
                        : (byte)(char.IsLower(value[i]) ? 1 : 0);
                var combined = new byte[keyData.Length + suffix.Length];
                keyData.CopyTo(combined, 0);
                suffix.CopyTo(combined, keyData.Length);
                return Xdm.XdmValue.Base64Binary(combined);
            }
            return Xdm.XdmValue.Base64Binary(sortKey.KeyData);
        }

        var comparison = CollationHelper.GetStringComparison(collationUri);
        if (comparison == StringComparison.OrdinalIgnoreCase)
        {
            // Case-insensitive: normalize to lowercase before generating key
            var normalized = value.ToLowerInvariant();
            var sortKeyBytes = System.Globalization.CultureInfo.InvariantCulture.CompareInfo
                .GetSortKey(normalized, System.Globalization.CompareOptions.IgnoreCase);
            return Xdm.XdmValue.Base64Binary(sortKeyBytes.KeyData);
        }

        var sk = System.Globalization.CultureInfo.InvariantCulture.CompareInfo
            .GetSortKey(value, System.Globalization.CompareOptions.None);
        return Xdm.XdmValue.Base64Binary(sk.KeyData);
    }
}

public sealed class CollationKeyFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "collation-key");
    public override XdmSequenceType ReturnType => XdmSequenceType.Item;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "value"), Type = XdmSequenceType.String },
        new() { Name = new QName(NamespaceId.None, "collation"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = Execution.QueryExecutionContext.Atomize(arguments[0]);
        // Per spec: $value must be xs:string (xs:untypedAtomic and xs:anyURI promote to string, other types are XPTY0004)
        if (arg is Xdm.XsUntypedAtomic ua)
            arg = ua.Value;
        if (arg is Xdm.XsAnyUri anyUri)
            arg = anyUri.Value;
        if (arg is not string)
            throw new XQueryException("XPTY0004",
                $"First argument to fn:collation-key must be xs:string, got {arg?.GetType().Name ?? "empty sequence"}");
        var collUri = arguments[1]?.ToString();
        return ValueTask.FromResult<object?>(
            CollationKey1Function.ComputeCollationKey((string)arg, collUri));
    }
}

/// <summary>
/// fn:parse-uri($uri as xs:string) as map(xs:string, xs:string)
/// Decomposes a URI into its components (XPath 4.0).
/// </summary>
public sealed class ParseUriFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "parse-uri");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "uri"), Type = XdmSequenceType.String }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var uriStr = arguments[0]?.ToString() ?? "";
        var result = new Dictionary<object, object?>();

        if (Uri.TryCreate(uriStr, UriKind.RelativeOrAbsolute, out var uri) && uri.IsAbsoluteUri)
        {
            result["scheme"] = uri.Scheme;
            if (!string.IsNullOrEmpty(uri.UserInfo)) result["userinfo"] = uri.UserInfo;
            result["host"] = uri.Host;
            if (uri.Port >= 0 && !uri.IsDefaultPort) result["port"] = (long)uri.Port;
            result["path"] = uri.AbsolutePath;
            if (!string.IsNullOrEmpty(uri.Query))
                result["query"] = uri.Query.TrimStart('?');
            if (!string.IsNullOrEmpty(uri.Fragment))
                result["fragment"] = uri.Fragment.TrimStart('#');
        }
        else
        {
            result["path"] = uriStr;
        }

        return ValueTask.FromResult<object?>(result);
    }
}

/// <summary>
/// fn:build-uri($components as map(xs:string, item()*)) as xs:string
/// Constructs a URI from component parts (XPath 4.0).
/// </summary>
public sealed class BuildUriFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "build-uri");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "components"), Type = new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne } }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        if (arguments[0] is not IDictionary<object, object?> map)
            return ValueTask.FromResult<object?>("");

        var sb = new System.Text.StringBuilder();

        if (MapKeyHelper.TryGetValue(map, "scheme", out var scheme) && scheme != null)
        {
            sb.Append(scheme).Append("://");
            if (MapKeyHelper.TryGetValue(map, "userinfo", out var userinfo) && userinfo != null)
                sb.Append(userinfo).Append('@');
            if (MapKeyHelper.TryGetValue(map, "host", out var host) && host != null)
                sb.Append(host);
            if (MapKeyHelper.TryGetValue(map, "port", out var port) && port != null)
                sb.Append(':').Append(port);
        }

        if (MapKeyHelper.TryGetValue(map, "path", out var path) && path != null)
            sb.Append(path);
        if (MapKeyHelper.TryGetValue(map, "query", out var query) && query != null)
            sb.Append('?').Append(query);
        if (MapKeyHelper.TryGetValue(map, "fragment", out var fragment) && fragment != null)
            sb.Append('#').Append(fragment);

        return ValueTask.FromResult<object?>(sb.ToString());
    }
}

/// <summary>
/// fn:contains-token($input as xs:string*, $token as xs:string) as xs:boolean
/// Tests if whitespace-separated tokens contain the given token (XPath 3.1/4.0).
/// </summary>
public sealed class ContainsTokenFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "contains-token");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "input"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "token"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var input = arguments[0];
        var token = arguments[1]?.ToString()?.Trim() ?? "";
        if (string.IsNullOrEmpty(token)) return ValueTask.FromResult<object?>(false);

        var strings = input is object?[] arr
            ? arr.Select(i => i?.ToString() ?? "")
            : new[] { input?.ToString() ?? "" };

        foreach (var str in strings)
        {
            var tokens = str.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Any(t => string.Equals(t.Trim(), token, StringComparison.Ordinal)))
                return ValueTask.FromResult<object?>(true);
        }
        return ValueTask.FromResult<object?>(false);
    }
}

/// <summary>fn:contains-token($input, $token, $collation) as xs:boolean</summary>
public sealed class ContainsToken3Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "contains-token");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "input"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "token"), Type = XdmSequenceType.String },
        new() { Name = new QName(NamespaceId.None, "collation"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var collation = arguments[2]?.ToString() ?? "";
        var comparison = collation.Contains("case-insensitive", StringComparison.OrdinalIgnoreCase)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var input = arguments[0];
        var token = arguments[1]?.ToString()?.Trim() ?? "";
        if (string.IsNullOrEmpty(token)) return ValueTask.FromResult<object?>(false);

        var strings = input is object?[] arr
            ? arr.Select(i => i?.ToString() ?? "")
            : new[] { input?.ToString() ?? "" };

        foreach (var str in strings)
        {
            var tokens = str.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Any(t => string.Equals(t.Trim(), token, comparison)))
                return ValueTask.FromResult<object?>(true);
        }
        return ValueTask.FromResult<object?>(false);
    }
}

/// <summary>
/// fn:char($name as xs:string) as xs:string — returns character by Unicode name or hex (XPath 4.0).
/// Accepts: hex codepoint (e.g., "A0"), Unicode name (e.g., "NO-BREAK SPACE"), or HTML entity name.
/// </summary>
public sealed class CharFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "char");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "name"), Type = XdmSequenceType.String }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var name = arguments[0]?.ToString() ?? "";

        // Try as hex codepoint first (e.g., "A0", "2019", "1F600")
        if (int.TryParse(name, System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture, out var codepoint))
        {
            return ValueTask.FromResult<object?>(char.ConvertFromUtf32(codepoint));
        }

        // Try common named characters
        var result = name.ToUpperInvariant() switch
        {
            "TAB" or "CHARACTER TABULATION" => "\t",
            "NEWLINE" or "LINE FEED" or "LF" => "\n",
            "CARRIAGE RETURN" or "CR" => "\r",
            "SPACE" => " ",
            "NO-BREAK SPACE" or "NBSP" => "\u00A0",
            "ZERO WIDTH SPACE" => "\u200B",
            "ZERO WIDTH NON-JOINER" or "ZWNJ" => "\u200C",
            "ZERO WIDTH JOINER" or "ZWJ" => "\u200D",
            "SOFT HYPHEN" or "SHY" => "\u00AD",
            "EN DASH" => "\u2013",
            "EM DASH" => "\u2014",
            "LEFT SINGLE QUOTATION MARK" => "\u2018",
            "RIGHT SINGLE QUOTATION MARK" => "\u2019",
            "LEFT DOUBLE QUOTATION MARK" => "\u201C",
            "RIGHT DOUBLE QUOTATION MARK" => "\u201D",
            "BULLET" => "\u2022",
            "HORIZONTAL ELLIPSIS" => "\u2026",
            "EURO SIGN" => "\u20AC",
            "COPYRIGHT SIGN" => "\u00A9",
            "REGISTERED SIGN" => "\u00AE",
            "TRADE MARK SIGN" => "\u2122",
            "DEGREE SIGN" => "\u00B0",
            "MULTIPLICATION SIGN" => "\u00D7",
            "DIVISION SIGN" => "\u00F7",
            _ => null
        };

        if (result != null)
            return ValueTask.FromResult<object?>(result);

        throw new InvalidOperationException($"FOCH0005: Unknown character name '{name}'");
    }
}

/// <summary>
/// fn:codepoint($char as xs:string) as xs:integer — returns Unicode codepoint (XPath 4.0).
/// </summary>
public sealed class CodepointFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "codepoint");
    public override XdmSequenceType ReturnType => XdmSequenceType.Integer;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "char"), Type = XdmSequenceType.String }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var str = arguments[0]?.ToString() ?? "";
        if (str.Length == 0)
            throw new InvalidOperationException("FOCH0004: fn:codepoint requires a single character string");
        var cp = char.ConvertToUtf32(str, 0);
        return ValueTask.FromResult<object?>((long)cp);
    }
}

/// <summary>
/// fn:in-scope-namespaces($element as element()) as map(xs:string, xs:anyURI)
/// Returns a map of prefix → namespace URI for all in-scope namespaces (XPath 4.0).
/// </summary>
public sealed class InScopeNamespacesFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "in-scope-namespaces");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "element"), Type = new() { ItemType = ItemType.Element, Occurrence = Occurrence.ExactlyOne } }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var result = new Dictionary<object, object?>();

        if (arguments[0] is PhoenixmlDb.Xdm.Nodes.XdmElement elem)
        {
            // Add xml namespace (always in scope)
            result["xml"] = new PhoenixmlDb.Xdm.XsAnyUri("http://www.w3.org/XML/1998/namespace");

            // Get namespaces from the element's declarations
            foreach (var binding in elem.NamespaceDeclarations)
            {
                // Resolve NamespaceId to URI string
                var nsUri = PhoenixmlDb.XQuery.Functions.FunctionNamespaces.ResolveNamespace(binding.Namespace);
                if (nsUri != null)
                    result[binding.Prefix] = new PhoenixmlDb.Xdm.XsAnyUri(nsUri);
            }
        }

        return ValueTask.FromResult<object?>(result);
    }
}

/// <summary>
/// fn:intersperse($seq as item()*, $sep as item()) as item()* — inserts single item between (XPath 4.0).
/// Simpler version of fn:insert-separator for single-item separators.
/// </summary>
public sealed class IntersperseFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "intersperse");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "sep"), Type = XdmSequenceType.Item }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var seq = arguments[0];
        var sep = arguments[1];
        if (seq == null) return ValueTask.FromResult<object?>(Array.Empty<object>());
        var items = seq is object?[] arr ? arr : new[] { seq };
        if (items.Length <= 1) return ValueTask.FromResult<object?>(seq);

        var result = new List<object?>(items.Length * 2 - 1);
        for (var i = 0; i < items.Length; i++)
        {
            if (i > 0) result.Add(sep);
            result.Add(items[i]);
        }
        return ValueTask.FromResult<object?>(result.ToArray());
    }
}

/// <summary>
/// fn:distinct-ordered($seq as xs:anyAtomicType*) as xs:anyAtomicType*
/// Returns distinct values preserving first-occurrence order (XPath 4.0).
/// </summary>
public sealed class DistinctOrderedFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "distinct-ordered");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var seq = arguments[0];
        if (seq == null) return ValueTask.FromResult<object?>(Array.Empty<object>());
        var items = seq is object?[] arr ? arr : new[] { seq };

        var seen = new HashSet<string>();
        var result = new List<object?>();
        foreach (var item in items)
        {
            var val = QueryExecutionContext.Atomize(item)?.ToString() ?? "";
            if (seen.Add(val))
                result.Add(item);
        }
        return ValueTask.FromResult<object?>(result.Count == 1 ? result[0] : result.ToArray());
    }
}

/// <summary>
/// fn:sort-by($seq as item()*, $key as function(item()) as xs:anyAtomicType?) as item()*
/// Sorts by a key function (XPath 4.0).
/// </summary>
public sealed class SortByFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "sort-by");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "key"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var seq = arguments[0];
        var keyFn = arguments[1] as XQueryFunction;
        if (seq == null || keyFn == null) return Array.Empty<object>();
        var items = seq is object?[] arr ? arr.ToList() : new List<object?> { seq };

        var keyed = new List<(object? Item, string Key)>();
        foreach (var item in items)
        {
            var key = await keyFn.InvokeAsync([item], context).ConfigureAwait(false);
            keyed.Add((item, QueryExecutionContext.Atomize(key)?.ToString() ?? ""));
        }

        keyed.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.Ordinal));
        var result = keyed.Select(k => k.Item).ToList();
        return result.Count == 1 ? result[0] : result.ToArray();
    }
}

/// <summary>
/// fn:parse-html($html as xs:string?) as document-node()? — parses HTML into an XDM tree (XPath 4.0).
/// Uses .NET's XmlDocument with a best-effort HTML-to-XML approach.
/// </summary>
public sealed class ParseHtmlFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "parse-html");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Document, Occurrence = Occurrence.ZeroOrOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "html"), Type = XdmSequenceType.OptionalString }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var html = arguments[0]?.ToString();
        if (string.IsNullOrEmpty(html)) return ValueTask.FromResult<object?>(null);

        try
        {
            // Best-effort HTML parsing: wrap in root if needed, try as XML
            var normalized = html.Trim();
            if (!normalized.StartsWith('<'))
                normalized = $"<html><body>{normalized}</body></html>";

            // Try parsing as well-formed XML first
            var doc = new System.Xml.XmlDocument();
            doc.PreserveWhitespace = true;
            try
            {
                doc.LoadXml(normalized);
            }
            catch (System.Xml.XmlException)
            {
                // Fallback: escape and wrap
                doc.LoadXml($"<html><body>{System.Security.SecurityElement.Escape(html)}</body></html>");
            }

            // Return the parsed document as a LINQ XDocument for downstream processing
            return ValueTask.FromResult<object?>(
                System.Xml.Linq.XDocument.Parse(doc.OuterXml));
        }
        catch (System.Xml.XmlException)
        {
            // Completely unparseable HTML — return null
            return ValueTask.FromResult<object?>(null);
        }
    }
}

/// <summary>
/// fn:type($item as item()) as record(name, namespace, kind) — returns type info (XPath 4.0).
/// </summary>
public sealed class TypeFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "type");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "item"), Type = XdmSequenceType.Item }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var item = arguments[0];
        var result = new Dictionary<object, object?>();

        var (kind, typeName) = item switch
        {
            null => ("empty-sequence", "empty-sequence"),
            bool => ("atomic", "xs:boolean"),
            int or long or System.Numerics.BigInteger => ("atomic", "xs:integer"),
            decimal => ("atomic", "xs:decimal"),
            double => ("atomic", "xs:double"),
            float => ("atomic", "xs:float"),
            string => ("atomic", "xs:string"),
            Xdm.XsDate => ("atomic", "xs:date"),
            Xdm.XsDateTime or DateTimeOffset => ("atomic", "xs:dateTime"),
            Xdm.XsTime => ("atomic", "xs:time"),
            Xdm.XsAnyUri => ("atomic", "xs:anyURI"),
            Xdm.XsUntypedAtomic => ("atomic", "xs:untypedAtomic"),
            PhoenixmlDb.Core.QName => ("atomic", "xs:QName"),
            Dictionary<object, object?> => ("map", "map(*)"),
            List<object?> => ("array", "array(*)"),
            XQueryFunction => ("function", "function(*)"),
            PhoenixmlDb.Xdm.Nodes.XdmElement => ("element", "element()"),
            PhoenixmlDb.Xdm.Nodes.XdmAttribute => ("attribute", "attribute()"),
            PhoenixmlDb.Xdm.Nodes.XdmText => ("text", "text()"),
            PhoenixmlDb.Xdm.Nodes.XdmComment => ("comment", "comment()"),
            PhoenixmlDb.Xdm.Nodes.XdmDocument => ("document-node", "document-node()"),
            PhoenixmlDb.Xdm.Nodes.XdmProcessingInstruction => ("processing-instruction", "processing-instruction()"),
            _ => ("item", item.GetType().Name)
        };

        result["kind"] = kind;
        result["name"] = typeName;
        return ValueTask.FromResult<object?>(result);
    }
}

/// <summary>
/// fn:graphemes($arg as xs:string?) as xs:string* — splits into grapheme clusters (XPath 4.0).
/// Similar to fn:characters but handles multi-codepoint grapheme clusters correctly.
/// </summary>
public sealed class GraphemesFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "graphemes");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.String, Occurrence = Occurrence.ZeroOrMore };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalString }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var str = arguments[0]?.ToString();
        if (string.IsNullOrEmpty(str)) return ValueTask.FromResult<object?>(Array.Empty<string>());
        var graphemes = new List<string>();
        var enumerator = System.Globalization.StringInfo.GetTextElementEnumerator(str);
        while (enumerator.MoveNext())
            graphemes.Add(enumerator.GetTextElement());
        return ValueTask.FromResult<object?>(graphemes.ToArray());
    }
}

/// <summary>
/// fn:some($seq as item()*, $pred as function(item()) as xs:boolean) as xs:boolean
/// Tests if any item in the sequence satisfies the predicate (XPath 4.0).
/// </summary>
public sealed class SomeFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "some");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "pred"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var seq = arguments[0];
        var pred = arguments[1] as XQueryFunction;
        if (seq == null || pred == null) return false;
        var items = seq is object?[] arr ? arr : new[] { seq };

        foreach (var item in items)
        {
            var result = await pred.InvokeAsync([item], context).ConfigureAwait(false);
            if (result is true || (result is not false && result != null))
                return true;
        }
        return false;
    }
}

/// <summary>
/// fn:every($seq as item()*, $pred as function(item()) as xs:boolean) as xs:boolean
/// Tests if all items in the sequence satisfy the predicate (XPath 4.0).
/// </summary>
public sealed class EveryFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "every");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "pred"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var seq = arguments[0];
        var pred = arguments[1] as XQueryFunction;
        if (pred == null) return false;
        if (seq == null) return true;
        var items = seq is object?[] arr ? arr : new[] { seq };

        foreach (var item in items)
        {
            var result = await pred.InvokeAsync([item], context).ConfigureAwait(false);
            if (!(result is true || (result is not false && result != null)))
                return false;
        }
        return true;
    }
}

/// <summary>
/// fn:ends-with-subsequence($seq as item()*, $subseq as item()*) as xs:boolean (XPath 4.0).
/// </summary>
public sealed class EndsWithSubsequenceFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "ends-with-subsequence");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "subseq"), Type = XdmSequenceType.ZeroOrMoreItems }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var seq = arguments[0] is object?[] a1 ? a1 : (arguments[0] != null ? new[] { arguments[0] } : Array.Empty<object?>());
        var sub = arguments[1] is object?[] a2 ? a2 : (arguments[1] != null ? new[] { arguments[1] } : Array.Empty<object?>());

        if (sub.Length == 0) return ValueTask.FromResult<object?>(true);
        if (sub.Length > seq.Length) return ValueTask.FromResult<object?>(false);

        var offset = seq.Length - sub.Length;
        for (var j = 0; j < sub.Length; j++)
        {
            if (!Equals(QueryExecutionContext.Atomize(seq[offset + j])?.ToString(),
                        QueryExecutionContext.Atomize(sub[j])?.ToString()))
                return ValueTask.FromResult<object?>(false);
        }
        return ValueTask.FromResult<object?>(true);
    }
}
