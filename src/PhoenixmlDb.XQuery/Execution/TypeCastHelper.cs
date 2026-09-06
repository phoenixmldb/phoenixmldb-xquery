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
/// Helper for type casting and type checking operations.
/// </summary>
public static class TypeCastHelper
{
    /// <summary>
    /// Wraps date/time/dateTime/duration parsing to convert .NET exceptions into FORG0001.
    /// </summary>
    internal static T SafeParseDateType<T>(Func<T> parse, string typeName, string input)
    {
        try
        {
            return parse();
        }
        catch (XQueryRuntimeException) { throw; }
        catch (Exception ex)
        {
            throw new XQueryRuntimeException("FORG0001",
                $"Cannot cast '{input}' to {typeName}: {ex.Message}");
        }
    }

    /// <summary>
    /// Strict type check for let/for bindings: the value must either be xs:untypedAtomic
    /// (implicit conversion) or already match the target atomic type. Numeric-to-numeric
    /// subtype promotion is permitted per function-conversion rules.
    /// </summary>
    public static void RequireAtomicTypeMatch(object? value, ItemType targetType, string context)
    {
        if (value is null) return;
        if (value is Xdm.XsUntypedAtomic) return;
        bool ok = targetType switch
        {
            ItemType.String or ItemType.AnyAtomicType => value is string,
            ItemType.AnyUri => value is Xdm.XsAnyUri or string,
            ItemType.Integer => value is long or int or short or byte or System.Numerics.BigInteger,
            ItemType.Decimal => value is decimal or long or int or short or byte or System.Numerics.BigInteger,
            ItemType.Double => value is double or long or int or decimal or System.Numerics.BigInteger,
            ItemType.Float => value is float or double or long or int or decimal,
            ItemType.Boolean => value is bool,
            ItemType.Date => value is Xdm.XsDate,
            ItemType.DateTime => value is Xdm.XsDateTime,
            ItemType.Time => value is Xdm.XsTime,
            ItemType.QName => value is PhoenixmlDb.Core.QName,
            _ => true
        };
        if (!ok)
            throw new XQueryRuntimeException("XPTY0004",
                $"{context}: value of type {value.GetType().Name} does not match declared {targetType}");
    }

    /// <summary>
    /// Strict SequenceType matching for variable declarations (let, declare variable).
    /// XQuery §3.8.1, §3.10.3: NO promotion, NO untypedAtomic casting — only subtype matching.
    /// Raises XPTY0004 on mismatch.
    /// </summary>
    public static void RequireSequenceTypeMatch(object? value, XdmSequenceType declaredType, string context,
        Func<NamespaceId, string?>? namespaceResolver = null)
    {
        var items = value switch
        {
            null => Array.Empty<object?>(),
            object?[] arr => arr,
            _ => new[] { value }
        };

        if (!MatchesType(items, declaredType, namespaceResolver: namespaceResolver))
            throw new XQueryRuntimeException("XPTY0004",
                $"{context}: value does not match declared type {declaredType}" +
                $" (got {DescribeValueType(items)})");
    }

    /// <summary>Describes what a value actually was, for a type-mismatch message.</summary>
    private static string DescribeValueType(IReadOnlyList<object?> items) => items.Count switch
    {
        0 => "empty-sequence()",
        1 => XdmShape.TypeNameOf(items[0]),
        _ => $"a sequence of {items.Count} items",
    };

    /// <summary>
    /// Numeric type promotion: promotes a value to the target item type.
    /// Per XQuery 3.1 §3.1.5.3: xs:float/xs:double promotion from integer/decimal.
    /// Also handles xs:anyURI → xs:string promotion.
    /// </summary>
    public static object? PromoteNumeric(object? value, ItemType target)
    {
        if (value == null) return null;
        return target switch
        {
            ItemType.Double => Convert.ToDouble(value),
            ItemType.Float => Convert.ToSingle(value),
            ItemType.Decimal => Convert.ToDecimal(value),
            ItemType.String when value is Xdm.XsAnyUri uri => uri.Value,
            _ => value
        };
    }

