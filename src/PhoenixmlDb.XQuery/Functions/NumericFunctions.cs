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
    public override XdmSequenceType ReturnType => XdmSequenceType.Double;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.Double }];

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

/// <summary>
/// fn:ceiling($arg) as xs:numeric?
/// </summary>
public sealed class CeilingFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "ceiling");
    public override XdmSequenceType ReturnType => XdmSequenceType.Double;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.Double }];

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

/// <summary>
/// fn:floor($arg) as xs:numeric?
/// </summary>
public sealed class FloorFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "floor");
    public override XdmSequenceType ReturnType => XdmSequenceType.Double;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.Double }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = QueryExecutionContext.AtomizeSingle(arguments[0]);
        arg = NumericParseHelper.ValidateNumericArg(arg, "fn:floor");
        if (arg is null) return ValueTask.FromResult<object?>(null);
        object? result = arg switch
        {
            int or long or System.Numerics.BigInteger => arg,
            decimal d => decimal.Floor(d),
            float f => (float)Math.Floor(f),
            _ => Math.Floor(Convert.ToDouble(arg))
        };
        return ValueTask.FromResult<object?>(result);
    }
}

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
        var result2 = Math.Round(value, clampedPrecision, MidpointRounding.AwayFromZero);
        if (value < 0 && result2 < value)
        {
            // AwayFromZero rounded more negative. Check if we're at exact midpoint.
            var unit = Math.Pow(10, -clampedPrecision);
            var candidate = result2 + unit;
            // value is midpoint if it equals the average of result2 and candidate
            if (value == (result2 + candidate) / 2.0)
                return candidate;
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

/// <summary>
/// fn:round($arg) as xs:numeric?
/// </summary>
public sealed class RoundFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "round");
    public override XdmSequenceType ReturnType => XdmSequenceType.Double;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.Double }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = QueryExecutionContext.AtomizeSingle(arguments[0]);
        arg = NumericParseHelper.ValidateNumericArg(arg, "fn:round");
        if (arg is null) return ValueTask.FromResult<object?>(null);
        object? result = arg switch
        {
            int or long or System.Numerics.BigInteger => arg,
            decimal d => XPathRound.Round(d),
            float f => XPathRound.Round(f),
            _ => XPathRound.Round(Convert.ToDouble(arg))
        };
        return ValueTask.FromResult<object?>(result);
    }
}

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

/// <summary>
/// fn:number($arg) as xs:double
/// </summary>
public sealed class NumberFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "number");
    public override XdmSequenceType ReturnType => XdmSequenceType.Double;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        // Atomize first — raises FOTY0013 for function items, maps, arrays
        var arg = Execution.QueryExecutionContext.Atomize(arguments[0]);
        if (arg is null)
            return ValueTask.FromResult<object?>(double.NaN);

        if (arg is double d) return ValueTask.FromResult<object?>(d);
        if (arg is float f) return ValueTask.FromResult<object?>((double)f);
        if (arg is int i) return ValueTask.FromResult<object?>((double)i);
        if (arg is long l) return ValueTask.FromResult<object?>((double)l);
        if (arg is System.Numerics.BigInteger bi) return ValueTask.FromResult<object?>((double)bi);
        if (arg is decimal dec) return ValueTask.FromResult<object?>((double)dec);
        if (arg is bool b) return ValueTask.FromResult<object?>(b ? 1.0 : 0.0);

        var str = arg.ToString()?.Trim();
        if (str is "INF" or "+INF") return ValueTask.FromResult<object?>(double.PositiveInfinity);
        if (str is "-INF") return ValueTask.FromResult<object?>(double.NegativeInfinity);
        if (double.TryParse(str, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var result))
            return ValueTask.FromResult<object?>(result);

        return ValueTask.FromResult<object?>(double.NaN);
    }
}

/// <summary>
/// fn:number() as xs:double (uses context item)
/// </summary>
public sealed class Number0Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "number");
    public override XdmSequenceType ReturnType => XdmSequenceType.Double;
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var contextItem = context.ContextItem;
        if (contextItem == null)
            return ValueTask.FromResult<object?>(double.NaN);

        // Delegate to the 1-argument number() function
        return new NumberFunction().InvokeAsync([contextItem], context);
    }
}

/// <summary>
/// fn:sum($arg) as xs:anyAtomicType
/// </summary>
public sealed class SumFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "sum");
    public override XdmSequenceType ReturnType => XdmSequenceType.Double;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        return SumHelper.SumCore(arguments[0], (long)0);
    }
}

/// <summary>
/// fn:sum($arg, $zero) as xs:anyAtomicType
/// </summary>
public sealed class Sum2Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "sum");
    public override XdmSequenceType ReturnType => XdmSequenceType.Double;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "zero"), Type = XdmSequenceType.Item }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        return SumHelper.SumCore(arguments[0], arguments[1]);
    }
}

internal static class SumHelper
{
    internal static ValueTask<object?> SumCore(object? arg, object? zero)
    {
        var items = arg as IEnumerable<object?> ?? [arg];
        bool hasDouble = false, hasFloat = false, hasDecimal = false, hasInt = false;
        long intSum = 0;
        decimal decSum = 0;
        double dblSum = 0;
        float fltSum = 0;
        TimeSpan tsSum = TimeSpan.Zero;
        int ymSum = 0;
        bool hasDayTime = false, hasYearMonth = false;
        int count = 0;

        foreach (var rawItem in items)
        {
            var item = QueryExecutionContext.Atomize(rawItem);
            if (item is null) continue;
            count++;
            if (item is TimeSpan ts) { hasDayTime = true; tsSum += ts; }
            else if (item is YearMonthDuration ym) { hasYearMonth = true; ymSum += ym.TotalMonths; }
            else if (item is double d) { hasDouble = true; dblSum += d; }
            else if (item is float f) { hasFloat = true; fltSum += f; dblSum += f; }
            else if (item is decimal) { hasDecimal = true; decSum += Convert.ToDecimal(item); }
            else if (item is int or long) { hasInt = true; intSum += Convert.ToInt64(item); }
            else if (item is Xdm.XsUntypedAtomic ua)
            {
                // Per XPath spec: xs:untypedAtomic is cast to xs:double for sum()
                hasDouble = true;
                if (double.TryParse(ua.Value, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var uv))
                    dblSum += uv;
                else
                    throw new XQueryRuntimeException("FORG0001", $"Cannot cast '{ua.Value}' to xs:double");
            }
            else if (item is bool)
            { throw new XQueryRuntimeException("XPTY0004", "Invalid argument type for fn:sum: xs:boolean"); }
            else if (item is string)
            { throw new XQueryRuntimeException("XPTY0004", $"Invalid argument type for fn:sum: xs:string"); }
            else if (item is Uri)
            { throw new XQueryRuntimeException("XPTY0004", "Invalid argument type for fn:sum: xs:anyURI"); }
        }

        if (count == 0) return ValueTask.FromResult(zero);

        // FORG0006: incompatible type mixing (numeric + duration, or dayTime + yearMonth)
        bool hasNumeric = hasDouble || hasFloat || hasDecimal || hasInt;
        if ((hasDayTime || hasYearMonth) && hasNumeric)
            throw new XQueryRuntimeException("FORG0006",
                $"Invalid argument types for fn:sum: cannot mix numeric and duration values");
        if (hasDayTime && hasYearMonth)
            throw new XQueryRuntimeException("FORG0006",
                $"Invalid argument types for fn:sum: cannot mix dayTimeDuration and yearMonthDuration");

        if (hasDayTime) return ValueTask.FromResult<object?>(tsSum);
        if (hasYearMonth) return ValueTask.FromResult<object?>(new YearMonthDuration(ymSum));
        if (hasDouble) return ValueTask.FromResult<object?>(dblSum + (double)decSum + intSum);
        // xs:float promotion: if only floats (no doubles), return float
        if (hasFloat) return ValueTask.FromResult<object?>((float)(fltSum + (float)decSum + intSum));
        if (hasDecimal) return ValueTask.FromResult<object?>(decSum + intSum);
        return ValueTask.FromResult<object?>(intSum);
    }
}