    /// <summary>
    /// Casts <paramref name="value"/> to <paramref name="targetType"/>, translating the CLR
    /// conversion exceptions into the XQuery error codes the spec assigns.
    /// <para>
    /// The conversion primitives underneath (<c>Convert.To*</c>, <c>*.Parse</c>) throw
    /// <see cref="FormatException"/>, <see cref="InvalidCastException"/> and
    /// <see cref="OverflowException"/>. Those escaped the engine verbatim, so
    /// <c>xs:int('abc')</c> reported "The input string 'abc' was not in a correct format" —
    /// not an XQuery error at all, and unmatchable against any expected code. 536 QT3 cases
    /// were failing on raw CLR exceptions, ~300 of them through this method.
    /// </para>
    /// <para>
    /// The wrapper lives here rather than in <c>CastOperator.ExecuteAsync</c> because that is an
    /// <c>async IAsyncEnumerable</c> iterator, and C# forbids <c>yield return</c> inside a
    /// <c>try</c> with a <c>catch</c> — which is the likeliest reason this was never wrapped.
    /// Putting it here also covers the other eight callers.
    /// </para>
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA1508")]
    public static object? CastValue(object? value, ItemType targetType)
    {
        try
        {
            return CastValueCore(value, targetType);
        }
        catch (FormatException ex)
        {
            // The lexical form is invalid for the target type: FORG0001, "invalid value for
            // cast/constructor".
            throw new XQueryRuntimeException("FORG0001",
                $"'{value}' is not a valid lexical value for {targetType}", ex);
        }
        catch (InvalidCastException ex)
        {
            // The SOURCE type has no conversion to the target at all — xs:date to a numeric,
            // say. That is a type error (XPTY0004), a different fault from a bad lexical form,
            // and the distinction is why this is not one blanket catch.
            throw new XQueryRuntimeException("XPTY0004",
                $"Cannot cast {value?.GetType().Name ?? "()"} to {targetType}", ex);
        }
        catch (OverflowException ex)
        {
            // Numeric overflow during conversion: FOCA0002. Range checks that the engine itself
            // performs raise FORG0001 directly and never reach here.
            throw new XQueryRuntimeException("FOCA0002",
                $"Value out of range casting to {targetType}: {ex.Message}", ex);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA1508")]
    private static object? CastValueCore(object? value, ItemType targetType)
    {
        if (value == null)
            return null;

        // xs:error — the empty union type (XSD 1.1): no value can ever be cast to xs:error
        if (targetType == ItemType.Error)
            throw new XQueryRuntimeException("FORG0001",
                "Cannot cast to xs:error — xs:error has no values");

        // Node/function/map/array target types: return the value unchanged (don't atomize)
        if (targetType is ItemType.Node or ItemType.Element or ItemType.Attribute
            or ItemType.Text or ItemType.Document or ItemType.Comment
            or ItemType.ProcessingInstruction or ItemType.Item
            or ItemType.Map or ItemType.Array or ItemType.Function)
            return value;

        // Atomize XDM nodes before casting to atomic types
        value = QueryExecutionContext.Atomize(value);
        if (value == null)
            return null;

        // xs:untypedAtomic is treated as its string value for casting (XPath/XQuery spec §19.1)
        if (value is Xdm.XsUntypedAtomic untypedVal)
            value = untypedVal.Value;

        // XSD string subtypes: unwrap to plain string for casting purposes
        if (value is Xdm.XsTypedString typedStr)
            value = typedStr.Value;

        // Cross-type casting rejection per XQuery 3.1 casting table
        RejectInvalidCast(value, targetType);

        return targetType switch
        {
            ItemType.String => Functions.ConcatFunction.XQueryStringValue(value),
            ItemType.Integer => value switch
            {
                long l => l,
                int i => (long)i,
                BigInteger bi => bi >= long.MinValue && bi <= long.MaxValue ? (object)(long)bi : bi,
                bool b => b ? 1L : 0L,
                double d when d >= long.MinValue && d <= long.MaxValue => (long)d,
                double d => (BigInteger)d,
                decimal m when m >= long.MinValue && m <= long.MaxValue => (long)m,
                decimal m => (BigInteger)m,
                string s => long.TryParse(s, out var r) ? r
                    : BigInteger.TryParse(s, out var bi2) ? (object)bi2
                    : throw new XQueryRuntimeException("FORG0001", $"Cannot cast '{s}' to xs:integer"),
                _ => Convert.ToInt64(value)
            },
            ItemType.Double => value switch
            {
                double d => d,
                string s when s == "INF" || s == "+INF" => double.PositiveInfinity,
                string s when s == "-INF" => double.NegativeInfinity,
                string s when s == "NaN" => double.NaN,
                string s => double.TryParse(s, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var r) ? r
                    : throw new XQueryRuntimeException("FORG0001", $"Cannot cast '{s}' to xs:double"),
                bool b => b ? 1.0 : 0.0,
                BigInteger bi => (double)bi,
                _ => Convert.ToDouble(value)
            },
            ItemType.Float => value switch
            {
                float f => f,
                BigInteger bi => (float)bi,
                string s when s == "INF" || s == "+INF" => float.PositiveInfinity,
                string s when s == "-INF" => float.NegativeInfinity,
                string s when s == "NaN" => float.NaN,
                string s => float.TryParse(s, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var r) ? r
                    : throw new XQueryRuntimeException("FORG0001", $"Cannot cast '{s}' to xs:float"),
                _ => Convert.ToSingle(value)
            },
            ItemType.Decimal => value switch
            {
                decimal m => m,
                BigInteger bi => (decimal)bi,
                string s => decimal.TryParse(s, System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture, out var r) ? r
                    : throw new XQueryRuntimeException("FORG0001", $"Cannot cast '{s}' to xs:decimal"),
                _ => Convert.ToDecimal(value)
            },
            ItemType.Boolean => value switch
            {
                bool b => b,
                string s when s is "true" or "1" => true,
                string s when s is "false" or "0" => false,
                string s => throw new XQueryRuntimeException("FORG0001", $"Cannot cast '{s}' to xs:boolean"),
                long l => l != 0,
                int i => i != 0,
                double d => d != 0 && !double.IsNaN(d),
                decimal m => m != 0,
                _ => QueryExecutionContext.EffectiveBooleanValue(value)
            },
            ItemType.QName => value is PhoenixmlDb.Core.QName ? value : ParseQName(value?.ToString() ?? ""),
            ItemType.AnyUri => value is Xdm.XsAnyUri ? value : new Xdm.XsAnyUri(value?.ToString() ?? ""),
            ItemType.UntypedAtomic => value is Xdm.XsUntypedAtomic ? value : new Xdm.XsUntypedAtomic(Functions.ConcatFunction.XQueryStringValue(value)),
            ItemType.AnyAtomicType => value, // No conversion needed
            // xs:numeric is a union of xs:double/xs:float/xs:decimal (and subtypes).
            // Casting to a union type yields the value typed as the matching member
            // type, so a value already numeric is preserved unchanged — including
            // derived-integer subtype tags (QT3 xs-numeric-013..017). Non-numeric
            // input (boolean, string, untypedAtomic) is cast to xs:double, the first
            // applicable member (xs-numeric-018).
            ItemType.Numeric => value switch
            {
                double or float or decimal or long or int or BigInteger
                    or Xdm.XsTypedInteger => value,
                bool b => b ? 1.0 : 0.0,
                string s when s == "INF" || s == "+INF" => double.PositiveInfinity,
                string s when s == "-INF" => double.NegativeInfinity,
                string s when s == "NaN" => double.NaN,
                string s => double.TryParse(s, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var r) ? r
                    : throw new XQueryRuntimeException("FORG0001", $"Cannot cast '{s}' to xs:numeric"),
                _ => Convert.ToDouble(value)
            },
            ItemType.Duration => value switch
            {
                Xdm.XsDuration d => d,
                TimeSpan ts => new Xdm.XsDuration(0, ts),
                YearMonthDuration ymd => new Xdm.XsDuration(ymd.TotalMonths, TimeSpan.Zero),
                string s => TypeCastHelper.SafeParseDateType(() => Xdm.XsDuration.Parse(s), "xs:duration", s),
                _ => throw new XQueryRuntimeException("XPTY0004", $"Cannot cast {value.GetType().Name} to xs:duration")
            },
            ItemType.YearMonthDuration => value switch
            {
                YearMonthDuration ymd => ymd,
                Xdm.XsDuration dur => new YearMonthDuration(dur.TotalMonths),
                TimeSpan => new YearMonthDuration(0), // dayTimeDuration has 0 months component
                string s => YearMonthDuration.Parse(s),
                _ => throw new XQueryRuntimeException("XPTY0004", $"Cannot cast {value.GetType().Name} to xs:yearMonthDuration")
            },
            ItemType.DayTimeDuration => value switch
            {
                TimeSpan ts => ts,
                Xdm.XsDuration dur => dur.DayTime,
                YearMonthDuration => TimeSpan.Zero, // yearMonthDuration has 0 days/time component
                string s => ParseDayTimeDuration(s),
                _ => throw new XQueryRuntimeException("XPTY0004", $"Cannot cast {value.GetType().Name} to xs:dayTimeDuration")
            },
            ItemType.DateTime => value switch
            {
                Xdm.XsDateTime xdt => xdt,
                DateTimeOffset dto => new Xdm.XsDateTime(dto, true),
                Xdm.XsDate xd => new Xdm.XsDateTime(
                    new DateTimeOffset(xd.Date.ToDateTime(TimeOnly.MinValue), xd.Timezone ?? TimeSpan.Zero),
                    xd.Timezone.HasValue),
                string s => TypeCastHelper.SafeParseDateType(() => Xdm.XsDateTime.Parse(s), "xs:dateTime", s),
                _ => throw new XQueryRuntimeException("XPTY0004", $"Cannot cast {value.GetType().Name} to xs:dateTime")
            },
            ItemType.Date => value switch
            {
                Xdm.XsDate xd => xd,
                Xdm.XsDateTime xdt => new Xdm.XsDate(DateOnly.FromDateTime(xdt.Value.DateTime), xdt.HasTimezone ? xdt.Value.Offset : null) { ExtendedYear = xdt.ExtendedYear },
                DateOnly d => new Xdm.XsDate(d, null),
                string s => TypeCastHelper.SafeParseDateType(() => Xdm.XsDate.Parse(s), "xs:date", s),
                _ => throw new XQueryRuntimeException("XPTY0004", $"Cannot cast {value.GetType().Name} to xs:date")
            },
            ItemType.Time => value switch
            {
                Xdm.XsTime xt => xt,
                Xdm.XsDateTime xdt => new Xdm.XsTime(TimeOnly.FromDateTime(xdt.Value.DateTime), xdt.HasTimezone ? xdt.Value.Offset : null, xdt.FractionalTicks),
                TimeOnly t => new Xdm.XsTime(t, null, (int)(t.Ticks % TimeSpan.TicksPerSecond)),
                string s => TypeCastHelper.SafeParseDateType(() => Xdm.XsTime.Parse(s), "xs:time", s),
                _ => throw new XQueryRuntimeException("XPTY0004", $"Cannot cast {value.GetType().Name} to xs:time")
            },
            ItemType.Base64Binary => value switch
            {
                PhoenixmlDb.Xdm.XdmValue v when v.Type == PhoenixmlDb.Xdm.XdmType.Base64Binary => v,
                PhoenixmlDb.Xdm.XdmValue v when v.Type == PhoenixmlDb.Xdm.XdmType.HexBinary =>
                    PhoenixmlDb.Xdm.XdmValue.Base64Binary((byte[])v.RawValue!),
                string s => PhoenixmlDb.Xdm.XdmValue.Base64Binary(Convert.FromBase64String(s.Trim())),
                _ => throw new XQueryRuntimeException("FORG0001", $"Cannot cast {value.GetType().Name} to xs:base64Binary")
            },
            ItemType.HexBinary => value switch
            {
                PhoenixmlDb.Xdm.XdmValue v when v.Type == PhoenixmlDb.Xdm.XdmType.HexBinary => v,
                PhoenixmlDb.Xdm.XdmValue v when v.Type == PhoenixmlDb.Xdm.XdmType.Base64Binary =>
                    PhoenixmlDb.Xdm.XdmValue.HexBinary((byte[])v.RawValue!),
                string s => PhoenixmlDb.Xdm.XdmValue.HexBinary(Convert.FromHexString(s.Trim())),
                _ => throw new XQueryRuntimeException("FORG0001", $"Cannot cast {value.GetType().Name} to xs:hexBinary")
            },
            ItemType.GYear => value switch
            {
                Xdm.XsGYear g => g,
                Xdm.XsDateTime dt => new Xdm.XsGYear(FormatGYear(dt.EffectiveYear, dt.HasTimezone ? dt.Value.Offset : (TimeSpan?)null)),
                Xdm.XsDate d => new Xdm.XsGYear(FormatGYear(d.EffectiveYear, d.Timezone)),
                string s => ParseGYear(s),
                // XQuery §19.1 permits casting to a gregorian type ONLY from xs:string,
                // xs:untypedAtomic, xs:date, xs:dateTime or the SAME gregorian type. Anything
                // else — a numeric, a boolean, xs:time, a duration, or a DIFFERENT gregorian
                // type — is a type error (XPTY0004). The catch-all below instead stringified the
                // operand and handed the text to the lexical parser, so
                // `xs:time("13:20:00-05:00") cast as xs:gYear` reported
                // "Invalid xs:gYear: '13:20:00-05:00'" (FORG0001): diagnosing a malformed
                // lexical form for a cast that was never legal in the first place. Same
                // fail-open shape as `_ => true` — the default arm accepting what it cannot
                // actually handle. Note the same-type arm is matched above, so listing every
                // gregorian type here is safe.
                int or long or double or float or decimal or System.Numerics.BigInteger
                    or bool or Xdm.XsTypedInteger or Xdm.XsTime or Xdm.XsDuration
                    or TimeSpan or Xdm.YearMonthDuration
                    or Xdm.XsGYearMonth or Xdm.XsGMonthDay or Xdm.XsGDay or Xdm.XsGMonth
                    => throw new XQueryRuntimeException("XPTY0004",
                        $"cast to xs:gYear requires an xs:string, xs:untypedAtomic, "
                        + "xs:date, xs:dateTime or xs:gYear operand"),
                _ => ParseGYear(value.ToString()!.Trim())
            },
            ItemType.GYearMonth => value switch
            {
                Xdm.XsGYearMonth g => g,
                Xdm.XsDateTime dt => new Xdm.XsGYearMonth(FormatGYearMonth(dt.EffectiveYear, dt.Value.Month, dt.HasTimezone ? dt.Value.Offset : (TimeSpan?)null)),
                Xdm.XsDate d => new Xdm.XsGYearMonth(FormatGYearMonth(d.EffectiveYear, d.Date.Month, d.Timezone)),
                string s => ParseGYearMonth(s),
                int or long or double or float or decimal or System.Numerics.BigInteger
                    or bool or Xdm.XsTypedInteger or Xdm.XsTime or Xdm.XsDuration
                    or TimeSpan or Xdm.YearMonthDuration
                    or Xdm.XsGYear or Xdm.XsGMonthDay or Xdm.XsGDay or Xdm.XsGMonth
                    => throw new XQueryRuntimeException("XPTY0004",
                        $"cast to xs:gYearMonth requires an xs:string, xs:untypedAtomic, "
                        + "xs:date, xs:dateTime or xs:gYearMonth operand"),
                _ => ParseGYearMonth(value.ToString()!.Trim())
            },
            ItemType.GMonthDay => value switch
            {
                Xdm.XsGMonthDay g => g,
                Xdm.XsDateTime dt => new Xdm.XsGMonthDay(FormatGMonthDay(dt.Value.Month, dt.Value.Day, dt.HasTimezone ? dt.Value.Offset : (TimeSpan?)null)),
                Xdm.XsDate d => new Xdm.XsGMonthDay(FormatGMonthDay(d.Date.Month, d.Date.Day, d.Timezone)),
                string s => ParseGMonthDay(s),
                int or long or double or float or decimal or System.Numerics.BigInteger
                    or bool or Xdm.XsTypedInteger or Xdm.XsTime or Xdm.XsDuration
                    or TimeSpan or Xdm.YearMonthDuration
                    or Xdm.XsGYear or Xdm.XsGYearMonth or Xdm.XsGDay or Xdm.XsGMonth
                    => throw new XQueryRuntimeException("XPTY0004",
                        $"cast to xs:gMonthDay requires an xs:string, xs:untypedAtomic, "
                        + "xs:date, xs:dateTime or xs:gMonthDay operand"),
                _ => ParseGMonthDay(value.ToString()!.Trim())
            },
            ItemType.GDay => value switch
            {
                Xdm.XsGDay g => g,
                Xdm.XsDateTime dt => new Xdm.XsGDay(FormatGDay(dt.Value.Day, dt.HasTimezone ? dt.Value.Offset : (TimeSpan?)null)),
                Xdm.XsDate d => new Xdm.XsGDay(FormatGDay(d.Date.Day, d.Timezone)),
                string s => ParseGDay(s),
                int or long or double or float or decimal or System.Numerics.BigInteger
                    or bool or Xdm.XsTypedInteger or Xdm.XsTime or Xdm.XsDuration
                    or TimeSpan or Xdm.YearMonthDuration
                    or Xdm.XsGYear or Xdm.XsGYearMonth or Xdm.XsGMonthDay or Xdm.XsGMonth
                    => throw new XQueryRuntimeException("XPTY0004",
                        $"cast to xs:gDay requires an xs:string, xs:untypedAtomic, "
                        + "xs:date, xs:dateTime or xs:gDay operand"),
                _ => ParseGDay(value.ToString()!.Trim())
            },
            ItemType.GMonth => value switch
            {
                Xdm.XsGMonth g => g,
                Xdm.XsDateTime dt => new Xdm.XsGMonth(FormatGMonth(dt.Value.Month, dt.HasTimezone ? dt.Value.Offset : (TimeSpan?)null)),
                Xdm.XsDate d => new Xdm.XsGMonth(FormatGMonth(d.Date.Month, d.Timezone)),
                string s => ParseGMonth(s),
                int or long or double or float or decimal or System.Numerics.BigInteger
                    or bool or Xdm.XsTypedInteger or Xdm.XsTime or Xdm.XsDuration
                    or TimeSpan or Xdm.YearMonthDuration
                    or Xdm.XsGYear or Xdm.XsGYearMonth or Xdm.XsGMonthDay or Xdm.XsGDay
                    => throw new XQueryRuntimeException("XPTY0004",
                        $"cast to xs:gMonth requires an xs:string, xs:untypedAtomic, "
                        + "xs:date, xs:dateTime or xs:gMonth operand"),
                _ => ParseGMonth(value.ToString()!.Trim())
            },
            _ => throw new XQueryRuntimeException("XPTY0004", $"Cannot cast to type {targetType}")
        };
    }

    private static TimeSpan ParseDayTimeDuration(string s)
    {
        var trimmed = s.Trim();
        var check = trimmed.StartsWith('-') ? trimmed[1..] : trimmed;
        if (check.StartsWith('P'))
        {
            var afterP = check[1..];
            var tIdx = afterP.IndexOf('T', StringComparison.Ordinal);
            // The part before 'T' (date part) must only contain digits and 'D' — no 'Y' or 'M'
            var datePart = tIdx >= 0 ? afterP[..tIdx] : afterP;
            if (datePart.Contains('Y', StringComparison.Ordinal) || datePart.Contains('M', StringComparison.Ordinal))
                throw new FormatException($"Invalid dayTimeDuration: contains year/month components");
        }
        return System.Xml.XmlConvert.ToTimeSpan(trimmed);
    }

    public static Xdm.XsTypedString NormalizeStringSubtype(string value, string typeName)
    {
        // XSD whitespace facets: normalizedString = "replace", others = "collapse"
        string normalized = typeName switch
        {
            "normalizedString" => ReplaceWs(value),
            "token" or "language" or "Name" or "NCName" or "NMTOKEN"
                or "ID" or "IDREF" or "ENTITY" => CollapseWs(value),
            _ => value
        };
        // Lexical validation
        bool ok = typeName switch
        {
            "language" => System.Text.RegularExpressions.Regex.IsMatch(normalized, @"^[a-zA-Z]{1,8}(-[a-zA-Z0-9]{1,8})*$"),
            "Name" => IsValidXmlName(normalized),
            "NCName" or "ID" or "IDREF" or "ENTITY" => IsValidNCNameLex(normalized),
            "NMTOKEN" => IsValidNmtoken(normalized),
            _ => true
        };
        if (!ok)
            throw new XQueryRuntimeException("FORG0001", $"'{value}' is not a valid xs:{typeName}");
        return new Xdm.XsTypedString(normalized, typeName);
    }

    private static string ReplaceWs(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s)
            sb.Append(c is '\t' or '\n' or '\r' ? ' ' : c);
        return sb.ToString();
    }

    private static string CollapseWs(string s)
    {
        var replaced = ReplaceWs(s);
        // Collapse consecutive spaces and trim
        var sb = new System.Text.StringBuilder(replaced.Length);
        bool prevSpace = true; // trim leading
        foreach (var c in replaced)
        {
            if (c == ' ')
            {
                if (!prevSpace) { sb.Append(' '); prevSpace = true; }
            }
            else { sb.Append(c); prevSpace = false; }
        }
        // trim trailing
        while (sb.Length > 0 && sb[^1] == ' ') sb.Length--;
        return sb.ToString();
    }

    internal static bool IsStringSubtype(string typeName) => typeName is
        "normalizedString" or "token" or "language" or "NMTOKEN"
        or "Name" or "NCName" or "ID" or "IDREF" or "ENTITY";

    /// <summary>
    /// True when <paramref name="sub"/> is the same as or derived-by-restriction from
    /// <paramref name="super"/> in the XSD string-type hierarchy. The hierarchy:
    /// <list type="bullet">
    ///   <item>string → normalizedString → token → language</item>
    ///   <item>string → normalizedString → token → NMTOKEN</item>
    ///   <item>string → normalizedString → token → Name → NCName → ID/IDREF/ENTITY</item>
    /// </list>
    /// Returns false when sub is at the same depth or shallower (a wider type).
    /// Used by <see cref="IsSequenceTypeSubtypeOf"/> for function return-type
    /// covariance checks.
    /// </summary>
    private static bool IsStringSubtypeOf(string sub, string super)
    {
        if (sub == super) return true;
        // Walk parent links until we find super or hit string.
        var current = sub;
        while (current != null)
        {
            current = StringTypeParent(current);
            if (current == super) return true;
        }
        return false;
    }

    private static string? StringTypeParent(string typeName) => typeName switch
    {
        "normalizedString" => "string",
        "token" => "normalizedString",
        "language" => "token",
        "NMTOKEN" => "token",
        "Name" => "token",
        "NCName" => "Name",
        "ID" => "NCName",
        "IDREF" => "NCName",
        "ENTITY" => "NCName",
        _ => null
    };

    internal static bool IsValidNCNameLex(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        if (!(char.IsLetter(s[0]) || s[0] == '_')) return false;
        for (int i = 1; i < s.Length; i++)
        {
            var c = s[i];
            if (!(char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_')) return false;
        }
        return true;
    }

    private static bool IsValidXmlName(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        if (!(char.IsLetter(s[0]) || s[0] == '_' || s[0] == ':')) return false;
        for (int i = 1; i < s.Length; i++)
        {
            var c = s[i];
            if (!(char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_' || c == ':')) return false;
        }
        return true;
    }

    private static bool IsValidNmtoken(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        foreach (var c in s)
            if (!(char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_' || c == ':')) return false;
        return true;
    }

    public static void ValidateIntegerSubtype(long value, string typeName)
    {
        bool valid = typeName switch
        {
            "long" => true, // xs:long = full long range
            "int" => value >= int.MinValue && value <= int.MaxValue,
            "short" => value >= short.MinValue && value <= short.MaxValue,
            "byte" => value >= sbyte.MinValue && value <= sbyte.MaxValue,
            "unsignedLong" => value >= 0,
            "unsignedInt" => value >= 0 && value <= uint.MaxValue,
            "unsignedShort" => value >= 0 && value <= ushort.MaxValue,
            "unsignedByte" => value >= 0 && value <= byte.MaxValue,
            "positiveInteger" => value > 0,
            "nonNegativeInteger" => value >= 0,
            "negativeInteger" => value < 0,
            "nonPositiveInteger" => value <= 0,
            _ => true
        };
        if (!valid)
            throw new XQueryRuntimeException("FORG0001",
                $"Value {value} is out of range for xs:{typeName}");
    }

    /// <summary>
    /// Validates an out-of-long-range BigInteger value against an xs:integer subtype.
    /// Only xs:integer (unbounded), xs:nonNegativeInteger, xs:nonPositiveInteger,
    /// xs:positiveInteger, xs:negativeInteger, and xs:unsignedLong admit values outside
    /// the long range. All bounded long-or-smaller subtypes reject such values.
    /// </summary>
    public static void ValidateIntegerSubtype(BigInteger value, string typeName)
    {
        // The unbounded xs:integer subtypes — only their sign matters.
        bool valid = typeName switch
        {
            "integer" => true,
            "nonNegativeInteger" => value >= 0,
            "positiveInteger" => value > 0,
            "nonPositiveInteger" => value <= 0,
            "negativeInteger" => value < 0,
            // xs:unsignedLong: 0 ≤ value ≤ 2^64 - 1. BigInteger can exceed long.MaxValue for
            // values in (long.MaxValue, 2^64 - 1].
            "unsignedLong" => value >= 0 && value <= ulong.MaxValue,
            // All other subtypes (long, int, short, byte, unsignedInt, unsignedShort, unsignedByte)
            // are fully bounded within long.Range — any BigInteger outside long.Range is invalid.
            _ => false
        };
        if (!valid)
            throw new XQueryRuntimeException("FORG0001",
                $"Value {value} is out of range for xs:{typeName}");
    }

    private static void RejectInvalidCast(object value, ItemType target)
    {
        // String can cast to anything — no rejection
        if (value is string) return;
        // AnyAtomicType accepts all atomic values
        if (target == ItemType.AnyAtomicType) return;
        // Numeric → non-numeric/non-string targets (except boolean)
        bool isNumeric = value is int or long or double or float or decimal or BigInteger;
        if (isNumeric && target is ItemType.Duration or ItemType.YearMonthDuration
            or ItemType.DayTimeDuration or ItemType.DateTime or ItemType.Date or ItemType.Time
            or ItemType.GYear or ItemType.GYearMonth or ItemType.GMonthDay or ItemType.GDay
            or ItemType.GMonth or ItemType.QName or ItemType.AnyUri
            or ItemType.Base64Binary or ItemType.HexBinary)
            throw new XQueryRuntimeException("XPTY0004", $"Cannot cast {value.GetType().Name} to {target}");
        // Boolean → date/time/duration/binary targets
        if (value is bool && target is ItemType.Duration or ItemType.YearMonthDuration
            or ItemType.DayTimeDuration or ItemType.DateTime or ItemType.Date or ItemType.Time
            or ItemType.GYear or ItemType.GYearMonth or ItemType.GMonthDay or ItemType.GDay
            or ItemType.GMonth or ItemType.QName or ItemType.AnyUri
            or ItemType.Base64Binary or ItemType.HexBinary)
            throw new XQueryRuntimeException("XPTY0004", $"Cannot cast xs:boolean to {target}");
        // Duration → numeric/boolean/QName/anyURI/binary targets
        bool isDuration = value is TimeSpan or Xdm.YearMonthDuration or Xdm.XsDuration;
        if (isDuration && target is ItemType.Integer or ItemType.Double or ItemType.Float
            or ItemType.Decimal or ItemType.Boolean or ItemType.QName or ItemType.AnyUri
            or ItemType.Base64Binary or ItemType.HexBinary
            or ItemType.DateTime or ItemType.Date or ItemType.Time
            or ItemType.GYear or ItemType.GYearMonth or ItemType.GMonthDay or ItemType.GDay or ItemType.GMonth)
            throw new XQueryRuntimeException("XPTY0004", $"Cannot cast duration to {target}");
        // Date/time → numeric/boolean/QName/anyURI/binary/duration targets
        bool isDateTime = value is Xdm.XsDateTime or Xdm.XsDate or Xdm.XsTime
            or DateTimeOffset or DateOnly or TimeOnly;
        if (isDateTime && target is ItemType.Integer or ItemType.Double or ItemType.Float
            or ItemType.Decimal or ItemType.Boolean or ItemType.QName or ItemType.AnyUri
            or ItemType.Base64Binary or ItemType.HexBinary
            or ItemType.Duration or ItemType.YearMonthDuration or ItemType.DayTimeDuration)
            throw new XQueryRuntimeException("XPTY0004", $"Cannot cast date/time to {target}");
        // QName → anything except string/untypedAtomic
        if (value is QName && target is not (ItemType.String or ItemType.UntypedAtomic or ItemType.QName))
            throw new XQueryRuntimeException("XPTY0004", $"Cannot cast xs:QName to {target}");
        // anyURI → only string/untypedAtomic/anyURI
        if (value is Xdm.XsAnyUri && target is not (ItemType.String or ItemType.UntypedAtomic or ItemType.AnyUri))
            throw new XQueryRuntimeException("XPTY0004", $"Cannot cast xs:anyURI to {target}");
        // g-types → only string/untypedAtomic/same-type/date/dateTime
        bool isGType = value is Xdm.XsGYear or Xdm.XsGYearMonth or Xdm.XsGMonthDay or Xdm.XsGDay or Xdm.XsGMonth;
        if (isGType && target is ItemType.Integer or ItemType.Double or ItemType.Float
            or ItemType.Decimal or ItemType.Boolean or ItemType.QName or ItemType.AnyUri
            or ItemType.Base64Binary or ItemType.HexBinary
            or ItemType.Duration or ItemType.YearMonthDuration or ItemType.DayTimeDuration
            or ItemType.DateTime or ItemType.Date or ItemType.Time)
            throw new XQueryRuntimeException("XPTY0004", $"Cannot cast g-type to {target}");
        // base64Binary/hexBinary → only string/untypedAtomic/base64Binary/hexBinary
        bool isBinary = value is PhoenixmlDb.Xdm.XdmValue xv2
            && (xv2.Type == PhoenixmlDb.Xdm.XdmType.Base64Binary || xv2.Type == PhoenixmlDb.Xdm.XdmType.HexBinary);
        if (isBinary && target is not (ItemType.String or ItemType.UntypedAtomic
            or ItemType.Base64Binary or ItemType.HexBinary or ItemType.AnyAtomicType))
            throw new XQueryRuntimeException("XPTY0004", $"Cannot cast binary to {target}");
    }

    private static void ValidateGTypeYear(string s)
    {
        // Extract the year digits and check for overflow
        var start = s.StartsWith('-') ? 1 : 0;
        var end = start;
        while (end < s.Length && char.IsDigit(s[end])) end++;
        var yearStr = s[start..end];
        if (yearStr.Length > 9 || (yearStr.Length >= 5 && !long.TryParse(yearStr, out var y)))
            throw new XQueryRuntimeException("FODT0001", $"Overflow in xs:gYear value: '{s}'");
        // Per XSD 1.1, year 0000 represents 1 BCE and is valid. XSD 1.0 excluded it; we follow
        // XSD 1.1 since that is the XQuery/XPath 3.1+ default. See QT3 cbcl-castable-gYear-002..
    }

    private static void ValidateGTypeMonth(string monthStr)
    {
        if (int.TryParse(monthStr, out var m) && (m < 1 || m > 12))
            throw new XQueryRuntimeException("FORG0001", $"Invalid month: {monthStr}");
    }

    private static void ValidateGTypeDay(string dayStr)
    {
        if (int.TryParse(dayStr, out var d) && (d < 1 || d > 31))
            throw new XQueryRuntimeException("FORG0001", $"Invalid day: {dayStr}");
    }

    private static string FormatTz(DateTimeOffset dto, bool hasTz) =>
        hasTz ? (dto.Offset == TimeSpan.Zero ? "Z" : dto.ToString("zzz", System.Globalization.CultureInfo.InvariantCulture)) : "";

    private static string FormatGYear(long year, TimeSpan? tz)
    {
        var sb = new System.Text.StringBuilder(16);
        if (year < 0) { sb.Append('-'); sb.Append((-year).ToString("D4", System.Globalization.CultureInfo.InvariantCulture)); }
        else sb.Append(year.ToString("D4", System.Globalization.CultureInfo.InvariantCulture));
        Xdm.XsDate.AppendTimezone(sb, tz);
        return sb.ToString();
    }
    private static string FormatGYearMonth(long year, int month, TimeSpan? tz)
    {
        var sb = new System.Text.StringBuilder(20);
        if (year < 0) { sb.Append('-'); sb.Append((-year).ToString("D4", System.Globalization.CultureInfo.InvariantCulture)); }
        else sb.Append(year.ToString("D4", System.Globalization.CultureInfo.InvariantCulture));
        sb.Append('-');
        sb.Append(month.ToString("D2", System.Globalization.CultureInfo.InvariantCulture));
        Xdm.XsDate.AppendTimezone(sb, tz);
        return sb.ToString();
    }
    private static string FormatGMonthDay(int month, int day, TimeSpan? tz)
    { var sb = new System.Text.StringBuilder(16); sb.Append("--"); sb.Append(month.ToString("D2", System.Globalization.CultureInfo.InvariantCulture)); sb.Append('-'); sb.Append(day.ToString("D2", System.Globalization.CultureInfo.InvariantCulture)); Xdm.XsDate.AppendTimezone(sb, tz); return sb.ToString(); }
    private static string FormatGDay(int day, TimeSpan? tz)
    { var sb = new System.Text.StringBuilder(12); sb.Append("---"); sb.Append(day.ToString("D2", System.Globalization.CultureInfo.InvariantCulture)); Xdm.XsDate.AppendTimezone(sb, tz); return sb.ToString(); }
    private static string FormatGMonth(int month, TimeSpan? tz)
    { var sb = new System.Text.StringBuilder(12); sb.Append("--"); sb.Append(month.ToString("D2", System.Globalization.CultureInfo.InvariantCulture)); Xdm.XsDate.AppendTimezone(sb, tz); return sb.ToString(); }

    // gYearMonth: -?YYYY-MM(Z|(+|-)hh:mm)?
    private static Xdm.XsGYearMonth ParseGYearMonth(string s)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(s, @"^-?\d{4,}-\d{2}(Z|[+-]\d{2}:\d{2})?$"))
            throw new XQueryRuntimeException("FORG0001", $"Invalid xs:gYearMonth: '{s}'");
        ValidateGTypeYear(s);
        // Validate month 01-12
        var dashIdx = s.StartsWith('-') ? s.IndexOf('-', 1) : s.IndexOf('-');
        if (dashIdx > 0)
        {
            var monthStr = s.Substring(dashIdx + 1, 2);
            if (int.TryParse(monthStr, out var month) && (month < 1 || month > 12))
                throw new XQueryRuntimeException("FORG0001", $"Invalid month in xs:gYearMonth: '{s}'");
        }
        // XSD 1.1 canonical form: "-0000-MM" → "0000-MM" (year 0 has no sign)
        if (s.StartsWith("-0000-", StringComparison.Ordinal))
            s = s[1..];
        return new Xdm.XsGYearMonth(s);
    }

    // gYear: -?YYYY(Z|(+|-)hh:mm)?
    private static Xdm.XsGYear ParseGYear(string s)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(s, @"^-?\d{4,}(Z|[+-]\d{2}:\d{2})?$"))
            throw new XQueryRuntimeException("FORG0001", $"Invalid xs:gYear: '{s}'");
        // Validate year is in representable range
        ValidateGTypeYear(s);
        // XSD 1.1 canonical form: "-0000(...)" → "0000(...)" (year 0 has no sign)
        if (s.StartsWith("-0000", StringComparison.Ordinal))
            s = s[1..];
        return new Xdm.XsGYear(s);
    }

    // gMonthDay: --MM-DD(Z|(+|-)hh:mm)?
    private static Xdm.XsGMonthDay ParseGMonthDay(string s)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(s, @"^--\d{2}-\d{2}(Z|[+-]\d{2}:\d{2})?$"))
            throw new XQueryRuntimeException("FORG0001", $"Invalid xs:gMonthDay: '{s}'");
        ValidateGTypeMonth(s[2..4]);
        ValidateGTypeDay(s[5..7]);
        return new Xdm.XsGMonthDay(s);
    }