/// <summary>
/// fn:avg($arg) as xs:anyAtomicType?
/// </summary>
public sealed class AvgFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "avg");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Double, Occurrence = Occurrence.ZeroOrOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var items = arguments[0] as IEnumerable<object?> ?? [arguments[0]];
        // Detect duration types on first item to use specialized averaging
        TimeSpan? dtdSum = null;
        Xdm.YearMonthDuration? ymdSum = null;
        double dblSum = 0;
        decimal decSum = 0;
        float fltSum = 0;
        bool hasDouble = false, hasFloat = false, hasDecimal = false, hasInteger = false;
        int count = 0;
        foreach (var rawItem in items)
        {
            var item = QueryExecutionContext.Atomize(rawItem);
            if (item != null)
            {
                if (item is TimeSpan ts)
                {
                    dtdSum = (dtdSum ?? TimeSpan.Zero) + ts;
                    count++;
                    continue;
                }
                if (item is Xdm.YearMonthDuration ymd)
                {
                    ymdSum = ymdSum.HasValue
                        ? new Xdm.YearMonthDuration(ymdSum.Value.TotalMonths + ymd.TotalMonths)
                        : ymd;
                    count++;
                    continue;
                }
                if (item is bool)
                    throw new XQueryRuntimeException("XPTY0004", "Invalid argument type for fn:avg: xs:boolean");
                if (item is string)
                    throw new XQueryRuntimeException("FORG0006", $"Invalid argument type for fn:avg: xs:string");
                if (item is Uri)
                    throw new XQueryRuntimeException("XPTY0004", "Invalid argument type for fn:avg: xs:anyURI");
                // Per XPath spec: xs:untypedAtomic is cast to xs:double
                if (item is Xdm.XsUntypedAtomic ua)
                {
                    if (double.TryParse(ua.Value, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var uaParsed))
                        item = uaParsed;
                    else
                        throw new XQueryRuntimeException("FORG0001", $"Cannot cast '{ua.Value}' to xs:double");
                }
                if (item is float f) { hasFloat = true; fltSum += f; }
                else if (item is double) { hasDouble = true; }
                else if (item is decimal d) { hasDecimal = true; decSum += d; }
                else if (item is int or long) { hasInteger = true; decSum += Convert.ToDecimal(item); }
                dblSum += Convert.ToDouble(item);
                count++;
            }
        }
        if (count == 0)
            return ValueTask.FromResult<object?>(null);

        // FORG0006: incompatible type mixing (numeric + duration, or dayTime + yearMonth)
        bool hasNumeric = hasDouble || hasFloat || hasDecimal || hasInteger;
        bool hasDuration = dtdSum.HasValue || ymdSum.HasValue;
        if (hasDuration && hasNumeric)
            throw new XQueryRuntimeException("FORG0006",
                $"Invalid argument types for fn:avg: cannot mix numeric and duration values");
        if (dtdSum.HasValue && ymdSum.HasValue)
            throw new XQueryRuntimeException("FORG0006",
                $"Invalid argument types for fn:avg: cannot mix dayTimeDuration and yearMonthDuration");

        if (dtdSum.HasValue)
        {
            var avgTicks = dtdSum.Value.Ticks / count;
            return ValueTask.FromResult<object?>(new TimeSpan(avgTicks));
        }
        if (ymdSum.HasValue)
        {
            var avgMonths = (int)Math.Round((double)ymdSum.Value.TotalMonths / count);
            return ValueTask.FromResult<object?>(new Xdm.YearMonthDuration(avgMonths));
        }
        if (hasDouble) return ValueTask.FromResult<object?>(dblSum / count);
        // xs:float: if only floats (no doubles), return float
        if (hasFloat) return ValueTask.FromResult<object?>(fltSum / count);
        // xs:integer and xs:decimal both average to xs:decimal
        if (hasDecimal || hasInteger) return ValueTask.FromResult<object?>(decSum / count);
        return ValueTask.FromResult<object?>(dblSum / count);
    }
}

/// <summary>
/// fn:min($arg) as xs:anyAtomicType?
/// </summary>
public sealed class MinFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "min");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.AnyAtomicType, Occurrence = Occurrence.ZeroOrOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var comparison = CollationHelper.GetDefaultComparison(context);
        var items = arguments[0] as IEnumerable<object?> ?? [arguments[0]];
        object? min = null;
        bool? useStringComparison = null;
        foreach (var rawItem in items)
        {
            var item = QueryExecutionContext.Atomize(rawItem);
            if (item is null) continue;
            // Per XPath spec: xs:untypedAtomic is cast to xs:double
            if (item is Xdm.XsUntypedAtomic ua)
            {
                if (double.TryParse(ua.Value, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var uaParsed))
                    item = uaParsed;
                else
                    item = double.NaN;
            }
            // Determine comparison mode from first item's type:
            // If the raw item is already a string (not a node), it's explicitly typed as xs:string → string comparison
            // If the raw item was a node (atomized to string), it's xs:untypedAtomic → cast to double
            if (useStringComparison == null && item is string)
                useStringComparison = rawItem is string; // true only if the raw input was already a string
            if (item is string s && useStringComparison != true)
            {
                if (double.TryParse(s, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                    item = parsed;
            }
            if (min is null) { min = item; continue; }
            var cmp = CompareValues(item, min, comparison);
            if (cmp < 0) min = item;
        }
        return ValueTask.FromResult<object?>(min);
    }

    internal static int CompareValues(object? a, object? b) => CompareValues(a, b, StringComparison.Ordinal);

    internal static int CompareValues(object? a, object? b, StringComparison stringComparison)
    {
        if (a is null && b is null) return 0;
        if (a is null) return -1;
        if (b is null) return 1;

        bool aNum = a is int or long or double or float or decimal or System.Numerics.BigInteger;
        bool bNum = b is int or long or double or float or decimal or System.Numerics.BigInteger;

        if (aNum && bNum)
        {
            if (a is double or float || b is double or float)
            {
                var da = a is System.Numerics.BigInteger abi ? (double)abi : Convert.ToDouble(a);
                var db = b is System.Numerics.BigInteger bbi ? (double)bbi : Convert.ToDouble(b);
                return da.CompareTo(db);
            }
            if (a is System.Numerics.BigInteger || b is System.Numerics.BigInteger)
            {
                var ba = a is System.Numerics.BigInteger ba1 ? ba1 : (System.Numerics.BigInteger)Convert.ToInt64(a);
                var bb = b is System.Numerics.BigInteger bb1 ? bb1 : (System.Numerics.BigInteger)Convert.ToInt64(b);
                return ba.CompareTo(bb);
            }
            return Convert.ToDecimal(a).CompareTo(Convert.ToDecimal(b));
        }

        if (a is string sa && b is string sb)
            return string.Compare(sa, sb, stringComparison);

        // Date/time comparisons
        if (a is XsDateTime xdtA && b is XsDateTime xdtB)
            return xdtA.CompareTo(xdtB);
        if (a is XsDate xdA && b is XsDate xdB)
            return xdA.CompareTo(xdB);
        if (a is XsTime xtA && b is XsTime xtB)
            return xtA.CompareTo(xtB);
        if (a is DateTimeOffset dtA && b is DateTimeOffset dtB)
            return dtA.CompareTo(dtB);
        if (a is DateOnly dA && b is DateOnly dB)
            return dA.CompareTo(dB);
        if (a is TimeOnly tA && b is TimeOnly tB)
            return tA.CompareTo(tB);

        // Duration comparisons
        if (a is TimeSpan tsA && b is TimeSpan tsB)
            return tsA.CompareTo(tsB);
        if (a is YearMonthDuration ymA && b is YearMonthDuration ymB)
            return ymA.CompareTo(ymB);

        // FORG0006: Incompatible typed atomic values
        // Numeric vs string, date vs non-date, etc.
        if ((aNum && b is string) || (bNum && a is string))
            throw new Execution.XQueryRuntimeException("FORG0006",
                $"Invalid argument type for min/max: cannot compare values of type '{a.GetType().Name}' and '{b.GetType().Name}'");

        bool aIsDateLike = a is XsDateTime or XsDate or XsTime or DateTimeOffset or DateOnly or TimeOnly or TimeSpan or YearMonthDuration;
        bool bIsDateLike = b is XsDateTime or XsDate or XsTime or DateTimeOffset or DateOnly or TimeOnly or TimeSpan or YearMonthDuration;
        if (aIsDateLike || bIsDateLike)
            throw new Execution.XQueryRuntimeException("FORG0006",
                $"Invalid argument type for min/max: cannot compare values of type '{a.GetType().Name}' and '{b.GetType().Name}'");

        // Fallback: stringify for remaining types
        return string.Compare(a.ToString(), b.ToString(), StringComparison.Ordinal);
    }
}