    // gDay: ---DD(Z|(+|-)hh:mm)?
    private static Xdm.XsGDay ParseGDay(string s)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(s, @"^---\d{2}(Z|[+-]\d{2}:\d{2})?$"))
            throw new XQueryRuntimeException("FORG0001", $"Invalid xs:gDay: '{s}'");
        ValidateGTypeDay(s[3..5]);
        return new Xdm.XsGDay(s);
    }

    // gMonth: --MM(Z|(+|-)hh:mm)?
    private static Xdm.XsGMonth ParseGMonth(string s)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(s, @"^--\d{2}(Z|[+-]\d{2}:\d{2})?$"))
            throw new XQueryRuntimeException("FORG0001", $"Invalid xs:gMonth: '{s}'");
        return new Xdm.XsGMonth(s);
    }

    private static QName ParseQName(string s)
    {
        // Per XML Namespaces, a lexical QName is either an NCName or prefix:NCName.
        // The validation here rejects non-QName strings (e.g. "aGVsbG8=") so that
        // `castable as xs:QName` returns false for invalid lexical forms.
        var trimmed = s.Trim();
        if (string.IsNullOrEmpty(trimmed))
            throw new XQueryRuntimeException("FORG0001", "Cannot cast empty string to xs:QName");

        var colonIdx = trimmed.IndexOf(':', StringComparison.Ordinal);
        if (colonIdx > 0)
        {
            var prefix = trimmed[..colonIdx];
            var localName = trimmed[(colonIdx + 1)..];
            if (!IsValidNCNameLex(prefix) || !IsValidNCNameLex(localName))
                throw new XQueryRuntimeException("FORG0001",
                    $"'{s}' is not a valid lexical xs:QName");
            var nsId = new NamespaceId((uint)Math.Abs(prefix.GetHashCode()));
            return new QName(nsId, localName, prefix);
        }
        if (!IsValidNCNameLex(trimmed))
            throw new XQueryRuntimeException("FORG0001",
                $"'{s}' is not a valid lexical xs:QName");
        return new QName(NamespaceId.None, trimmed);
    }

    public static bool MatchesType(IReadOnlyList<object?> items, XdmSequenceType type,
        ISchemaProvider? schemaProvider = null,
        Func<NamespaceId, string?>? namespaceResolver = null)
    {
        // Check occurrence
        var count = items.Count;
        switch (type.Occurrence)
        {
            case Occurrence.ExactlyOne when count != 1:
                return false;
            case Occurrence.ZeroOrOne when count > 1:
                return false;
            case Occurrence.OneOrMore when count < 1:
                return false;
            case Occurrence.ZeroOrMore:
                break;
        }

        if (type.ItemType == ItemType.Item)
            return true;

        // Check each item matches the type
        foreach (var item in items)
        {
            if (item == null)
            {
                if (type.Occurrence == Occurrence.ExactlyOne || type.Occurrence == Occurrence.OneOrMore)
                    return false;
                continue;
            }

            if (!MatchesItemType(item, type.ItemType))
                return false;

            // Check derived integer subtype. XsTypedInteger values carry a specific
            // subtype tag (set by xs:byte(...), xs:long(...), etc.) — strict hierarchy
            // check via the XSD integer derivation tree.
            //
            // An *untagged* integer value (a literal, the result of xs:integer(...),
            // or the product of integer arithmetic) has dynamic type xs:integer. Per
            // the XDM type hierarchy it is an instance of xs:integer (and its
            // supertypes) only — never of a proper subtype such as xs:byte or
            // xs:nonNegativeInteger, regardless of whether its value happens to fall
            // in that subtype's range. (functx:atomic-type(2) must report "xs:integer",
            // not "xs:byte".) Derived-typed values are always tagged XsTypedInteger by
            // their constructors and handled by the branch above.
            if (type.DerivedIntegerType != null && type.ItemType == ItemType.Integer)
            {
                if (item is Xdm.XsTypedInteger tagged)
                {
                    if (!tagged.IsSubtypeOf(type.DerivedIntegerType))
                        return false;
                }
                else if (!string.Equals(type.DerivedIntegerType, "integer", StringComparison.Ordinal))
                {
                    return false;
                }
            }

            // Check derived string subtype hierarchy (xs:normalizedString, xs:token, xs:NCName, etc.).
            // Use LocalTypeName so xs:NCName (prefixed) and bare NCName (under xpath-default-namespace)
            // both run the subtype check.
            var stringLocalName = type.LocalTypeName ?? type.UnprefixedTypeName;
            if (stringLocalName != null && type.ItemType == ItemType.String
                && IsStringSubtype(stringLocalName))
            {
                if (item is Xdm.XsTypedString ts)
                {
                    if (!ts.IsSubtypeOf(stringLocalName))
                        return false;
                }
                else
                {
                    // Plain string is xs:string — not a subtype of any derived string type
                    return false;
                }
            }

            // Check typed function type: function(ParamTypes) as ReturnType
            // Uses contravariant parameter types and covariant return type
            if (type.FunctionParameterTypes != null && item is XQueryFunction fn)
            {
                if (!MatchesFunctionType(fn, type.FunctionParameterTypes, type.FunctionReturnType))
                    return false;
            }
            // Map checked against function(K) as V: per XPath 3.1 §2.5.4.2
            // A map matches function(K) as V iff arity=1 AND the return type V can accommodate
            // the empty sequence (since looking up a non-existent key returns ()).
            // We also check that all actual values in the map match V.
            else if (type.FunctionParameterTypes != null && item is IDictionary<object, object?> fnMap)
            {
                if (type.FunctionParameterTypes.Count != 1)
                    return false;
                // The return type must allow empty sequence (map may return () for unknown keys)
                if (type.FunctionReturnType != null)
                {
                    var retOcc = type.FunctionReturnType.Occurrence;
                    if (retOcc == Occurrence.ExactlyOne || retOcc == Occurrence.OneOrMore)
                        return false;
                    // Check all actual values match the return type
                    foreach (var kvp in fnMap)
                    {
                        var valItems = NormalizeToList(kvp.Value);
                        if (!MatchesType(valItems, type.FunctionReturnType))
                            return false;
                    }
                }
            }
            // Array checked against function(xs:integer) as V: arity=1, every member matches V
            else if (type.FunctionParameterTypes != null && item is List<object?> fnArr)
            {
                if (type.FunctionParameterTypes.Count != 1)
                    return false;
                // Array is function(xs:integer) as V — param must be compatible with xs:integer
                if (type.FunctionReturnType != null)
                {
                    foreach (var member in fnArr)
                    {
                        var memberItems = NormalizeToList(member);
                        if (!MatchesType(memberItems, type.FunctionReturnType))
                            return false;
                    }
                }
            }

            // Check parameterized map type: map(KeyType, ValueType)
            // Every key must match KeyType and every value must match ValueType
            if (type.MapKeyType != null && item is IDictionary<object, object?> mapDict)
            {
                foreach (var kvp in mapDict)
                {
                    if (!MatchesItemType(kvp.Key, type.MapKeyType.Value))
                        return false;
                    if (type.MapValueSequenceType != null)
                    {
                        var valItems = NormalizeToList(kvp.Value);
                        if (!MatchesType(valItems, type.MapValueSequenceType))
                            return false;
                    }
                    else if (type.MapValueType != null)
                    {
                        // Legacy: simple ItemType-only value check
                        if (kvp.Value == null || !MatchesItemType(kvp.Value, type.MapValueType.Value))
                            return false;
                    }
                }
            }

            // Check parameterized array type: array(MemberType)
            // Every member of the array must match MemberType
            if (type.ArrayMemberType != null && item is List<object?> arrayList)
            {
                foreach (var member in arrayList)
                {
                    var memberItems = NormalizeToList(member);
                    if (!MatchesType(memberItems, type.ArrayMemberType))
                        return false;
                }
            }

            // Check named element constraint: element(name) or element(name, type)
            if (type.ElementName != null && item is Xdm.Nodes.XdmElement elem)
            {
                if (elem.LocalName != type.ElementName)
                    return false;
                // Also check namespace when the element-test carried a prefixed name and the
                // namespace URI was resolved at parse time (e.g. element(P:L) inside a direct
                // element constructor that binds xmlns:P="...").
                if (type.ElementNamespace != null && namespaceResolver != null)
                {
                    var elemNsUri = namespaceResolver(elem.Namespace) ?? "";
                    if (elemNsUri != type.ElementNamespace)
                        return false;
                }
            }

            // Check named attribute constraint: attribute(name) or attribute(name, type)
            if (type.AttributeName != null && item is Xdm.Nodes.XdmAttribute attr2)
            {
                if (attr2.LocalName != type.AttributeName)
                    return false;
                // Namespace check for attribute tests (e.g. attribute(P:name))
                if (type.AttributeNamespace != null && namespaceResolver != null)
                {
                    var attrNsUri = namespaceResolver(attr2.Namespace) ?? "";
                    if (attrNsUri != type.AttributeNamespace)
                        return false;
                }
            }

            // Check schema-element(name). Provider-aware: when supplied, route through
            // MatchesSchemaElement so substitution-group members and elements with schema
            // type annotations are recognized. Local-name fallback otherwise.
            if (type.SchemaElementName != null && item is Xdm.Nodes.XdmElement schemaElem2)
            {
                if (schemaProvider is not null)
                {
                    if (!schemaProvider.MatchesSchemaElement(schemaElem2,
                        type.SchemaElementNamespace ?? "", type.SchemaElementName))
                        return false;
                }
                else if (schemaElem2.LocalName != type.SchemaElementName)
                {
                    return false;
                }
            }

            // Check schema-attribute(name).
            if (type.SchemaAttributeName != null && item is Xdm.Nodes.XdmAttribute schemaAttr2)
            {
                if (schemaProvider is not null)
                {
                    if (!schemaProvider.MatchesSchemaAttribute(schemaAttr2,
                        type.SchemaAttributeNamespace ?? "", type.SchemaAttributeName))
                        return false;
                }
                else if (schemaAttr2.LocalName != type.SchemaAttributeName)
                {
                    return false;
                }
            }

            // Check document-node(element(name)) constraint
            if (type.DocumentElementName != null && item is Xdm.Nodes.XdmDocument doc)
            {
                if (doc.DocumentElementLocalName != type.DocumentElementName)
                    return false;
            }

            // Check type annotation for element(*, type) and attribute(*, type)
            if (type.TypeAnnotation is { } ta)
            {
                if (!MatchesTypeAnnotation(item, ta))
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Checks if a function matches a typed function type using subtype rules:
    /// - Arity must match exactly
    /// - Parameter types are CONTRAVARIANT: function's declared param type must be
    ///   a subtype of (or equal to) the test's param type. This means the function
    ///   accepts at least everything the test type requires.
    /// - Return type is COVARIANT: function's declared return type must be a subtype
    ///   of (or equal to) the test's return type.
    /// </summary>
    public static bool MatchesFunctionType(XQueryFunction fn,
        IReadOnlyList<XdmSequenceType> requiredParamTypes, XdmSequenceType? requiredReturnType)
    {
        // Arity must match
        if (fn.Parameters.Count != requiredParamTypes.Count)
            return false;

        // Check parameter types (contravariant):
        // For each parameter position, the function's declared param type must be
        // a subtype of the required param type. i.e., the required type must be
        // a supertype of the function's type.
        for (int i = 0; i < requiredParamTypes.Count; i++)
        {
            var fnParamType = fn.Parameters[i].Type;
            var reqParamType = requiredParamTypes[i];

            // If the function's param type is the untyped default (item()*), accept:
            // inline function literals without declared parameter types are effectively wildcard.
            if (IsUntypedDefault(fnParamType))
                continue;

            // Contravariant: the required param type must be a subtype of the function's param type
            // (the function must accept everything the caller might pass)
            if (!IsSequenceTypeSubtypeOf(reqParamType, fnParamType))
                return false;
        }

        // Return type (covariant): the function's declared return type must be a subtype
        // of the required return type. Per XPath 3.1 §2.5.4, for instance-of checks,
        // the function's return type must be compatible.
        if (requiredReturnType != null && fn.ReturnType != null)
        {
            if (!IsSequenceTypeSubtypeOf(fn.ReturnType, requiredReturnType))
                return false;
        }

        return true;
    }

    private static bool IsUntypedDefault(XdmSequenceType t)
        => t.ItemType == ItemType.Item && t.Occurrence == Occurrence.ZeroOrMore
           && t.ElementName == null && t.AttributeName == null && t.TypeAnnotation == null
           && t.FunctionParameterTypes == null;

    /// <summary>
    /// Checks if type A is a subtype of type B (A subtype-of B).
    /// A is a subtype of B if every value that matches A also matches B.
    /// </summary>
    private static bool IsSequenceTypeSubtypeOf(XdmSequenceType subType, XdmSequenceType superType)
    {
        // Check occurrence compatibility: sub's occurrence must be "narrower" than super's
        if (!IsOccurrenceSubtypeOf(subType.Occurrence, superType.Occurrence))
            return false;

        // Check item type
        if (!IsItemTypeSubtypeOf(subType.ItemType, superType.ItemType))
            return false;

        // For element types: check name and type annotation constraints
        if (superType.ItemType == ItemType.Element)
        {
            // element(name) vs element(*): sub must have same or more specific name
            if (superType.ElementName != null && subType.ElementName != superType.ElementName)
                return false;
            // element(*, type) or element(name, type): sub must have a type annotation that derives-from
            // If super has a type annotation but sub doesn't, sub is NOT a subtype
            // (sub could match elements of any type, super requires specific type)
            if (superType.TypeAnnotation != null && subType.TypeAnnotation == null)
                return false;
        }

        // For attribute types: similar name/type annotation constraints
        if (superType.ItemType == ItemType.Attribute)
        {
            if (superType.AttributeName != null && subType.AttributeName != superType.AttributeName)
                return false;
            if (superType.TypeAnnotation != null && subType.TypeAnnotation == null)
                return false;
        }

        // String-subtype hierarchy: when both are strings but super has a derived
        // local name (xs:NCName, xs:ID, xs:token, etc.), sub must declare the same
        // or a more-specific subtype. Plain xs:string is NOT a subtype of any
        // derived string type. Required for QT3 instanceof128:
        // `name#1 instance of function(element(A)) as xs:NCName` must be false
        // because fn:name returns xs:string, not xs:NCName.
        if (superType.ItemType == ItemType.String)
        {
            var superStringName = superType.LocalTypeName ?? superType.UnprefixedTypeName;
            if (superStringName != null && IsStringSubtype(superStringName))
            {
                var subStringName = subType.LocalTypeName ?? subType.UnprefixedTypeName;
                if (subStringName == null || !IsStringSubtypeOf(subStringName, superStringName))
                    return false;
            }
        }

        // For parameterized map types: map(K1,V1) subtype-of map(K2,V2) iff K1 subtype-of K2 AND V1 subtype-of V2
        if (superType.ItemType == ItemType.Map && superType.MapKeyType != null)
        {
            // sub is map(*) (no key type) — not a subtype of map(K,V)
            if (subType.MapKeyType == null)
                return false;
            if (!IsItemTypeSubtypeOf(subType.MapKeyType.Value, superType.MapKeyType.Value))
                return false;
            if (superType.MapValueSequenceType != null)
            {
                if (subType.MapValueSequenceType == null)
                    return false;
                if (!IsSequenceTypeSubtypeOf(subType.MapValueSequenceType, superType.MapValueSequenceType))
                    return false;
            }
        }

        // For parameterized array types: array(T1) subtype-of array(T2) iff T1 subtype-of T2
        if (superType.ItemType == ItemType.Array && superType.ArrayMemberType != null)
        {
            if (subType.ArrayMemberType == null)
                return false;
            if (!IsSequenceTypeSubtypeOf(subType.ArrayMemberType, superType.ArrayMemberType))
                return false;
        }

        // For typed function types: function(P1) as R1 subtype-of function(P2) as R2
        // iff P2 subtype-of P1 (contravariant) AND R1 subtype-of R2 (covariant)
        if (superType.FunctionParameterTypes != null)
        {
            // Handle map(K,V) subtype-of function(P) as R:
            // map(K,V) is equivalent to function(K) as V? for subtype purposes.
            // Additionally, per XQuery 3.1 §17.1.2, every map IS-A function(xs:anyAtomicType) as item()*
            // (the "universal map function type") regardless of its specific K and V.
            if (subType.FunctionParameterTypes == null && subType.ItemType == ItemType.Map
                && superType.FunctionParameterTypes.Count == 1)
            {
                // Check the universal map function type shortcut first:
                // function(xs:anyAtomicType) as item()* is a supertype of every map.
                var superParam = superType.FunctionParameterTypes[0];
                bool superParamIsAnyAtomic = superParam.ItemType == ItemType.AnyAtomicType
                    && superParam.Occurrence == Occurrence.ExactlyOne
                    && superParam.FunctionParameterTypes == null;
                bool superReturnIsItemStar = superType.FunctionReturnType == null
                    || (superType.FunctionReturnType.ItemType == ItemType.Item
                        && superType.FunctionReturnType.Occurrence == Occurrence.ZeroOrMore
                        && superType.FunctionReturnType.FunctionParameterTypes == null);
                if (superParamIsAnyAtomic && superReturnIsItemStar)
                    return true; // every map is function(xs:anyAtomicType) as item()*

                // Standard map-to-function subtype: map(K,V) <: function(P) as R
                // iff P <: K (contravariant) AND V? <: R (covariant).
                if (subType.MapKeyType != null)
                {
                    var mapKeySeqType = new XdmSequenceType { ItemType = subType.MapKeyType.Value, Occurrence = Occurrence.ExactlyOne };
                    if (!IsSequenceTypeSubtypeOf(superType.FunctionParameterTypes[0], mapKeySeqType))
                        return false;
                }
                // Map's value type (as V?) must be a subtype of the required return type
                if (superType.FunctionReturnType != null && subType.MapValueSequenceType != null)
                {
                    // Map values may be empty (key not found), so effective return type is V?
                    var effectiveRetType = subType.MapValueSequenceType.Occurrence == Occurrence.ExactlyOne
                        ? new XdmSequenceType { ItemType = subType.MapValueSequenceType.ItemType, Occurrence = Occurrence.ZeroOrOne,
                            MapKeyType = subType.MapValueSequenceType.MapKeyType, MapValueSequenceType = subType.MapValueSequenceType.MapValueSequenceType,
                            ArrayMemberType = subType.MapValueSequenceType.ArrayMemberType, FunctionParameterTypes = subType.MapValueSequenceType.FunctionParameterTypes,
                            FunctionReturnType = subType.MapValueSequenceType.FunctionReturnType }
                        : subType.MapValueSequenceType;
                    if (!IsSequenceTypeSubtypeOf(effectiveRetType, superType.FunctionReturnType))
                        return false;
                }
            }
            // Handle array(T) subtype-of function(P) as R:
            // array(T) is equivalent to function(xs:integer) as T for subtype purposes
            else if (subType.FunctionParameterTypes == null && subType.ItemType == ItemType.Array
                     && superType.FunctionParameterTypes.Count == 1)
            {
                // Array param is xs:integer; check contravariance
                var arrayParamType = new XdmSequenceType { ItemType = ItemType.Integer, Occurrence = Occurrence.ExactlyOne };
                if (!IsSequenceTypeSubtypeOf(superType.FunctionParameterTypes[0], arrayParamType))
                    return false;
                if (superType.FunctionReturnType != null && subType.ArrayMemberType != null)
                {
                    if (!IsSequenceTypeSubtypeOf(subType.ArrayMemberType, superType.FunctionReturnType))
                        return false;
                }
            }
            else
            {
                if (subType.FunctionParameterTypes == null)
                    return false;
                if (subType.FunctionParameterTypes.Count != superType.FunctionParameterTypes.Count)
                    return false;
                for (int i = 0; i < superType.FunctionParameterTypes.Count; i++)
                {
                    // Contravariant: super's param must be subtype of sub's param
                    if (!IsSequenceTypeSubtypeOf(superType.FunctionParameterTypes[i], subType.FunctionParameterTypes[i]))
                        return false;
                }
                if (superType.FunctionReturnType != null)
                {
                    if (subType.FunctionReturnType == null)
                        return false;
                    if (!IsSequenceTypeSubtypeOf(subType.FunctionReturnType, superType.FunctionReturnType))
                        return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Checks if occurrence A is a sub-occurrence of B.
    /// ExactlyOne is subtype of ZeroOrOne, OneOrMore, ZeroOrMore, etc.
    /// </summary>
    private static bool IsOccurrenceSubtypeOf(Occurrence sub, Occurrence super)
    {
        if (sub == super) return true;
        return super switch
        {
            Occurrence.ZeroOrMore => true, // everything is subtype of *
            Occurrence.ZeroOrOne => sub == Occurrence.ExactlyOne || sub == Occurrence.Zero,
            Occurrence.OneOrMore => sub == Occurrence.ExactlyOne,
            Occurrence.ExactlyOne => false, // only ExactlyOne itself (handled by == above)
            Occurrence.Zero => false, // only Zero itself
            _ => false
        };
    }

    /// <summary>
    /// Checks if item type A is a subtype of item type B in the XDM type hierarchy.
    /// </summary>
    private static bool IsItemTypeSubtypeOf(ItemType sub, ItemType super)
    {
        if (sub == super) return true;
        if (super == ItemType.Item) return true; // item() is the top type

        return super switch
        {
            // xs:decimal supertypes: xs:integer
            ItemType.Decimal => sub is ItemType.Integer,
            // xs:numeric (union of double/float/decimal) supertypes
            ItemType.Numeric => sub is ItemType.Double or ItemType.Float
                or ItemType.Decimal or ItemType.Integer,
            // xs:double supertypes: (numeric promotion, not strict subtyping)
            // xs:anyAtomicType supertypes: all atomic types
            ItemType.AnyAtomicType => sub is ItemType.String or ItemType.Integer or ItemType.Double
                or ItemType.Float or ItemType.Decimal or ItemType.Boolean or ItemType.Date
                or ItemType.DateTime or ItemType.Time or ItemType.Duration or ItemType.YearMonthDuration
                or ItemType.DayTimeDuration or ItemType.QName or ItemType.AnyUri
                or ItemType.UntypedAtomic or ItemType.GYearMonth or ItemType.GYear
                or ItemType.GMonthDay or ItemType.GDay or ItemType.GMonth
                or ItemType.HexBinary or ItemType.Base64Binary or ItemType.Numeric,
            // xs:duration supertypes: yearMonthDuration, dayTimeDuration
            ItemType.Duration => sub is ItemType.YearMonthDuration or ItemType.DayTimeDuration,
            // node() supertypes: all node types
            ItemType.Node => sub is ItemType.Element or ItemType.Attribute or ItemType.Text
                or ItemType.Document or ItemType.Comment or ItemType.ProcessingInstruction,
            // function() supertypes: map(*), array(*)
            ItemType.Function => sub is ItemType.Map or ItemType.Array,
            // xs:string subtypes: (xs:NCName, xs:token, etc. — not tracked in our type system)
            _ => false
        };
    }

    /// <summary>
    /// Checks if an item's type annotation matches the required type, considering XSD type hierarchy.
    /// For non-schema-aware processors:
    /// Normalizes a value (which may be null, a single item, or a sequence) into a list for type checking.
    /// </summary>
    internal static IReadOnlyList<object?> NormalizeToList(object? value)
    {
        if (value == null) return Array.Empty<object?>();
        if (value is IEnumerable<object?> seq && value is not string && value is not Dictionary<object, object?>
            && value is not List<object?> && value is not PhoenixmlDb.Xdm.Nodes.XdmNode
            && value is not PhoenixmlDb.Xdm.TextNodeItem)
            return seq.ToList();
        return new[] { value };
    }

    /// <summary>
    /// - Elements have type annotation xs:untyped
    /// - Attributes have type annotation xs:untypedAtomic
    /// </summary>
    private static bool MatchesTypeAnnotation(object item, Xdm.XdmTypeName requiredType)
    {
        // xs:anyType matches everything (top of type hierarchy)
        if (requiredType == Xdm.XdmTypeName.AnyType)
            return true;

        if (item is Xdm.Nodes.XdmElement elem)
        {
            // Non-schema-aware: element type is xs:untyped
            // Type hierarchy: xs:untyped → xs:anyType
            return elem.TypeAnnotation == requiredType;
        }
        if (item is Xdm.Nodes.XdmAttribute attr)
        {
            // Non-schema-aware: attribute type is xs:untypedAtomic
            // Type hierarchy: xs:untypedAtomic → xs:anyAtomicType → xs:anySimpleType → xs:anyType
            if (attr.TypeAnnotation == requiredType)
                return true;
            return IsSubtypeOf(attr.TypeAnnotation, requiredType);
        }
        return false;
    }

    /// <summary>
    /// Checks if actualType is a subtype of requiredType in the XSD type hierarchy.
    /// </summary>
    private static bool IsSubtypeOf(Xdm.XdmTypeName actualType, Xdm.XdmTypeName requiredType)
    {
        // xs:untypedAtomic → xs:anyAtomicType → xs:anySimpleType → xs:anyType
        if (actualType == Xdm.XdmTypeName.UntypedAtomic)
        {
            return requiredType == Xdm.XdmTypeName.AnyAtomicType
                || requiredType == Xdm.XdmTypeName.AnySimpleType
                || requiredType == Xdm.XdmTypeName.AnyType;
        }
        // xs:untyped → xs:anyType
        if (actualType == Xdm.XdmTypeName.Untyped)
        {
            return requiredType == Xdm.XdmTypeName.AnyType;
        }
        return false;
    }

    public static bool MatchesDerivedIntegerRange(object? item, string derivedType)
    {
        BigInteger val;
        if (item is long l) val = l;
        else if (item is int i) val = i;
        else if (item is BigInteger bi) val = bi;
        else return false;

        return derivedType switch
        {
            "long" => val >= long.MinValue && val <= long.MaxValue,
            "int" => val >= int.MinValue && val <= int.MaxValue,
            "short" => val >= short.MinValue && val <= short.MaxValue,
            "byte" => val >= sbyte.MinValue && val <= sbyte.MaxValue,
            "unsignedLong" => val >= 0 && val <= ulong.MaxValue,
            "unsignedInt" => val >= 0 && val <= uint.MaxValue,
            "unsignedShort" => val >= 0 && val <= ushort.MaxValue,
            "unsignedByte" => val >= 0 && val <= byte.MaxValue,
            "nonNegativeInteger" => val >= 0,
            "positiveInteger" => val > 0,
            "nonPositiveInteger" => val <= 0,
            "negativeInteger" => val < 0,
            _ => true
        };
    }

    public static bool MatchesItemType(object? item, ItemType type)
    {
        if (item == null)
            return false;
        return type switch
        {
            ItemType.Item => true,
            // xs:anyAtomicType excludes all non-atomic items: nodes, functions, maps
            // (any IDictionary, not only the concrete Dictionary — maps are OrderedXdmMap),
            // and arrays (List&lt;object?&gt;). Maps/arrays are function items, never atomic.
            ItemType.AnyAtomicType => item is not PhoenixmlDb.Xdm.Nodes.XdmNode and not PhoenixmlDb.Xdm.TextNodeItem and not XQueryFunction and not IDictionary<object, object?> and not List<object?>,
            // xs:numeric — the union of xs:double, xs:float, xs:decimal and their subtypes (incl. xs:integer).
            ItemType.Numeric => item is double or float or decimal or int or long or BigInteger or Xdm.XsTypedInteger,
            ItemType.String => item is string or Xdm.XsTypedString,
            ItemType.Integer => item is int or long or BigInteger or Xdm.XsTypedInteger,
            ItemType.Double => item is double,
            ItemType.Float => item is float,
            ItemType.Decimal => item is decimal or int or long or BigInteger or Xdm.XsTypedInteger,
            ItemType.Boolean => item is bool,
            ItemType.Date => item is Xdm.XsDate or DateOnly,
            ItemType.DateTime => item is Xdm.XsDateTime or DateTimeOffset,
            ItemType.Time => item is Xdm.XsTime or TimeOnly,
            ItemType.Duration => item is TimeSpan or Xdm.DayTimeDuration or Xdm.YearMonthDuration or Xdm.XsDuration,
            ItemType.YearMonthDuration => item is Xdm.YearMonthDuration,
            ItemType.DayTimeDuration => item is TimeSpan or Xdm.DayTimeDuration,
            ItemType.QName => item is PhoenixmlDb.Core.QName,
            ItemType.AnyUri => item is Xdm.XsAnyUri,
            ItemType.UntypedAtomic => item is Xdm.XsUntypedAtomic,
            ItemType.GYearMonth => item is Xdm.XsGYearMonth,
            ItemType.GYear => item is Xdm.XsGYear,
            ItemType.GMonthDay => item is Xdm.XsGMonthDay,
            ItemType.GDay => item is Xdm.XsGDay,
            ItemType.GMonth => item is Xdm.XsGMonth,
            ItemType.HexBinary => item is Xdm.XdmValue v1 && v1.Type == Xdm.XdmType.HexBinary,
            ItemType.Base64Binary => item is Xdm.XdmValue v2 && v2.Type == Xdm.XdmType.Base64Binary,
            ItemType.Node => item is PhoenixmlDb.Xdm.Nodes.XdmNode or PhoenixmlDb.Xdm.TextNodeItem,
            ItemType.Element => item is PhoenixmlDb.Xdm.Nodes.XdmElement,
            ItemType.Attribute => item is PhoenixmlDb.Xdm.Nodes.XdmAttribute,
            ItemType.Text => item is PhoenixmlDb.Xdm.Nodes.XdmText or PhoenixmlDb.Xdm.TextNodeItem,
            ItemType.Comment => item is PhoenixmlDb.Xdm.Nodes.XdmComment,
            ItemType.Document => item is PhoenixmlDb.Xdm.Nodes.XdmDocument,
            ItemType.Namespace => item is PhoenixmlDb.Xdm.Nodes.XdmNamespace,
            ItemType.ProcessingInstruction => item is PhoenixmlDb.Xdm.Nodes.XdmProcessingInstruction,
            ItemType.Function => item is XQueryFunction or IDictionary<object, object?> or List<object?>,
            ItemType.Map => item is Dictionary<object, object?> or IDictionary<object, object?>,
            ItemType.Array => item is List<object?>,
            ItemType.Record => item is Dictionary<object, object?> or IDictionary<object, object?>,
            ItemType.Enum => item is string,
            ItemType.Union => true, // Union matching done in MatchesSequenceItemType with member types
            ItemType.SchemaElement => item is PhoenixmlDb.Xdm.Nodes.XdmElement, // Full matching via ISchemaProvider
            ItemType.SchemaAttribute => item is PhoenixmlDb.Xdm.Nodes.XdmAttribute, // Full matching via ISchemaProvider
            ItemType.Notation => false, // xs:NOTATION — no atomic value is ever an instance
            ItemType.Error => false, // xs:error — the empty union type; no value is ever an instance
            _ => false
        };
    }

    /// <summary>
    /// Checks if an item matches a full sequence type (including named element/document constraints).
    /// When <paramref name="schemaProvider"/> is supplied, schema-element/schema-attribute names
    /// are matched through the provider — covering substitution groups and type-annotation
    /// subsumption. When null, falls back to local-name-only matching (legacy behavior, also
    /// what shows up at most call sites that don't have provider access in scope).
    /// </summary>
    public static bool MatchesSequenceItemType(object? item, XdmSequenceType seqType,
        ISchemaProvider? schemaProvider = null)
    {
        if (!MatchesItemType(item, seqType.ItemType))
            return false;

        // Check named element constraint: element(name)
        if (seqType.ElementName != null && item is PhoenixmlDb.Xdm.Nodes.XdmElement elem)
        {
            if (elem.LocalName != seqType.ElementName)
                return false;
        }

        // Check schema-element(name). With a provider, route through MatchesSchemaElement so
        // substitution-group members and elements with schema-derived type annotations match.
        // Without a provider, fall back to local-name comparison (best-effort).
        if (seqType.SchemaElementName != null && item is PhoenixmlDb.Xdm.Nodes.XdmElement schemaElem)
        {
            if (schemaProvider is not null)
            {
                if (!schemaProvider.MatchesSchemaElement(schemaElem,
                    seqType.SchemaElementNamespace ?? "", seqType.SchemaElementName))
                    return false;
            }
            else if (schemaElem.LocalName != seqType.SchemaElementName)
            {
                return false;
            }
        }

        // Check schema-attribute(name).
        if (seqType.SchemaAttributeName != null && item is PhoenixmlDb.Xdm.Nodes.XdmAttribute schemaAttr)
        {
            if (schemaProvider is not null)
            {
                if (!schemaProvider.MatchesSchemaAttribute(schemaAttr,
                    seqType.SchemaAttributeNamespace ?? "", seqType.SchemaAttributeName))
                    return false;
            }
            else if (schemaAttr.LocalName != seqType.SchemaAttributeName)
            {
                return false;
            }
        }

        // Check processing-instruction("name") constraint
        if (seqType.PIName != null && item is PhoenixmlDb.Xdm.Nodes.XdmProcessingInstruction pi)
        {
            if (pi.Target != seqType.PIName)
                return false;
        }

        // Check document-node(element(name)) constraint
        if (seqType.DocumentElementName != null && item is not PhoenixmlDb.Xdm.Nodes.XdmDocument)
        {
            return false;
        }

        // XPath 4.0: Check record field constraints
        if (seqType.RecordFields != null && item is IDictionary<object, object?> recordMap)
        {
            foreach (var (fieldName, fieldDef) in seqType.RecordFields)
            {
                var hasField = recordMap.ContainsKey(fieldName);
                if (!hasField && !fieldDef.Optional)
                    return false; // Required field missing
                if (hasField && fieldDef.Type != null)
                {
                    var fieldValue = recordMap[fieldName];
                    if (!MatchesSequenceItemType(fieldValue, fieldDef.Type))
                        return false; // Field type mismatch
                }
            }
            // If not extensible, check that no extra fields exist
            if (!seqType.RecordExtensible)
            {
                foreach (var key in recordMap.Keys)
                {
                    if (key is string keyStr && !seqType.RecordFields.ContainsKey(keyStr))
                        return false; // Extra field not allowed
                }
            }
        }

        // XPath 4.0: Check union type — item must match at least one member type
        if (seqType.UnionTypes != null)
        {
            return seqType.UnionTypes.Any(memberType => MatchesSequenceItemType(item, memberType));
        }

        // XPath 4.0: Check enum value constraint
        if (seqType.EnumValues != null && item is string strVal)
        {
            if (!seqType.EnumValues.Contains(strVal))
                return false; // Value not in enum
        }

        return true;
    }

    /// <summary>
    /// Validate a single argument against a declared parameter type for dynamic function calls.
    /// Raises XPTY0004 if the argument cannot be coerced to the parameter type.
    /// Function coercion rules (XPath 3.0 §3.1.5.1):
    ///   - untypedAtomic → cast to target type
    ///   - anyURI → string promotion
    ///   - numeric promotion: integer→decimal→float→double
    ///   - subtype substitution
    ///   - otherwise → XPTY0004
    /// </summary>
    public static void ValidateDynamicFunctionArg(object? arg, XdmSequenceType paramType, string funcName, int paramIndex)
    {
        if (paramType.ItemType == ItemType.Item || paramType.ItemType == ItemType.AnyAtomicType)
            return; // item() and xs:anyAtomicType accept anything

        // Handle sequences
        if (arg is object?[] arr)
        {
            if (arr.Length == 0 && paramType.Occurrence is Occurrence.ExactlyOne or Occurrence.OneOrMore)
                throw new XQueryRuntimeException("XPTY0004",
                    $"Function {funcName}(): parameter {paramIndex + 1} requires a value, got empty sequence");
            foreach (var item in arr)
                ValidateDynamicFunctionArgItem(item, paramType, funcName, paramIndex);
            return;
        }

        if (arg == null)
        {
            if (paramType.Occurrence is Occurrence.ExactlyOne or Occurrence.OneOrMore)
                throw new XQueryRuntimeException("XPTY0004",
                    $"Function {funcName}(): parameter {paramIndex + 1} requires a value, got empty sequence");
            return;
        }

        ValidateDynamicFunctionArgItem(arg, paramType, funcName, paramIndex);
    }

    private static void ValidateDynamicFunctionArgItem(object? item, XdmSequenceType paramType, string funcName, int paramIndex)
    {
        if (item == null) return;

        // untypedAtomic can be cast to any target type (coercion rule)
        if (item is Xdm.XsUntypedAtomic)
            return;
        if (item is Xdm.XsTypedString ts && ts.TypeName == "untypedAtomic")
            return;

        // Nodes passed to atomic-typed parameters will be atomized by the function call mechanism.
        // The atomized value is untypedAtomic (for untyped elements), which can be coerced to any type.
        // So nodes are always allowed when the target is an atomic type.
        if (item is Xdm.Nodes.XdmNode || item is Xdm.TextNodeItem)
        {
            // If the parameter expects a node or item type, check normally below.
            // If it expects an atomic type, allow it (atomization + untypedAtomic coercion will handle it).
            if (paramType.ItemType is not ItemType.Node and not ItemType.Element and not ItemType.Attribute
                and not ItemType.Text and not ItemType.Comment and not ItemType.ProcessingInstruction
                and not ItemType.Document and not ItemType.Item)
                return;
        }

        // Check if item already matches the declared type
        if (MatchesItemType(item, paramType.ItemType))
            return;

        // Numeric promotion: integer → decimal → float → double
        if (paramType.ItemType == ItemType.Double && (item is int or long or BigInteger or decimal or float))
            return;
        if (paramType.ItemType == ItemType.Float && (item is int or long or BigInteger or decimal))
            return;
        if (paramType.ItemType == ItemType.Decimal && (item is int or long or BigInteger))
            return;

        // anyURI → string promotion
        if (paramType.ItemType == ItemType.String && item is Xdm.XsAnyUri)
            return;
        if (paramType.ItemType == ItemType.String && item is Xdm.XsTypedString uts && uts.TypeName == "anyURI")
            return;

        // Node subtype: element/attribute/text/etc. are subtypes of node
        if (paramType.ItemType == ItemType.Node && item is Xdm.Nodes.XdmNode)
            return;

        // Function types: function items match function(*)
        if (paramType.ItemType == ItemType.Function && item is XQueryFunction)
            return;

        // Map: maps match map(*)
        if (paramType.ItemType == ItemType.Map && item is IDictionary<object, object?>)
            return;

        // Array: arrays match array(*)
        if (paramType.ItemType == ItemType.Array && item is IList<object?>)
            return;

        throw new XQueryRuntimeException("XPTY0004",
            $"Function {funcName}(): argument {paramIndex + 1} of type {item.GetType().Name} does not match required type {paramType}");
    }

    public static bool DeepEquals(object? a, object? b, StringComparison stringComparison = StringComparison.Ordinal,
        INodeProvider? nodeProvider = null)
    {
        if (a == null && b == null)
            return true;
        if (a == null || b == null)
            return false;

        // Unwrap XsTypedString to plain string for comparison purposes
        if (a is Xdm.XsTypedString tsA) a = tsA.Value;
        if (b is Xdm.XsTypedString tsB) b = tsB.Value;

        // Unwrap derived-integer-typed values (xs:int, xs:long, …) to their CLR long so they
        // compare value-wise against plain integers (deep-equal is type-promoting within the
        // numeric type hierarchy, so xs:int 23 is deep-equal to xs:integer 23).
        if (a is Xdm.XsTypedInteger tiA) a = tiA.Value;
        if (b is Xdm.XsTypedInteger tiB) b = tiB.Value;

        // XDM Node deep-equal: compare by node kind, name, and content per XPath spec §15.3.1
        if (a is XdmNode nodeA && b is XdmNode nodeB)
            return DeepEqualsNodes(nodeA, nodeB, stringComparison, nodeProvider);

        // Node vs non-node: never equal
        if (a is XdmNode || b is XdmNode)
            return false;

        // XPath deep-equal requires same primitive type for atomic values.
        // Numeric types (int, long, decimal, double, float) are all comparable.
        // Strings are only comparable with strings. Different primitive types → false.
        var aIsNumeric = a is int or long or double or float or decimal or BigInteger;
        var bIsNumeric = b is int or long or double or float or decimal or BigInteger;

        if (aIsNumeric && bIsNumeric)
        {
            // BigInteger comparison — avoid double precision loss
            if (a is BigInteger || b is BigInteger)
            {
                if (a is double or float || b is double or float)
                {
                    var x = Convert.ToDouble(a);
                    var y = Convert.ToDouble(b);
                    if (double.IsNaN(x) && double.IsNaN(y)) return true;
                    return x == y;
                }
                var abi = a is BigInteger ba ? ba : (BigInteger)Convert.ToInt64(a);
                var bbi = b is BigInteger bb ? bb : (BigInteger)Convert.ToInt64(b);
                return abi == bbi;
            }
            // Per XPath F&O §15.3.1: if either is float (not double), compare as float.
            // If either is double, compare as double. Otherwise compare as decimal.
            bool aIsFloat = a is float;
            bool bIsFloat = b is float;
            bool aIsDouble = a is double;
            bool bIsDouble = b is double;
            if ((aIsFloat || bIsFloat) && !aIsDouble && !bIsDouble)
            {
                var fa = Convert.ToSingle(a);
                var fb = Convert.ToSingle(b);
                if (float.IsNaN(fa) && float.IsNaN(fb)) return true;
                return fa == fb;
            }
            var da = Convert.ToDouble(a);
            var db = Convert.ToDouble(b);
            // deep-equal considers NaN equal to NaN (unlike value comparison)
            if (double.IsNaN(da) && double.IsNaN(db)) return true;
            return da == db;
        }

        if (aIsNumeric != bIsNumeric)
            return false; // One numeric, other not — different primitive types

        // Both are non-numeric: compare by type then value
        if (a is bool && b is bool)
            return (bool)a == (bool)b;
        if (a is bool || b is bool)
            return false;

        // Namespace nodes: equal if same prefix and same URI
        if (a is XdmNamespace nsA && b is XdmNamespace nsB)
            return string.Equals(nsA.Prefix, nsB.Prefix, StringComparison.Ordinal)
                && string.Equals(nsA.Uri, nsB.Uri, stringComparison);
        if (a is XdmNamespace || b is XdmNamespace)
            return false;

        // Array deep-equal: same size + all members deep-equal
        if (a is List<object?> arrA && b is List<object?> arrB)
        {
            if (arrA.Count != arrB.Count) return false;
            for (int i = 0; i < arrA.Count; i++)
                if (!DeepEquals(arrA[i], arrB[i], stringComparison, nodeProvider)) return false;
            return true;
        }
        if (a is List<object?> || b is List<object?>)
            return false; // array vs non-map

        // Sequence deep-equal: array members can be sequences (object?[], string[], etc.)
        // Compare element-by-element. Note: List<object?> is XDM array (handled above).
        if (a is Array seqA && b is Array seqB)
        {
            if (seqA.Length != seqB.Length) return false;
            for (int i = 0; i < seqA.Length; i++)
                if (!DeepEquals(seqA.GetValue(i), seqB.GetValue(i), stringComparison, nodeProvider)) return false;
            return true;
        }
        // One is a sequence, other is not — not equal (unless single-item sequence matches the item)
        if (a is Array singleSeqA && singleSeqA.Length == 1)
            return DeepEquals(singleSeqA.GetValue(0), b, stringComparison, nodeProvider);
        if (b is Array singleSeqB && singleSeqB.Length == 1)
            return DeepEquals(a, singleSeqB.GetValue(0), stringComparison, nodeProvider);

        // Map deep-equal: same keys + same values for each key
        if (a is IDictionary<object, object?> mapA && b is IDictionary<object, object?> mapB)
        {
            if (mapA.Count != mapB.Count) return false;
            foreach (var kv in mapA)
            {
                if (!mapB.TryGetValue(kv.Key, out var bVal)) return false;
                if (!DeepEquals(kv.Value, bVal, stringComparison, nodeProvider)) return false;
            }
            return true;
        }
        if (a is IDictionary<object, object?> || b is IDictionary<object, object?>)
            return false; // map vs non-map

        // xs:anyURI and xs:string are comparable in deep-equal (XPath spec: anyURI is promotable to string)
        if ((a is Xdm.XsAnyUri || a is string) && (b is Xdm.XsAnyUri || b is string))
            return string.Equals(a.ToString(), b.ToString(), stringComparison);

        // xs:untypedAtomic and xs:string are comparable
        if ((a is Xdm.XsUntypedAtomic || a is string) && (b is Xdm.XsUntypedAtomic || b is string))
            return string.Equals(a.ToString(), b.ToString(), stringComparison);

        // Date/time types: different types are not deep-equal (e.g., xs:date vs xs:string)
        if (a.GetType() != b.GetType())
            return false;

        // Date/time value comparison using eq semantics (handles implicit timezone)
        if (a is Xdm.XsDateTime dtA && b is Xdm.XsDateTime dtB)
            return dtA.CompareTo(dtB) == 0;
        if (a is Xdm.XsDate dateA && b is Xdm.XsDate dateB)
            return dateA.CompareTo(dateB) == 0;
        if (a is Xdm.XsTime timeA && b is Xdm.XsTime timeB)
            return timeA.CompareTo(timeB) == 0;
        // Duration value comparison
        if (a is Xdm.XsDuration durA && b is Xdm.XsDuration durB)
            return durA.TotalMonths == durB.TotalMonths && durA.DayTime == durB.DayTime;
        if (a is Xdm.YearMonthDuration ymdA && b is Xdm.YearMonthDuration ymdB)
            return ymdA.TotalMonths == ymdB.TotalMonths;
        if (a is Xdm.DayTimeDuration dtdA && b is Xdm.DayTimeDuration dtdB)
            return dtdA.TotalSeconds == dtdB.TotalSeconds;

        // Two xs:QName values are equal when their namespace URI and local name match; the
        // prefix is not part of the identity. Without this they fell to the ToString() fallback
        // below and were compared by their DEBUG rendering, so the same name spelled two ways —
        // "Q{uri}local" from one carrying an expanded namespace, "prefix:local" from one
        // carrying only a runtime namespace — compared unequal. This is the same rule the eq
        // operator already applies (see the QName arm of the value-comparison path); deep-equal
        // simply never reached it.
        if (a is QName qnameA && b is QName qnameB)
        {
            if (!string.Equals(qnameA.LocalName, qnameB.LocalName, StringComparison.Ordinal))
                return false;
            var uriA = qnameA.ResolvedNamespace;
            var uriB = qnameB.ResolvedNamespace;
            if (uriA != null && uriB != null)
                return string.Equals(uriA, uriB, StringComparison.Ordinal);
            return qnameA.Namespace == qnameB.Namespace;
        }

        return string.Equals(a.ToString(), b.ToString(), stringComparison);
    }

    private static bool DeepEqualsNodes(XdmNode a, XdmNode b, StringComparison stringComparison,
        INodeProvider? nodeProvider)
    {
        // Different node kinds → not equal
        if (a.NodeKind != b.NodeKind)
            return false;

        switch (a)
        {
            case XdmElement elemA when b is XdmElement elemB:
                // Elements: same name (namespace + local name)
                if (elemA.Namespace != elemB.Namespace || elemA.LocalName != elemB.LocalName)
                    return false;
                // Compare attributes (order-independent): same count, each attribute in A has match in B
                var attrsA = GetAttributeNodes(elemA, nodeProvider);
                var attrsB = GetAttributeNodes(elemB, nodeProvider);
                if (attrsA.Count != attrsB.Count)
                    return false;
                foreach (var attrA in attrsA)
                {
                    var matchB = attrsB.FirstOrDefault(ab =>
                        ab.Namespace == attrA.Namespace && ab.LocalName == attrA.LocalName);
                    if (matchB == null || !string.Equals(attrA.Value, matchB.Value, stringComparison))
                        return false;
                }
                // Compare children (order-dependent), skipping PIs and comments per XPath spec
                var childrenA = GetSignificantChildren(elemA, nodeProvider);
                var childrenB = GetSignificantChildren(elemB, nodeProvider);
                if (childrenA.Count != childrenB.Count)
                    return false;
                for (int i = 0; i < childrenA.Count; i++)
                    if (!DeepEqualsNodes(childrenA[i], childrenB[i], stringComparison, nodeProvider))
                        return false;
                return true;

            case XdmDocument docA when b is XdmDocument docB:
                // Documents: compare children, skipping PIs and comments per XPath spec
                var dChildrenA = GetSignificantChildren(docA, nodeProvider);
                var dChildrenB = GetSignificantChildren(docB, nodeProvider);
                if (dChildrenA.Count != dChildrenB.Count)
                    return false;
                for (int i = 0; i < dChildrenA.Count; i++)
                    if (!DeepEqualsNodes(dChildrenA[i], dChildrenB[i], stringComparison, nodeProvider))
                        return false;
                return true;

            case XdmAttribute attrA when b is XdmAttribute attrB:
                // Attributes: same name + same string value
                return attrA.Namespace == attrB.Namespace
                    && attrA.LocalName == attrB.LocalName
                    && string.Equals(attrA.Value, attrB.Value, stringComparison);

            case XdmProcessingInstruction piA when b is XdmProcessingInstruction piB:
                // PIs: same target + same value
                return string.Equals(piA.Target, piB.Target, StringComparison.Ordinal)
                    && string.Equals(piA.Value, piB.Value, stringComparison);

            case XdmText textA when b is XdmText textB:
                return string.Equals(textA.Value, textB.Value, stringComparison);

            case XdmComment commentA when b is XdmComment commentB:
                return string.Equals(commentA.Value, commentB.Value, stringComparison);

            default:
                // Same kind, compare string values
                return string.Equals(a.StringValue, b.StringValue, stringComparison);
        }
    }

    private static List<XdmAttribute> GetAttributeNodes(XdmElement elem, INodeProvider? nodeProvider)
    {
        var attrs = new List<XdmAttribute>();
        foreach (var attrId in elem.Attributes)
        {
            if (nodeProvider?.GetNode(attrId) is XdmAttribute attr)
                attrs.Add(attr);
        }
        return attrs;
    }

    /// <summary>
    /// Returns element/text children only, filtering out PIs and comments
    /// as required by the XPath deep-equal specification.
    /// </summary>
    private static List<XdmNode> GetSignificantChildren(XdmNode node, INodeProvider? nodeProvider)
    {
        var children = GetChildNodes(node, nodeProvider);
        children.RemoveAll(c => c is XdmProcessingInstruction or XdmComment);
        return children;
    }

    private static List<XdmNode> GetChildNodes(XdmNode node, INodeProvider? nodeProvider)
    {
        var children = new List<XdmNode>();
        var childIds = node switch
        {
            XdmElement elem => elem.Children,
            XdmDocument doc => doc.Children,
            _ => System.Collections.Immutable.ImmutableArray<NodeId>.Empty
        };
        foreach (var childId in childIds)
        {
            var child = nodeProvider?.GetNode(childId);
            if (child != null)
                children.Add(child);
        }
        return children;
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// XQuery Update Facility — Physical Operators
//
// Status: LIVE. Update operators collect PUL entries during evaluation.
// The PendingUpdateList on QueryExecutionContext accumulates primitives,
// and TransformOperator applies them via deep-copy + PendingUpdateApplicator.
// ═══════════════════════════════════════════════════════════════════════════