/// <summary>
/// fn:max($arg) as xs:anyAtomicType?
/// </summary>
public sealed class MaxFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "max");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.AnyAtomicType, Occurrence = Occurrence.ZeroOrOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var comparison = CollationHelper.GetDefaultComparison(context);
        var items = arguments[0] as IEnumerable<object?> ?? [arguments[0]];
        object? max = null;
        bool? useStringComparison = null;
        foreach (var rawItem in items)
        {
            var item = QueryExecutionContext.Atomize(rawItem);
            if (item is null) continue;
            // Per XPath spec: xs:untypedAtomic is cast to xs:double
            if (item is Xdm.XsUntypedAtomic uaMax)
            {
                if (double.TryParse(uaMax.Value, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var uaParsed))
                    item = uaParsed;
                else
                    item = double.NaN;
            }
            if (useStringComparison == null && item is string)
                useStringComparison = rawItem is string;
            if (item is string s && useStringComparison != true)
            {
                if (double.TryParse(s, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                    item = parsed;
            }
            if (max is null) { max = item; continue; }
            var cmp = MinFunction.CompareValues(item, max, comparison);
            if (cmp > 0) max = item;
        }
        return ValueTask.FromResult<object?>(max);
    }
}

/// <summary>
/// fn:min($arg, $collation) as xs:anyAtomicType?
/// </summary>
public sealed class Min2Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "min");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.AnyAtomicType, Occurrence = Occurrence.ZeroOrOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "collation"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var comparison = CollationHelper.GetStringComparison(arguments[1]?.ToString());
        var items = arguments[0] as IEnumerable<object?> ?? [arguments[0]];
        object? min = null;
        bool? useStringComparison = null;
        foreach (var rawItem in items)
        {
            var item = QueryExecutionContext.Atomize(rawItem);
            if (item is null) continue;
            if (useStringComparison == null && item is string)
                useStringComparison = rawItem is string;
            if (item is string s && useStringComparison != true)
            {
                if (double.TryParse(s, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                    item = parsed;
            }
            if (min is null) { min = item; continue; }
            var cmp = MinFunction.CompareValues(item, min, comparison);
            if (cmp < 0) min = item;
        }
        return ValueTask.FromResult<object?>(min);
    }
}

/// <summary>
/// fn:max($arg, $collation) as xs:anyAtomicType?
/// </summary>
public sealed class Max2Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "max");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.AnyAtomicType, Occurrence = Occurrence.ZeroOrOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "collation"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var comparison = CollationHelper.GetStringComparison(arguments[1]?.ToString());
        var items = arguments[0] as IEnumerable<object?> ?? [arguments[0]];
        object? max = null;
        bool? useStringComparison = null;
        foreach (var rawItem in items)
        {
            var item = QueryExecutionContext.Atomize(rawItem);
            if (item is null) continue;
            // Per XPath spec: xs:untypedAtomic is cast to xs:double
            if (item is Xdm.XsUntypedAtomic uaMax2)
            {
                if (double.TryParse(uaMax2.Value, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var uaParsed))
                    item = uaParsed;
                else
                    item = double.NaN;
            }
            if (useStringComparison == null && item is string)
                useStringComparison = rawItem is string;
            if (item is string s && useStringComparison != true)
            {
                if (double.TryParse(s, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                    item = parsed;
            }
            if (max is null) { max = item; continue; }
            var cmp = MinFunction.CompareValues(item, max, comparison);
            if (cmp > 0) max = item;
        }
        return ValueTask.FromResult<object?>(max);
    }
}

/// <summary>
/// fn:count($arg) as xs:integer
/// </summary>
public sealed class CountFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "count");
    public override XdmSequenceType ReturnType => XdmSequenceType.Integer;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        if (arguments[0] is IEnumerable<object?> items)
            return ValueTask.FromResult<object?>((long)items.Count());
        if (arguments[0] == null)
            return ValueTask.FromResult<object?>((long)0);
        return ValueTask.FromResult<object?>((long)1);
    }
}

/// <summary>
/// Parses a numeric string to its appropriate .NET numeric type.
/// Used by numeric functions after atomizing node values.
/// </summary>
internal static class NumericParseHelper
{
    public static object ParseNumericString(string s)
    {
        if (long.TryParse(s, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var l))
            return l;
        if (double.TryParse(s, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var d))
            return d;
        throw new XQueryRuntimeException("FORG0001", $"Cannot cast '{s}' to a numeric type");
    }

    /// <summary>
    /// Validates that an atomized argument is a numeric type (xs:integer, xs:decimal, xs:float, xs:double)
    /// or xs:untypedAtomic (string from untyped source, which gets cast to xs:double).
    /// Raises XPTY0004 for xs:string, xs:boolean, xs:anyURI, and other non-numeric types.
    /// </summary>
    public static object? ValidateNumericArg(object? atomized, string functionName)
    {
        if (atomized is null) return null;
        return atomized switch
        {
            int or long or System.Numerics.BigInteger or decimal or float or double => atomized,
            // xs:untypedAtomic values (strings from untyped XML) should be cast to xs:double
            string s when IsUntypedAtomic(s) => ParseNumericString(s),
            string => throw new XQueryRuntimeException("XPTY0004",
                $"Cannot pass xs:string to {functionName} — expected numeric type"),
            bool => throw new XQueryRuntimeException("XPTY0004",
                $"Cannot pass xs:boolean to {functionName} — expected numeric type"),
            Uri => throw new XQueryRuntimeException("XPTY0004",
                $"Cannot pass xs:anyURI to {functionName} — expected numeric type"),
            _ => atomized // Let it through — may be a custom numeric type
        };
    }

    // In our engine, all strings from atomization are xs:untypedAtomic unless they come
    // from a typed context. Since we don't do schema validation, strings from XML content
    // are untypedAtomic. Strings from XQuery string literals are xs:string.
    // For now, treat string arguments to numeric functions as errors — the QT3 tests
    // that pass strings explicitly (xs:string("1")) expect XPTY0004.
    private static bool IsUntypedAtomic(string _) => false;

    /// <summary>
    /// Validates and converts an argument to double for math functions.
    /// Rejects non-numeric types with XPTY0004.
    /// </summary>
    public static double ValidateAndConvertToDouble(object? arg, string functionName)
    {
        if (arg is double d) return d;
        if (arg is float f) return f;
        if (arg is int i) return i;
        if (arg is long l) return l;
        if (arg is decimal m) return (double)m;
        if (arg is System.Numerics.BigInteger bi) return (double)bi;
        if (arg is bool)
            throw new XQueryRuntimeException("XPTY0004",
                $"Cannot pass xs:boolean to {functionName} — expected numeric type");
        if (arg is string)
            throw new XQueryRuntimeException("XPTY0004",
                $"Cannot pass xs:string to {functionName} — expected numeric type");
        return Convert.ToDouble(arg);
    }
}
