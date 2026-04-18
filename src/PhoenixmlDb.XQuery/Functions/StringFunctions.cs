using System.Collections.Concurrent;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// Cache for compiled regex patterns used by fn:matches, fn:tokenize, fn:replace, fn:analyze-string.
/// Avoids creating new Regex objects on every call for the same pattern.
/// </summary>
internal static class RegexCache
{
    private static readonly ConcurrentDictionary<string, System.Text.RegularExpressions.Regex> _cache = new();

    public static System.Text.RegularExpressions.Regex GetOrCreate(string pattern, string? flags = null)
    {
        var cacheKey = flags != null ? $"{pattern}\x00{flags}" : pattern;
        return _cache.GetOrAdd(cacheKey, _ =>
        {
            XQueryRegexHelper.ValidateXsdRegex(pattern);
            var netPattern = XQueryRegexHelper.ConvertXPathPatternToNet(pattern);
            netPattern = XQueryRegexHelper.ConvertXsdEscapesToNet(netPattern);
            netPattern = XQueryRegexHelper.FixDollarAnchor(netPattern);
            var options = System.Text.RegularExpressions.RegexOptions.None;
            if (flags != null)
                options = XQueryRegexHelper.ParseFlags(flags);
            bool isSingleLine = flags?.Contains('s', StringComparison.Ordinal) == true;
            netPattern = XQueryRegexHelper.FixDotForSurrogatePairs(netPattern, isSingleLine);
            return new System.Text.RegularExpressions.Regex(netPattern, options);
        });
    }
}

/// <summary>
/// fn:string-length($arg as xs:string?) as xs:integer
/// </summary>
public sealed class StringLengthFunction : XQueryFunction
{
    /// <summary>
    /// Enforces XPTY0004 when the atomized argument isn't xs:string, xs:anyURI,
    /// or xs:untypedAtomic. Numeric, boolean, date/time types are rejected.
    /// </summary>
    internal static void RequireStringLike(object? atomized, string fnName)
    {
        if (atomized is null) return;
        if (atomized is string) return;
        if (atomized is Xdm.XsUntypedAtomic) return;
        if (atomized is Xdm.XsAnyUri) return;
        if (atomized is Xdm.XsTypedString) return;
        throw new Execution.XQueryRuntimeException("XPTY0004",
            $"fn:{fnName} expects xs:string?, got {atomized.GetType().Name}");
    }

    public override QName Name => new(FunctionNamespaces.Fn, "string-length");
    public override XdmSequenceType ReturnType => XdmSequenceType.Integer;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalString }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var atomized = Execution.QueryExecutionContext.Atomize(arguments.Count > 0 ? arguments[0] : null);
        if (atomized is null) return ValueTask.FromResult<object?>((long)0);
        RequireStringLike(atomized, "string-length");
        var arg = ConcatFunction.XQueryStringValue(atomized);
        return ValueTask.FromResult<object?>((long)CountCodepoints(arg));
    }

    /// <summary>
    /// Count Unicode codepoints (scalar values) in a string. This differs from
    /// StringInfo.LengthInTextElements which counts grapheme clusters (merging
    /// CR+LF into one element), and from String.Length which counts UTF-16 code units.
    /// XQuery defines string-length as the number of codepoints.
    /// </summary>
    internal static int CountCodepoints(string s)
    {
        int count = 0;
        for (int i = 0; i < s.Length; i++)
        {
            count++;
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                i++; // surrogate pair = one codepoint, skip low surrogate
        }
        return count;
    }
}

/// <summary>
/// fn:string-length() as xs:integer — zero-arity version uses context item
/// </summary>
public sealed class StringLength0Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "string-length");
    public override XdmSequenceType ReturnType => XdmSequenceType.Integer;
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        object? item = null;
        if (context is Execution.QueryExecutionContext qec)
            item = qec.ContextItem;
        if (item is null)
            throw new Execution.XQueryRuntimeException("XPDY0002", "Context item is absent");
        var atomized = Execution.QueryExecutionContext.Atomize(item);
        var str = ConcatFunction.XQueryStringValue(atomized);
        return ValueTask.FromResult<object?>((long)StringLengthFunction.CountCodepoints(str));
    }
}

/// <summary>
/// fn:substring($sourceString, $start) as xs:string
/// </summary>
public sealed class SubstringFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "substring");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "sourceString"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "start"), Type = XdmSequenceType.Double }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var source = QueryExecutionContext.ToString(arguments[0]);
        var start = QueryExecutionContext.ToDouble(arguments[1]);

        // XPath rounds .5 towards positive infinity (not banker's rounding)
        var roundedStart = Math.Round(start, MidpointRounding.AwayFromZero);
        if (double.IsNaN(roundedStart) || double.IsPositiveInfinity(roundedStart))
            return ValueTask.FromResult<object?>("");

        int startPos = double.IsNegativeInfinity(roundedStart) ? 1 : (int)roundedStart;
        int clampedStart = Math.Max(1, startPos);

        // XPath substring uses codepoint-based positions, not UTF-16 char positions.
        return ValueTask.FromResult<object?>(SubstringHelper.SubstringByCodepoints(source, clampedStart, int.MaxValue));
    }
}

/// <summary>
/// fn:substring($sourceString, $start, $length) as xs:string
/// </summary>
public sealed class Substring3Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "substring");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "sourceString"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "start"), Type = XdmSequenceType.Double },
        new() { Name = new QName(NamespaceId.None, "length"), Type = XdmSequenceType.Double }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var source = QueryExecutionContext.ToString(arguments[0]);
        var start = QueryExecutionContext.ToDouble(arguments[1]);
        var length = QueryExecutionContext.ToDouble(arguments[2]);

        // XPath rounds .5 towards positive infinity (not banker's rounding)
        var roundedStart = Math.Round(start, MidpointRounding.AwayFromZero);
        var roundedLength = Math.Round(length, MidpointRounding.AwayFromZero);

        // Per XPath spec: return codepoints at 1-based position p where
        // round(start) <= p < round(start) + round(length)
        // NaN comparisons are always false, so NaN naturally produces ""
        if (double.IsNaN(roundedStart) || double.IsNaN(roundedLength) ||
            double.IsNaN(roundedStart + roundedLength))
            return ValueTask.FromResult<object?>("");

        int startPos = double.IsNegativeInfinity(roundedStart) ? int.MinValue : (int)roundedStart;
        double endPosD = roundedStart + roundedLength;
        int endPos = double.IsPositiveInfinity(endPosD) ? int.MaxValue :
                     double.IsNegativeInfinity(endPosD) ? int.MinValue : (int)endPosD;

        int clampedStart = Math.Max(1, startPos);
        int clampedLength = endPos > int.MaxValue - clampedStart ? int.MaxValue : Math.Max(0, endPos - clampedStart);

        // XPath substring uses codepoint-based positions, not UTF-16 char positions.
        return ValueTask.FromResult<object?>(SubstringHelper.SubstringByCodepoints(source, clampedStart, clampedLength));
    }
}

/// <summary>
/// Helper for codepoint-based substring operations.
/// XPath substring uses Unicode codepoints, not UTF-16 code units.
/// Supplementary characters (U+10000+) are single codepoints but two UTF-16 chars.
/// </summary>
internal static class SubstringHelper
{
    /// <summary>
    /// Extracts a substring using 1-based codepoint positions.
    /// </summary>
    internal static string SubstringByCodepoints(string source, int start, int length)
    {
        if (string.IsNullOrEmpty(source) || length <= 0)
            return "";

        int cpIndex = 1; // 1-based codepoint position
        int charStart = -1;
        int charEnd = -1;
        int cpCount = 0;

        for (int i = 0; i < source.Length; )
        {
            if (cpIndex == start)
                charStart = i;

            // Advance past this codepoint (1 or 2 UTF-16 chars)
            if (char.IsHighSurrogate(source[i]) && i + 1 < source.Length && char.IsLowSurrogate(source[i + 1]))
                i += 2;
            else
                i += 1;

            cpIndex++;

            if (charStart >= 0)
            {
                cpCount++;
                if (cpCount >= length)
                {
                    charEnd = i;
                    break;
                }
            }
        }

        if (charStart < 0)
            return "";
        if (charEnd < 0)
            charEnd = source.Length; // take rest of string

        return source[charStart..charEnd];
    }
}

/// <summary>
/// fn:concat($arg1, $arg2, ...) as xs:string
/// </summary>
public sealed class ConcatFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "concat");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override bool IsVariadic => true;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "arg1"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "arg2"), Type = XdmSequenceType.ZeroOrMoreItems }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        // Each argument must be a single atomic value, not a sequence (XPTY0004)
        foreach (var arg in arguments)
        {
            if (arg is object?[] seq && seq.Length > 1)
                throw new Execution.XQueryRuntimeException("XPTY0004",
                    "Each argument to fn:concat must be a single atomic value, not a sequence");
            if (arg is List<object?> list && list.Count > 1)
                throw new Execution.XQueryRuntimeException("XPTY0004",
                    "Each argument to fn:concat must be a single atomic value, not a sequence");
        }
        var result = string.Concat(arguments.Select(XQueryStringValue));
        return ValueTask.FromResult<object?>(result);
    }

    /// <summary>
    /// Converts a value to its XQuery string representation.
    /// </summary>
    public static string XQueryStringValue(object? value) => XQueryStringValue(value, null);

    public static string XQueryStringValue(object? value, INodeProvider? nodeProvider)
    {
        if (value is null) return "";
        if (value is decimal dec)
            return FormatDecimalXPath(dec);
        if (value is double d)
            return FormatDoubleXPath(d);
        if (value is float f)
            return FormatFloatXPath(f);
        if (value is bool b) return b ? "true" : "false";
        if (value is Xdm.XsDateTime xdt) return xdt.ToString();
        if (value is Xdm.XsDate xd) return xd.ToString();
        if (value is Xdm.XsTime xt) return xt.ToString();
        if (value is DateTimeOffset dto) return dto.ToString("yyyy-MM-ddTHH:mm:ssK", System.Globalization.CultureInfo.InvariantCulture);
        if (value is DateOnly dateOnly) return dateOnly.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        if (value is TimeOnly timeOnly) return timeOnly.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        if (value is TimeSpan ts) return System.Xml.XmlConvert.ToString(ts);
        if (value is Xdm.XsDuration dur) return dur.ToString();
        if (value is PhoenixmlDb.Xdm.YearMonthDuration ymd) return ymd.ToString();
        if (value is PhoenixmlDb.Xdm.XdmValue xv) return xv.AsString();
        if (value is Xdm.Nodes.XdmElement elem && nodeProvider != null)
            return Execution.QueryExecutionContext.ComputeElementStringValue(elem, nodeProvider);
        if (value is Xdm.Nodes.XdmDocument doc && nodeProvider != null)
            return Execution.QueryExecutionContext.ComputeDocumentStringValue(doc, nodeProvider);
        if (value is Xdm.Nodes.XdmNode node) return node.StringValue;
        if (value is PhoenixmlDb.XQuery.Ast.XQueryFunction)
            throw new XQueryException("FOTY0014", "The string value of a function item is not defined");
        if (value is IDictionary<object, object?>)
            throw new XQueryException("FOTY0014", "The string value of a map is not defined");
        if (value is object?[] arr) return string.Join(" ", arr.Select(XQueryStringValue));
        if (value is IEnumerable<object?> seq) return string.Join(" ", seq.Select(XQueryStringValue));
        return value.ToString() ?? "";
    }

    /// <summary>
    /// Formats a decimal per XPath canonical rules:
    /// No trailing fractional zeros, no decimal point if integer,
    /// negative zero maps to "0".
    /// </summary>
    public static string FormatDecimalXPath(decimal dec)
    {
        // Negative zero → "0"
        if (dec == 0m) return "0";

        // C# decimal preserves scale (trailing zeros), so we must trim them.
        var s = dec.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (s.Contains('.'))
        {
            s = s.TrimEnd('0').TrimEnd('.');
        }

        return s;
    }


    /// <summary>
    /// Formats a double per XPath canonical rules (XPath 3.1 §4.2):
    /// fixed-point for |value| in [1E-6, 1E6), scientific notation otherwise.
    /// </summary>
    public static string FormatDoubleXPath(double d)
    {
        if (double.IsNaN(d)) return "NaN";
        if (double.IsPositiveInfinity(d)) return "INF";
        if (double.IsNegativeInfinity(d)) return "-INF";
        if (d == 0.0) return double.IsNegative(d) ? "-0" : "0";

        var abs = Math.Abs(d);
        if (abs >= 1e-6 && abs < 1e6)
        {
            if (d == Math.Floor(d) && abs < 1e15)
                return ((long)d).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var s = d.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            if (s.Contains('E', StringComparison.Ordinal) || s.Contains('e', StringComparison.Ordinal))
            {
                // ToString("R") gave scientific notation for a value in fixed-point range.
                // Use fixed-point format with enough digits.
                s = d.ToString("0.###################", System.Globalization.CultureInfo.InvariantCulture);
            }
            if (s.Contains('.', StringComparison.Ordinal))
            {
                s = s.TrimEnd('0');
                if (s[^1] == '.') s += "0";
            }
            return s;
        }

        // Scientific notation for |d| < 1E-6 or |d| >= 1E6.
        // Use ToString("R") to get the shortest round-trip representation,
        // then reformat to XPath canonical form (no '+' in exponent, no trailing zeros).
        var rt = d.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        var eIdx = rt.IndexOf('E');
        if (eIdx < 0) eIdx = rt.IndexOf('e');
        if (eIdx >= 0)
        {
            // Already in scientific notation - reformat
            var mantissaStr = rt[..eIdx];
            var expStr = rt[(eIdx + 1)..];
            // Remove leading '+' from exponent
            if (expStr.StartsWith('+')) expStr = expStr[1..];
            // Remove leading zeros from exponent
            var expNeg = expStr.StartsWith('-');
            if (expNeg) expStr = expStr[1..];
            expStr = expStr.TrimStart('0');
            if (expStr.Length == 0) expStr = "0";
            if (expNeg) expStr = "-" + expStr;
            // Clean up mantissa trailing zeros and ensure decimal point
            if (mantissaStr.Contains('.', StringComparison.Ordinal))
            {
                mantissaStr = mantissaStr.TrimEnd('0');
                if (mantissaStr[^1] == '.') mantissaStr += "0";
            }
            else
            {
                // XPath requires mantissa to always have a decimal point in scientific notation
                mantissaStr += ".0";
            }
            return $"{mantissaStr}E{expStr}";
        }
        // Fallback: compute mantissa/exponent manually
        int exp = (int)Math.Floor(Math.Log10(abs));
        double mantissa = d / Math.Pow(10, exp);
        var mStr = mantissa.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        if (mStr.Contains('.', StringComparison.Ordinal))
        {
            mStr = mStr.TrimEnd('0');
            if (mStr[^1] == '.') mStr += "0";
        }
        else
        {
            mStr += ".0";
        }
        return $"{mStr}E{exp}";
    }

    /// <summary>
    /// Formats a float per XPath canonical rules, preserving float (single) precision.
    /// </summary>
    public static string FormatFloatXPath(float f)
    {
        if (float.IsNaN(f)) return "NaN";
        if (float.IsPositiveInfinity(f)) return "INF";
        if (float.IsNegativeInfinity(f)) return "-INF";
        if (f == 0.0f) return float.IsNegative(f) ? "-0" : "0";

        var abs = MathF.Abs(f);
        if (abs >= 1e-6f && abs < 1e6f)
        {
            if (f == MathF.Floor(f) && abs < 1e7f)
                return ((int)f).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var s = f.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            if (s.Contains('E', StringComparison.Ordinal) || s.Contains('e', StringComparison.Ordinal))
            {
                // ToString("R") gave scientific notation for a value in fixed-point range.
                s = f.ToString("0.#########", System.Globalization.CultureInfo.InvariantCulture);
            }
            if (s.Contains('.', StringComparison.Ordinal))
            {
                s = s.TrimEnd('0');
                if (s[^1] == '.') s += "0";
            }
            return s;
        }

        // Scientific notation for values outside [1E-6, 1E6)
        // .NET's "R" format may still produce fixed-point for integer floats;
        // force scientific notation for proper XPath output
        var raw = f.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        if (!raw.Contains('E') && !raw.Contains('e'))
        {
            // Manually construct scientific notation
            var exp = (int)MathF.Floor(MathF.Log10(abs));
            var mantissa = f / MathF.Pow(10, exp);
            raw = mantissa.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "E" + exp;
        }
        // .NET produces "3.4028235E+38" — XPath wants "3.4028235E38" (no + in exponent)
        var eIdx = raw.IndexOf('E');
        if (eIdx < 0) eIdx = raw.IndexOf('e');
        if (eIdx >= 0)
        {
            var mantissaStr = raw[..eIdx];
            var expStr = raw[(eIdx + 1)..];
            // Strip leading + from exponent
            if (expStr.StartsWith('+')) expStr = expStr[1..];
            // Remove leading zeros from exponent (preserve sign)
            var expNeg = expStr.StartsWith('-');
            if (expNeg) expStr = expStr[1..];
            expStr = expStr.TrimStart('0');
            if (expStr.Length == 0) expStr = "0";
            if (expNeg) expStr = "-" + expStr;
            // Ensure mantissa has decimal point with at least one digit after it
            if (mantissaStr.Contains('.'))
            {
                mantissaStr = mantissaStr.TrimEnd('0');
                if (mantissaStr[^1] == '.') mantissaStr += "0";
            }
            else
            {
                // Integer mantissa — add .0 per XPath canonical form
                mantissaStr += ".0";
            }
            return $"{mantissaStr}E{expStr}";
        }
        // Fallback: no E notation — shouldn't happen for values outside [1e-6, 1e6)
        return raw;
    }
}

/// <summary>
/// fn:string-join($arg1, $arg2) as xs:string
/// </summary>
public sealed class StringJoinFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "string-join");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "arg1"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "arg2"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        if (arguments[1] is null)
            throw new XQueryException("XPTY0004", "Separator argument to fn:string-join cannot be an empty sequence");
        var items = arguments[0] as IEnumerable<object?> ?? [arguments[0]];
        var separator = ConcatFunction.XQueryStringValue(arguments[1]);
        var result = string.Join(separator, items.Select(ConcatFunction.XQueryStringValue));
        return ValueTask.FromResult<object?>(result);
    }
}

/// <summary>
/// fn:string-join($arg1) as xs:string (1-arg version, separator defaults to "")
/// </summary>
public sealed class StringJoin1Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "string-join");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "arg1"), Type = XdmSequenceType.ZeroOrMoreItems }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var items = arguments[0] as IEnumerable<object?> ?? [arguments[0]];
        var result = string.Join("", items.Select(ConcatFunction.XQueryStringValue));
        return ValueTask.FromResult<object?>(result);
    }
}

/// <summary>
/// fn:contains($arg1, $arg2) as xs:boolean
/// </summary>
public sealed class ContainsFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "contains");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "arg1"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "arg2"), Type = XdmSequenceType.OptionalString }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var atomized0 = Execution.QueryExecutionContext.Atomize(arguments[0]);
        var atomized1 = Execution.QueryExecutionContext.Atomize(arguments[1]);
        StringLengthFunction.RequireStringLike(atomized0, "contains");
        StringLengthFunction.RequireStringLike(atomized1, "contains");
        var str = ConcatFunction.XQueryStringValue(atomized0);
        var search = ConcatFunction.XQueryStringValue(atomized1);
        var comparison = CollationHelper.GetDefaultComparison(context);
        return ValueTask.FromResult<object?>(str.Contains(search, comparison));
    }
}

/// <summary>
/// fn:contains($arg1, $arg2, $collation) as xs:boolean
/// </summary>
public sealed class Contains3Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "contains");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "arg1"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "arg2"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "collation"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var str = ConcatFunction.XQueryStringValue(Execution.QueryExecutionContext.Atomize(arguments[0]));
        var search = ConcatFunction.XQueryStringValue(Execution.QueryExecutionContext.Atomize(arguments[1]));
        var collationUri = arguments[2]?.ToString();
        if (CollationHelper.IsHtmlAsciiCaseInsensitive(collationUri))
            return ValueTask.FromResult<object?>(CollationHelper.AsciiLower(str).Contains(CollationHelper.AsciiLower(search), StringComparison.Ordinal));
        var comparison = CollationHelper.GetStringComparison(collationUri);
        return ValueTask.FromResult<object?>(str.Contains(search, comparison));
    }
}

/// <summary>
/// fn:starts-with($arg1, $arg2) as xs:boolean
/// </summary>
public sealed class StartsWithFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "starts-with");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "arg1"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "arg2"), Type = XdmSequenceType.OptionalString }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var str = ConcatFunction.XQueryStringValue(Execution.QueryExecutionContext.Atomize(arguments[0]));
        var prefix = ConcatFunction.XQueryStringValue(Execution.QueryExecutionContext.Atomize(arguments[1]));
        var comparison = CollationHelper.GetDefaultComparison(context);
        return ValueTask.FromResult<object?>(str.StartsWith(prefix, comparison));
    }
}

/// <summary>
/// fn:ends-with($arg1, $arg2) as xs:boolean
/// </summary>
public sealed class EndsWithFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "ends-with");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "arg1"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "arg2"), Type = XdmSequenceType.OptionalString }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var atomized0 = Execution.QueryExecutionContext.Atomize(arguments[0]);
        var atomized1 = Execution.QueryExecutionContext.Atomize(arguments[1]);
        StringLengthFunction.RequireStringLike(atomized0, "ends-with");
        StringLengthFunction.RequireStringLike(atomized1, "ends-with");
        var str = ConcatFunction.XQueryStringValue(atomized0);
        var suffix = ConcatFunction.XQueryStringValue(atomized1);
        var comparison = CollationHelper.GetDefaultComparison(context);
        return ValueTask.FromResult<object?>(str.EndsWith(suffix, comparison));
    }
}

/// <summary>
/// fn:normalize-space($arg) as xs:string
/// </summary>
public sealed class NormalizeSpaceFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "normalize-space");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalString }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var str = ConcatFunction.XQueryStringValue(arguments[0]);
        var result = string.Join(" ", str.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return ValueTask.FromResult<object?>(result);
    }
}

/// <summary>
/// fn:normalize-space() as xs:string (uses context item)
/// </summary>
public sealed class NormalizeSpace0Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "normalize-space");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        object? item = null;
        if (context is Execution.QueryExecutionContext qec)
            item = qec.ContextItem;
        if (item is null)
            throw new Execution.XQueryRuntimeException("XPDY0002", "Context item is absent");
        var atomized = Execution.QueryExecutionContext.Atomize(item);
        var str = ConcatFunction.XQueryStringValue(atomized);
        var result = string.Join(" ", str.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return ValueTask.FromResult<object?>(result);
    }
}

/// <summary>
/// fn:upper-case($arg) as xs:string
/// </summary>
public sealed class UpperCaseFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "upper-case");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalString }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var atomized = Execution.QueryExecutionContext.Atomize(arguments[0]);
        if (atomized is null) return ValueTask.FromResult<object?>(string.Empty);
        StringLengthFunction.RequireStringLike(atomized, "upper-case");
        var str = ConcatFunction.XQueryStringValue(atomized);
        return ValueTask.FromResult<object?>(FullUnicodeUpperCase(str));
    }

    /// <summary>
    /// Full Unicode upper-case mapping per Unicode SpecialCasing.txt.
    /// .NET's ToUpperInvariant() uses simple (1-to-1) case mapping, but the XQuery spec
    /// requires full case mapping where certain characters expand to multiple codepoints
    /// (e.g. ß → SS, Armenian ligatures → decomposed pairs).
    /// </summary>
    internal static string FullUnicodeUpperCase(string input)
    {
        // Fast path: if no special characters, just use ToUpperInvariant
        if (!ContainsSpecialUpperCaseChar(input))
            return input.ToUpperInvariant();

        var sb = new System.Text.StringBuilder(input.Length + 4);
        for (int i = 0; i < input.Length; i++)
        {
            int cp = char.ConvertToUtf32(input, i);
            if (char.IsHighSurrogate(input[i])) i++;

            if (s_specialUpperCase.TryGetValue(cp, out var replacement))
            {
                foreach (var rcp in replacement)
                    sb.Append(char.ConvertFromUtf32(rcp));
            }
            else
            {
                sb.Append(char.ConvertFromUtf32(cp).ToUpperInvariant());
            }
        }
        return sb.ToString();
    }

    private static bool ContainsSpecialUpperCaseChar(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            // Quick check for the known special casing ranges
            if (c == 0x00DF || c == 0x0149 || c == 0x01F0 ||
                c == 0x0390 || c == 0x03B0 || c == 0x0587 ||
                (c >= 0xFB00 && c <= 0xFB06) || (c >= 0xFB13 && c <= 0xFB17))
                return true;
        }
        return false;
    }

    /// <summary>Unconditional full upper-case mappings from Unicode SpecialCasing.txt</summary>
    private static readonly Dictionary<int, int[]> s_specialUpperCase = new()
    {
        { 0x00DF, [0x0053, 0x0053] },                  // ß → SS
        { 0x0149, [0x02BC, 0x004E] },                  // ŉ → ʼN
        { 0x01F0, [0x004A, 0x030C] },                  // ǰ → J̌
        { 0x0390, [0x0399, 0x0308, 0x0301] },          // ΐ → Ϊ́
        { 0x03B0, [0x03A5, 0x0308, 0x0301] },          // ΰ → Ϋ́
        { 0x0587, [0x0535, 0x0552] },                   // և → ԵՒ
        { 0xFB00, [0x0046, 0x0046] },                   // ﬀ → FF
        { 0xFB01, [0x0046, 0x0049] },                   // ﬁ → FI
        { 0xFB02, [0x0046, 0x004C] },                   // ﬂ → FL
        { 0xFB03, [0x0046, 0x0046, 0x0049] },           // ﬃ → FFI
        { 0xFB04, [0x0046, 0x0046, 0x004C] },           // ﬄ → FFL
        { 0xFB05, [0x0053, 0x0054] },                   // ﬅ → ST
        { 0xFB06, [0x0053, 0x0054] },                   // ﬆ → ST
        { 0xFB13, [0x0544, 0x0546] },                   // ﬓ → ՄՆ
        { 0xFB14, [0x0544, 0x0535] },                   // ﬔ → ՄԵ
        { 0xFB15, [0x0544, 0x053B] },                   // ﬕ → ՄԻ
        { 0xFB16, [0x054E, 0x0546] },                   // ﬖ → ՎՆ
        { 0xFB17, [0x0544, 0x053D] },                   // ﬗ → ՄԽ
    };
}

/// <summary>
/// fn:lower-case($arg) as xs:string
/// </summary>
public sealed class LowerCaseFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "lower-case");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalString }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var str = ConcatFunction.XQueryStringValue(arguments[0]);
        return ValueTask.FromResult<object?>(FullUnicodeLowerCase(str));
    }

    /// <summary>
    /// Full Unicode lower-case mapping per Unicode SpecialCasing.txt.
    /// Handles characters like U+0130 (I with dot above) that expand to multiple codepoints.
    /// </summary>
    internal static string FullUnicodeLowerCase(string input)
    {
        if (!ContainsSpecialLowerCaseChar(input))
            return input.ToLowerInvariant();

        var sb = new System.Text.StringBuilder(input.Length + 4);
        for (int i = 0; i < input.Length; i++)
        {
            int cp = char.ConvertToUtf32(input, i);
            if (char.IsHighSurrogate(input[i])) i++;

            if (s_specialLowerCase.TryGetValue(cp, out var replacement))
            {
                foreach (var rcp in replacement)
                    sb.Append(char.ConvertFromUtf32(rcp));
            }
            else
            {
                sb.Append(char.ConvertFromUtf32(cp).ToLowerInvariant());
            }
        }
        return sb.ToString();
    }

    private static bool ContainsSpecialLowerCaseChar(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == 0x0130) return true;
        }
        return false;
    }

    /// <summary>Unconditional full lower-case mappings from Unicode SpecialCasing.txt</summary>
    private static readonly Dictionary<int, int[]> s_specialLowerCase = new()
    {
        { 0x0130, [0x0069, 0x0307] },  // İ → i + combining dot above
    };
}

/// <summary>
/// fn:normalize-unicode($arg, $normalizationForm) as xs:string
/// </summary>
public sealed class NormalizeUnicode2Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "normalize-unicode");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "normalizationForm"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var str = arguments[0]?.ToString();
        if (str == null) return ValueTask.FromResult<object?>("");
        var formArg = arguments[1];
        if (formArg == null)
            throw new XQueryException("XPTY0004",
                "fn:normalize-unicode: normalization form cannot be an empty sequence");
        var form = (formArg.ToString() ?? "NFC").Trim().ToUpperInvariant();
        if (form.Length == 0)
            return ValueTask.FromResult<object?>(str); // empty string = no normalization
        var normForm = form switch
        {
            "NFC" => System.Text.NormalizationForm.FormC,
            "NFD" => System.Text.NormalizationForm.FormD,
            "NFKC" => System.Text.NormalizationForm.FormKC,
            "NFKD" => System.Text.NormalizationForm.FormKD,
            _ => throw new InvalidOperationException($"FOCH0003: Unsupported normalization form '{form}'")
        };
        return ValueTask.FromResult<object?>(str.Normalize(normForm));
    }
}

/// <summary>
/// fn:translate($arg, $mapString, $transString) as xs:string
/// </summary>
public sealed class TranslateFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "translate");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "mapString"), Type = XdmSequenceType.String },
        new() { Name = new QName(NamespaceId.None, "transString"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        // Type checking: all arguments must be string-like (xs:string, xs:untypedAtomic, xs:anyURI)
        ValidateStringArg(arguments[0], "arg");
        ValidateStringArgRequired(arguments[1], "mapString");
        ValidateStringArgRequired(arguments[2], "transString");

        var str = arguments[0]?.ToString() ?? "";
        var mapString = arguments[1]?.ToString() ?? "";
        var transString = arguments[2]?.ToString() ?? "";

        // Use codepoint-level operations to handle surrogate pairs / non-BMP characters
        var mapCodepoints = ToCodepoints(mapString);
        var transCodepoints = ToCodepoints(transString);

        var sb = new System.Text.StringBuilder(str.Length);
        var enumerator = System.Globalization.StringInfo.GetTextElementEnumerator(str);
        while (enumerator.MoveNext())
        {
            var textElement = enumerator.GetTextElement();
            var codepoint = char.ConvertToUtf32(textElement, 0);
            var index = mapCodepoints.IndexOf(codepoint);
            if (index < 0)
            {
                sb.Append(textElement);
            }
            else if (index < transCodepoints.Count)
            {
                sb.Append(char.ConvertFromUtf32(transCodepoints[index]));
            }
            // else: character is deleted
        }

        return ValueTask.FromResult<object?>(sb.ToString());
    }

    private static void ValidateStringArg(object? arg, string paramName)
    {
        if (arg == null) return;
        // Allow nodes (they get atomized to string) and string-like types
        if (arg is Xdm.Nodes.XdmNode) return;
        if (arg is string) return;
        if (arg is Xdm.XsUntypedAtomic) return;
        if (arg is Xdm.XsAnyUri) return;
        throw new XQueryException("XPTY0004",
            $"fn:translate: argument ${paramName} must be xs:string, got {arg.GetType().Name}");
    }

    private static void ValidateStringArgRequired(object? arg, string paramName)
    {
        if (arg == null)
            throw new XQueryException("XPTY0004",
                $"fn:translate: argument ${paramName} cannot be an empty sequence");
        ValidateStringArg(arg, paramName);
    }

    private static List<int> ToCodepoints(string s)
    {
        var result = new List<int>();
        for (int i = 0; i < s.Length; i++)
        {
            int cp;
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                cp = char.ConvertToUtf32(s[i], s[i + 1]);
                i++;
            }
            else
            {
                cp = s[i];
            }
            result.Add(cp);
        }
        return result;
    }
}

/// <summary>
/// fn:string($arg) as xs:string
/// </summary>
public sealed class StringFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "string");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalItem }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0];

        // fn:string() requires a single item — sequences of 2+ are XPTY0004
        if (arg is object?[] seq && seq.Length > 1)
            throw new Execution.XQueryRuntimeException("XPTY0004",
                "fn:string() requires zero or one item, got a sequence of " + seq.Length + " items");

        // fn:string() is not defined for function items, arrays, or maps — FOTY0014
        if (arg is List<object?>)
            throw new XQueryException("FOTY0014", "The string value of an array is not defined");
        if (arg is IDictionary<object, object?>)
            throw new XQueryException("FOTY0014", "The string value of a map is not defined");
        if (arg is Ast.XQueryFunction)
            throw new XQueryException("FOTY0014", "The string value of a function item is not defined");

        // For element/document nodes, compute string value by walking descendant text nodes
        if (arg is Xdm.Nodes.XdmElement elem2)
        {
            var nodeProvider = (context as Execution.QueryExecutionContext)?.NodeProvider;
            var atomized = Execution.QueryExecutionContext.Atomize(arg, nodeProvider);
            return ValueTask.FromResult<object?>(atomized?.ToString() ?? "");
        }
        if (arg is Xdm.Nodes.XdmDocument)
        {
            var nodeProvider = (context as Execution.QueryExecutionContext)?.NodeProvider;
            var atomized = Execution.QueryExecutionContext.Atomize(arg, nodeProvider);
            return ValueTask.FromResult<object?>(atomized?.ToString() ?? "");
        }
        return ValueTask.FromResult<object?>(ConcatFunction.XQueryStringValue(arg));
    }
}

/// <summary>
/// fn:string() as xs:string (uses context item)
/// </summary>
public sealed class String0Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "string");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        object? item = null;
        if (context is Execution.QueryExecutionContext qec)
            item = qec.ContextItem;
        if (item is null)
            throw new Execution.XQueryRuntimeException("XPDY0002", "Context item is absent");
        // Use XQueryStringValue for proper formatting (e.g., double -INF → "-INF" not "-Infinity")
        var nodeProvider = (context as Execution.QueryExecutionContext)?.NodeProvider;
        return ValueTask.FromResult<object?>(ConcatFunction.XQueryStringValue(item, nodeProvider));
    }
}

/// <summary>
/// fn:tokenize($input, $pattern) as xs:string*
/// </summary>
public sealed class TokenizeFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "tokenize");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.String, Occurrence = Occurrence.ZeroOrMore };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "input"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "pattern"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var input = ConcatFunction.XQueryStringValue(Execution.QueryExecutionContext.Atomize(arguments[0]));
        var pattern = arguments[1]?.ToString() ?? "";

        // Per XPath spec: if $input is the zero-length string, return empty sequence
        if (input.Length == 0)
            return ValueTask.FromResult<object?>(Array.Empty<string>());

        try
        {
            var regex = RegexCache.GetOrCreate(pattern);
            // FORX0003: pattern must not match empty string
            if (regex.IsMatch(""))
                throw new InvalidOperationException("FORX0003: The supplied pattern matches a zero-length string");
            var tokens = TokenizeSplit(regex, input);
            return ValueTask.FromResult<object?>(tokens);
        }
        catch (InvalidOperationException)
        {
            throw; // FORX0003 errors
        }
        catch (ArgumentException ex)
        {
            throw new XQueryException("FORX0002",
                $"Invalid regular expression: {ex.Message}");
        }
    }

    /// <summary>
    /// Split input by regex matches without including captured group values
    /// (unlike .NET's Regex.Split which includes captured groups in results).
    /// </summary>
    internal static string[] TokenizeSplit(System.Text.RegularExpressions.Regex regex, string input)
    {
        var tokens = new List<string>();
        int lastEnd = 0;
        foreach (System.Text.RegularExpressions.Match m in regex.Matches(input))
        {
            tokens.Add(input[lastEnd..m.Index]);
            lastEnd = m.Index + m.Length;
        }
        tokens.Add(input[lastEnd..]);
        return tokens.ToArray();
    }
}

/// <summary>
/// fn:tokenize($input, $pattern, $flags) as xs:string*
/// </summary>
public sealed class Tokenize3Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "tokenize");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.String, Occurrence = Occurrence.ZeroOrMore };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "input"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "pattern"), Type = XdmSequenceType.String },
        new() { Name = new QName(NamespaceId.None, "flags"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var input = ConcatFunction.XQueryStringValue(Execution.QueryExecutionContext.Atomize(arguments[0]));
        var pattern = arguments[1]?.ToString() ?? "";
        var flags = arguments[2]?.ToString() ?? "";

        if (input.Length == 0)
            return ValueTask.FromResult<object?>(Array.Empty<string>());

        try
        {
            var isLiteral = flags.Contains('q', StringComparison.Ordinal);
            // XPath 'x' flag: strip whitespace from pattern before any processing
            if (!isLiteral && flags.Contains('x', StringComparison.Ordinal))
                pattern = XQueryRegexHelper.StripXModeWhitespace(pattern);
            if (!isLiteral)
                XQueryRegexHelper.ValidateXsdRegex(pattern);
            var netPattern = isLiteral
                ? System.Text.RegularExpressions.Regex.Escape(pattern)
                : XQueryRegexHelper.ConvertXPathPatternToNet(pattern);
            netPattern = XQueryRegexHelper.ConvertXsdEscapesToNet(netPattern);
            if (flags.Contains('i', StringComparison.Ordinal))
                netPattern = XQueryRegexHelper.FixPropertyEscapesForCaseInsensitive(netPattern);
            if (!flags.Contains('m', StringComparison.Ordinal))
                netPattern = XQueryRegexHelper.FixDollarAnchor(netPattern);
            else
                netPattern = XQueryRegexHelper.FixCaretAnchorMultiline(netPattern);
            netPattern = XQueryRegexHelper.FixDotForSurrogatePairs(netPattern,
                flags.Contains('s', StringComparison.Ordinal));
            var options = XQueryRegexHelper.ParseFlags(flags);
            var regex = new System.Text.RegularExpressions.Regex(netPattern, options);
            // FORX0003: pattern must not match empty string
            if (regex.IsMatch(""))
                throw new InvalidOperationException("FORX0003: The supplied pattern matches a zero-length string");
            var tokens = TokenizeFunction.TokenizeSplit(regex, input);
            return ValueTask.FromResult<object?>(tokens);
        }
        catch (InvalidOperationException)
        {
            throw; // FORX0003 errors
        }
        catch (ArgumentException ex)
        {
            throw new XQueryException("FORX0002",
                $"Invalid regular expression: {ex.Message}");
        }
    }
}

/// <summary>
/// fn:matches($input, $pattern) as xs:boolean
/// </summary>
public sealed class MatchesFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "matches");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "input"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "pattern"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var input = arguments[0]?.ToString() ?? "";
        if (arguments[1] is null)
            throw new XQueryException("XPTY0004", "Pattern argument to fn:matches cannot be an empty sequence");
        var pattern = arguments[1]!.ToString() ?? "";
        try
        {
            var regex = RegexCache.GetOrCreate(pattern);
            return ValueTask.FromResult<object?>(regex.IsMatch(input));
        }
        catch (System.Text.RegularExpressions.RegexParseException ex)
        {
            throw new XQueryException("FORX0002", $"Invalid regular expression '{pattern}': {ex.Message}");
        }
    }
}

/// <summary>
/// fn:matches($input, $pattern, $flags) as xs:boolean
/// </summary>
public sealed class Matches3Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "matches");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "input"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "pattern"), Type = XdmSequenceType.String },
        new() { Name = new QName(NamespaceId.None, "flags"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var input = arguments[0]?.ToString() ?? "";
        if (arguments[1] is null)
            throw new XQueryException("XPTY0004", "Pattern argument to fn:matches cannot be an empty sequence");
        if (arguments[2] is null)
            throw new XQueryException("XPTY0004", "Flags argument to fn:matches cannot be an empty sequence");
        var pattern = arguments[1]!.ToString() ?? "";
        var flags = arguments[2]!.ToString() ?? "";
        try
        {
            var isLiteral = flags.Contains('q', StringComparison.Ordinal);
            // XPath 'x' flag: strip whitespace from pattern before any processing
            if (!isLiteral && flags.Contains('x', StringComparison.Ordinal))
                pattern = XQueryRegexHelper.StripXModeWhitespace(pattern);
            if (!isLiteral)
                XQueryRegexHelper.ValidateXsdRegex(pattern);
            var netPattern = isLiteral
                ? System.Text.RegularExpressions.Regex.Escape(pattern)
                : XQueryRegexHelper.ConvertXPathPatternToNet(pattern);
            netPattern = XQueryRegexHelper.ConvertXsdEscapesToNet(netPattern);
            if (flags.Contains('i', StringComparison.Ordinal))
                netPattern = XQueryRegexHelper.FixPropertyEscapesForCaseInsensitive(netPattern);
            if (!flags.Contains('m', StringComparison.Ordinal))
                netPattern = XQueryRegexHelper.FixDollarAnchor(netPattern);
            else
                netPattern = XQueryRegexHelper.FixCaretAnchorMultiline(netPattern);
            netPattern = XQueryRegexHelper.FixDotForSurrogatePairs(netPattern,
                flags.Contains('s', StringComparison.Ordinal));
            var options = XQueryRegexHelper.ParseFlags(flags);
            var regex = new System.Text.RegularExpressions.Regex(netPattern, options);
            return ValueTask.FromResult<object?>(regex.IsMatch(input));
        }
        catch (System.Text.RegularExpressions.RegexParseException ex)
        {
            throw new InvalidOperationException($"FORX0002: Invalid regular expression '{pattern}': {ex.Message}", ex);
        }
    }
}

/// <summary>
/// fn:replace($input, $pattern, $replacement) as xs:string
/// </summary>
public sealed class ReplaceFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "replace");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "input"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "pattern"), Type = XdmSequenceType.String },
        new() { Name = new QName(NamespaceId.None, "replacement"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var input = arguments[0]?.ToString() ?? "";
        if (arguments[1] is null)
            throw new XQueryException("XPTY0004", "Pattern argument to fn:replace cannot be an empty sequence");
        if (arguments[2] is null)
            throw new XQueryException("XPTY0004", "Replacement argument to fn:replace cannot be an empty sequence");
        var pattern = arguments[1]!.ToString() ?? "";
        var replacement = arguments[2]!.ToString() ?? "";
        try
        {
            XQueryRegexHelper.ValidateXsdRegex(pattern);
            var netPattern = XQueryRegexHelper.ConvertXPathPatternToNet(pattern);
            netPattern = XQueryRegexHelper.ConvertXsdEscapesToNet(netPattern);
            netPattern = XQueryRegexHelper.FixDollarAnchor(netPattern);
            netPattern = XQueryRegexHelper.FixDotForSurrogatePairs(netPattern);
            var regex = new System.Text.RegularExpressions.Regex(netPattern);
            // FORX0003: pattern must not match empty string
            if (regex.IsMatch(""))
                throw new XQueryException("FORX0003",
                    "Pattern matches a zero-length string in fn:replace");
            var netReplacement = XQueryRegexHelper.ConvertXPathReplacementToNet(replacement, pattern);
            return ValueTask.FromResult<object?>(regex.Replace(input, netReplacement));
        }
        catch (XQueryException) { throw; }
        catch (InvalidOperationException) { throw; }
        catch (ArgumentException ex)
        {
            throw new XQueryException("FORX0002",
                $"Invalid regular expression: {ex.Message}");
        }
    }
}

/// <summary>
/// fn:replace($input, $pattern, $replacement, $flags) as xs:string
/// </summary>
public sealed class Replace4Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "replace");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "input"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "pattern"), Type = XdmSequenceType.String },
        new() { Name = new QName(NamespaceId.None, "replacement"), Type = XdmSequenceType.String },
        new() { Name = new QName(NamespaceId.None, "flags"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var input = arguments[0]?.ToString() ?? "";
        var pattern = arguments[1]?.ToString() ?? "";
        var replacement = arguments[2]?.ToString() ?? "";
        var flags = arguments[3]?.ToString() ?? "";
        try
        {
            var isLiteral = flags.Contains('q', StringComparison.Ordinal);
            // XPath 'x' flag: strip whitespace from pattern before any processing
            if (!isLiteral && flags.Contains('x', StringComparison.Ordinal))
                pattern = XQueryRegexHelper.StripXModeWhitespace(pattern);
            if (!isLiteral)
                XQueryRegexHelper.ValidateXsdRegex(pattern);
            string netReplacement;
            string netPattern;
            if (isLiteral)
            {
                // 'q' flag: pattern is literal, replacement is literal (no $N or \ escapes)
                netPattern = System.Text.RegularExpressions.Regex.Escape(pattern);
                netReplacement = replacement.Replace("$", "$$", StringComparison.Ordinal);
            }
            else
            {
                netPattern = XQueryRegexHelper.ConvertXPathPatternToNet(pattern);
                netReplacement = XQueryRegexHelper.ConvertXPathReplacementToNet(replacement, pattern);
            }
            netPattern = XQueryRegexHelper.ConvertXsdEscapesToNet(netPattern);
            if (flags.Contains('i', StringComparison.Ordinal))
                netPattern = XQueryRegexHelper.FixPropertyEscapesForCaseInsensitive(netPattern);
            if (!flags.Contains('m', StringComparison.Ordinal))
                netPattern = XQueryRegexHelper.FixDollarAnchor(netPattern);
            else
                netPattern = XQueryRegexHelper.FixCaretAnchorMultiline(netPattern);
            netPattern = XQueryRegexHelper.FixDotForSurrogatePairs(netPattern,
                flags.Contains('s', StringComparison.Ordinal));
            var options = XQueryRegexHelper.ParseFlags(flags);
            var regex = new System.Text.RegularExpressions.Regex(netPattern, options);
            // FORX0003: pattern must not match empty string
            if (!isLiteral && regex.IsMatch(""))
                throw new XQueryException("FORX0003",
                    "Pattern matches a zero-length string in fn:replace");
            return ValueTask.FromResult<object?>(regex.Replace(input, netReplacement));
        }
        catch (InvalidOperationException)
        {
            throw; // FORX0004 errors
        }
        catch (ArgumentException ex)
        {
            throw new XQueryException("FORX0002",
                $"Invalid regular expression: {ex.Message}");
        }
    }
}

/// <summary>
/// fn:substring-before($arg1, $arg2) as xs:string
/// </summary>
public sealed class SubstringBeforeFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "substring-before");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "arg1"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "arg2"), Type = XdmSequenceType.OptionalString }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var str = ConcatFunction.XQueryStringValue(arguments[0]);
        var search = ConcatFunction.XQueryStringValue(arguments[1]);
        if (string.IsNullOrEmpty(search))
            return ValueTask.FromResult<object?>("");
        var comparison = CollationHelper.GetDefaultComparison(context);
        var idx = str.IndexOf(search, comparison);
        return ValueTask.FromResult<object?>(idx < 0 ? "" : str[..idx]);
    }
}

/// <summary>
/// fn:substring-after($arg1, $arg2) as xs:string
/// </summary>
public sealed class SubstringAfterFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "substring-after");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "arg1"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "arg2"), Type = XdmSequenceType.OptionalString }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var str = ConcatFunction.XQueryStringValue(arguments[0]);
        var search = ConcatFunction.XQueryStringValue(arguments[1]);
        if (string.IsNullOrEmpty(search))
            return ValueTask.FromResult<object?>(str);
        var comparison = CollationHelper.GetDefaultComparison(context);
        var idx = str.IndexOf(search, comparison);
        return ValueTask.FromResult<object?>(idx < 0 ? "" : str[(idx + search.Length)..]);
    }
}

/// <summary>
/// fn:compare($string1, $string2) as xs:integer?
/// </summary>
public sealed class CompareFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "compare");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Integer, Occurrence = Occurrence.ZeroOrOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "comparand1"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "comparand2"), Type = XdmSequenceType.OptionalString }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var a0 = Execution.QueryExecutionContext.Atomize(arguments[0]);
        var a1 = Execution.QueryExecutionContext.Atomize(arguments[1]);
        if (a0 == null || a1 == null)
            return ValueTask.FromResult<object?>(null);
        // fn:compare requires xs:string arguments (xs:untypedAtomic/xs:anyURI promote to string)
        StringLengthFunction.RequireStringLike(a0, "compare");
        StringLengthFunction.RequireStringLike(a1, "compare");
        var s1 = ConcatFunction.XQueryStringValue(a0);
        var s2 = ConcatFunction.XQueryStringValue(a1);
        var comparison = CollationHelper.GetDefaultComparison(context);
        var cmp = CollationHelper.CompareStrings(s1, s2, comparison);
        return ValueTask.FromResult<object?>((long)Math.Sign(cmp));
    }
}

/// <summary>
/// fn:string-to-codepoints($arg) as xs:integer*
/// </summary>
public sealed class StringToCodepointsFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "string-to-codepoints");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Integer, Occurrence = Occurrence.ZeroOrMore };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalString }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is null) return ValueTask.FromResult<object?>(null);
        // Atomize nodes to get their string value
        arg = DataFunction.Atomize(arg);
        if (arg is Xdm.XsUntypedAtomic ua) arg = ua.Value;
        if (arg is Xdm.XsTypedString ts) arg = ts.Value;
        if (arg is not string)
            throw new Execution.XQueryRuntimeException("XPTY0004",
                $"Expected xs:string argument for fn:string-to-codepoints, got {arg?.GetType().Name}");
        var str = (string)arg;
        var codepoints = str.EnumerateRunes().Select(r => (object?)(long)r.Value).ToArray();
        return ValueTask.FromResult<object?>(codepoints);
    }
}

/// <summary>
/// fn:codepoints-to-string($arg) as xs:string
/// </summary>
public sealed class CodepointsToStringFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "codepoints-to-string");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = new() { ItemType = ItemType.Integer, Occurrence = Occurrence.ZeroOrMore } }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg == null) return ValueTask.FromResult<object?>("");
        var codepoints = arg is IEnumerable<object?> seq
            ? seq.Select(x => QueryExecutionContext.ToInt(x))
            : [QueryExecutionContext.ToInt(arg)];
        // Use StringBuilder to avoid allocating intermediate string objects per codepoint
        var sb = new System.Text.StringBuilder();
        foreach (var cp in codepoints)
        {
            // XML 1.0 Char: #x9 | #xA | #xD | [#x20-#xD7FF] | [#xE000-#xFFFD] | [#x10000-#x10FFFF]
            var valid = cp == 0x9 || cp == 0xA || cp == 0xD
                || (cp >= 0x20 && cp <= 0xD7FF)
                || (cp >= 0xE000 && cp <= 0xFFFD)
                || (cp >= 0x10000 && cp <= 0x10FFFF);
            if (!valid)
                throw new XQueryRuntimeException("FOCH0001",
                    $"Invalid XML character codepoint: {cp}");
            sb.Append(char.ConvertFromUtf32(cp));
        }
        return ValueTask.FromResult<object?>(sb.ToString());
    }
}

/// <summary>
/// fn:codepoint-equal($comparand1, $comparand2) as xs:boolean?
/// Returns true if the two arguments are equal using Unicode codepoint comparison.
/// </summary>
public sealed class CodepointEqualFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "codepoint-equal");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Boolean, Occurrence = Occurrence.ZeroOrOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "comparand1"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "comparand2"), Type = XdmSequenceType.OptionalString }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg1 = arguments[0];
        var arg2 = arguments[1];
        // XPTY0004: arguments must be strings (or empty sequence/untypedAtomic)
        if (arg1 != null && arg1 is not string && arg1 is not Xdm.XsUntypedAtomic)
            throw new Execution.XQueryRuntimeException("XPTY0004",
                $"First argument to fn:codepoint-equal must be xs:string, got {arg1.GetType().Name}");
        if (arg2 != null && arg2 is not string && arg2 is not Xdm.XsUntypedAtomic)
            throw new Execution.XQueryRuntimeException("XPTY0004",
                $"Second argument to fn:codepoint-equal must be xs:string, got {arg2.GetType().Name}");
        var s1 = arg1?.ToString();
        var s2 = arg2?.ToString();
        // If either argument is empty sequence, return empty sequence
        if (s1 is null || s2 is null)
            return ValueTask.FromResult<object?>(null);
        return ValueTask.FromResult<object?>(string.Equals(s1, s2, StringComparison.Ordinal));
    }
}

/// <summary>
/// fn:encode-for-uri($arg) as xs:string
/// </summary>
public sealed class EncodeForUriFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "encode-for-uri");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalString }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is null) return ValueTask.FromResult<object?>("");
        if (arg is not string && arg is not Xdm.XsUntypedAtomic)
            throw new Execution.XQueryRuntimeException("XPTY0004",
                $"Expected xs:string argument for fn:encode-for-uri, got {arg.GetType().Name}");
        return ValueTask.FromResult<object?>(Uri.EscapeDataString(arg.ToString()!));
    }
}

/// <summary>
/// fn:escape-html-uri($arg) as xs:string
/// </summary>
public sealed class EscapeHtmlUriFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "escape-html-uri");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalString }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is not null && arg is not string && arg is not Xdm.XsUntypedAtomic && arg is not Xdm.XsAnyUri)
            throw new Execution.XQueryRuntimeException("XPTY0004",
                $"Expected xs:string argument for fn:escape-html-uri, got {arg.GetType().Name}");
        var str = arg?.ToString() ?? "";
        // escape-html-uri: only escape non-ASCII characters and characters not valid in URIs
        var sb = new System.Text.StringBuilder(str.Length);
        foreach (var ch in str)
        {
            if (ch >= 0x20 && ch <= 0x7E)
                sb.Append(ch);
            else
            {
                foreach (var b in System.Text.Encoding.UTF8.GetBytes(new[] { ch }))
                    sb.Append($"%{b:X2}");
            }
        }
        return ValueTask.FromResult<object?>(sb.ToString());
    }
}

/// <summary>
/// fn:iri-to-uri($arg) as xs:string
/// </summary>
public sealed class IriToUriFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "iri-to-uri");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalString }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is not null && arg is not string && arg is not Xdm.XsUntypedAtomic && arg is not Xdm.XsAnyUri)
            throw new Execution.XQueryRuntimeException("XPTY0004",
                $"Expected xs:string argument for fn:iri-to-uri, got {arg.GetType().Name}");
        var str = arg?.ToString() ?? "";
        var sb = new System.Text.StringBuilder(str.Length);
        foreach (var ch in str)
        {
            // IRI-to-URI: percent-encode characters outside of the URI character set
            if (ch <= 0x20 || ch >= 0x7F || "<>\"{}|\\^`".Contains(ch))
            {
                foreach (var b in System.Text.Encoding.UTF8.GetBytes(new[] { ch }))
                    sb.Append($"%{b:X2}");
            }
            else
            {
                sb.Append(ch);
            }
        }
        return ValueTask.FromResult<object?>(sb.ToString());
    }
}

/// <summary>
/// fn:normalize-unicode($arg) as xs:string
/// </summary>
public sealed class NormalizeUnicodeFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "normalize-unicode");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalString }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg != null)
            StringLengthFunction.RequireStringLike(arg, "normalize-unicode");
        var str = arg?.ToString() ?? "";
        return ValueTask.FromResult<object?>(str.Normalize(System.Text.NormalizationForm.FormC));
    }
}

/// <summary>
/// Helper for XQuery regex flag parsing and XPath-to-.NET regex conversion.
/// </summary>
public static class XQueryRegexHelper
{
    public static System.Text.RegularExpressions.RegexOptions ParseFlags(string flags)
    {
        var options = System.Text.RegularExpressions.RegexOptions.None;
        foreach (var c in flags)
        {
            options |= c switch
            {
                's' => System.Text.RegularExpressions.RegexOptions.Singleline,
                'm' => System.Text.RegularExpressions.RegexOptions.Multiline,
                'i' => System.Text.RegularExpressions.RegexOptions.IgnoreCase,
                'x' => System.Text.RegularExpressions.RegexOptions.None, // handled by StripXModeWhitespace before parsing
                'q' => System.Text.RegularExpressions.RegexOptions.None, // literal mode, handled separately
                _ => throw new XQueryException("FORX0001",
                    $"Invalid regular expression flag '{c}'. Valid flags are: s, m, i, x, q")
            };
        }
        return options;
    }

    /// <summary>
    /// Implements XPath 'x' flag: removes whitespace characters (#x9, #xA, #xD, #x20)
    /// from the regular expression, except when they appear inside character class
    /// expressions (between '[' and ']'). Unlike .NET's IgnorePatternWhitespace,
    /// XPath 'x' does not support '#' comments and does not treat '\ ' as an escaped space.
    /// </summary>
    public static string StripXModeWhitespace(string pattern)
    {
        var sb = new System.Text.StringBuilder(pattern.Length);
        bool inCharClass = false;

        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];

            if (inCharClass)
            {
                sb.Append(c);
                if (c == '\\' && i + 1 < pattern.Length)
                {
                    i++;
                    sb.Append(pattern[i]);
                }
                else if (c == ']')
                {
                    inCharClass = false;
                }
                continue;
            }

            if (c == '[')
            {
                inCharClass = true;
                sb.Append(c);
                continue;
            }

            if (c == '\\' && i + 1 < pattern.Length)
            {
                char next = pattern[i + 1];
                // Skip whitespace even after backslash — per XPath spec, x-mode strips
                // all whitespace outside char classes before any other interpretation
                if (next is ' ' or '\t' or '\n' or '\r')
                {
                    // Space stripped; backslash remains and will be processed with next non-ws char
                    // Actually per spec: whitespace is removed first, so \<space> becomes \ followed by
                    // whatever comes next. We need to output the backslash and let the next iteration handle it.
                    sb.Append('\\');
                    // Don't advance i — the space at i+1 will be handled next iteration and stripped
                    continue;
                }
                sb.Append(c);
                sb.Append(next);
                i++;
                continue;
            }

            // Strip whitespace outside char classes
            if (c is ' ' or '\t' or '\n' or '\r')
                continue;

            sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>
    /// In XPath/XQuery, without the 'm' flag, $ matches only at the absolute end of the string.
    /// In .NET, $ without Multiline matches at end of string AND before a trailing \n.
    /// This method replaces unescaped $ (outside character classes) with \z to get XPath semantics.
    /// </summary>
    public static string FixDollarAnchor(string pattern)
    {
        if (!pattern.Contains('$', StringComparison.Ordinal))
            return pattern;

        var sb = new System.Text.StringBuilder(pattern.Length + 4);
        var inCharClass = false;
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (inCharClass)
            {
                sb.Append(c);
                if (c == '\\' && i + 1 < pattern.Length)
                {
                    i++;
                    sb.Append(pattern[i]);
                }
                else if (c == ']')
                {
                    inCharClass = false;
                }
                continue;
            }

            if (c == '[') { inCharClass = true; sb.Append(c); continue; }
            if (c == '\\' && i + 1 < pattern.Length) { sb.Append(c); i++; sb.Append(pattern[i]); continue; }
            if (c == '$') { sb.Append("\\z"); continue; }
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// In XPath/XQuery with the 'm' flag, ^ matches at the start of the string and after
    /// every newline EXCEPT a newline that is the last character of the string.
    /// In .NET with Multiline, ^ also matches after a trailing newline (at the absolute end).
    /// This method replaces unescaped ^ (outside character classes) with (?!\z(?&lt;=\n))^
    /// to prevent matching at the end of string after a trailing newline.
    /// </summary>
    public static string FixCaretAnchorMultiline(string pattern)
    {
        if (!pattern.Contains('^', StringComparison.Ordinal))
            return pattern;

        var sb = new System.Text.StringBuilder(pattern.Length + 32);
        var inCharClass = false;
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (inCharClass)
            {
                sb.Append(c);
                if (c == '\\' && i + 1 < pattern.Length)
                {
                    i++;
                    sb.Append(pattern[i]);
                }
                else if (c == ']')
                {
                    inCharClass = false;
                }
                continue;
            }

            if (c == '[') { inCharClass = true; sb.Append(c); continue; }
            if (c == '\\' && i + 1 < pattern.Length) { sb.Append(c); i++; sb.Append(pattern[i]); continue; }
            if (c == '^') { sb.Append(@"(?!\z(?<=\n))^"); continue; }
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// In XPath/XQuery, the 'i' (case-insensitive) flag does not affect Unicode category
    /// escapes like \p{Lu} or \P{Lu}. In .NET, IgnoreCase causes \p{Lu} to also match
    /// lowercase letters, which is incorrect per the XPath spec. This method wraps
    /// \p{...} and \P{...} escapes (outside character classes) in (?-i:...) groups
    /// to locally disable case-insensitive matching for property escapes.
    /// </summary>
    public static string FixPropertyEscapesForCaseInsensitive(string pattern)
    {
        // Quick check: if no \p or \P, return as-is
        if (!pattern.Contains("\\p", StringComparison.Ordinal) &&
            !pattern.Contains("\\P", StringComparison.Ordinal))
            return pattern;

        var sb = new System.Text.StringBuilder(pattern.Length + 32);
        int charClassDepth = 0;

        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];

            if (charClassDepth > 0)
            {
                sb.Append(c);
                if (c == '\\' && i + 1 < pattern.Length)
                {
                    i++;
                    sb.Append(pattern[i]);
                }
                else if (c == '[')
                {
                    charClassDepth++;
                }
                else if (c == ']')
                {
                    charClassDepth--;
                }
                continue;
            }

            if (c == '[')
            {
                charClassDepth = 1;
                sb.Append(c);
                continue;
            }

            if (c == '\\' && i + 1 < pattern.Length)
            {
                char next = pattern[i + 1];
                if ((next is 'p' or 'P') && i + 2 < pattern.Length && pattern[i + 2] == '{')
                {
                    int closeBrace = pattern.IndexOf('}', i + 3);
                    if (closeBrace > 0)
                    {
                        // Wrap the entire \p{...} or \P{...} in (?-i:...)
                        sb.Append("(?-i:");
                        sb.Append(pattern, i, closeBrace - i + 1);
                        sb.Append(')');
                        i = closeBrace;
                        continue;
                    }
                }
                sb.Append(c);
                sb.Append(next);
                i++;
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>
    /// In XPath regex, "." matches any Unicode codepoint. In .NET regex, "." matches
    /// a single UTF-16 char, so surrogate pairs (non-BMP characters) are treated as two chars.
    /// This method replaces unescaped "." outside character classes with a subexpression
    /// that matches either a surrogate pair or a BMP character.
    /// </summary>
    public static string FixDotForSurrogatePairs(string pattern, bool singleLineMode = false)
    {
        if (!pattern.Contains('.', StringComparison.Ordinal))
            return pattern;

        // In XPath, without 's' flag, '.' matches any character except \n and \r.
        // .NET's default '.' matches any character except \n (but DOES match \r).
        // We fix this by using [^\r\n] instead of '.' when not in singleline mode.
        var surrogateDot = singleLineMode
            ? @"(?:[\uD800-\uDBFF][\uDC00-\uDFFF]|.)"
            : @"(?:[\uD800-\uDBFF][\uDC00-\uDFFF]|[^\r\n])";
        var sb = new System.Text.StringBuilder(pattern.Length + 16);
        var inCharClass = false;
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (inCharClass)
            {
                sb.Append(c);
                if (c == '\\' && i + 1 < pattern.Length)
                {
                    i++;
                    sb.Append(pattern[i]);
                }
                else if (c == ']')
                {
                    inCharClass = false;
                }
                continue;
            }

            if (c == '[') { inCharClass = true; sb.Append(c); continue; }
            if (c == '\\' && i + 1 < pattern.Length) { sb.Append(c); i++; sb.Append(pattern[i]); continue; }
            if (c == '.') { sb.Append(surrogateDot); continue; }
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Counts the number of capturing groups in an XPath regex pattern.
    /// Excludes non-capturing groups (?:...), lookahead (?=...), etc.
    /// </summary>
    public static int CountCapturingGroups(string pattern)
    {
        int count = 0;
        bool inCharClass = false;
        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];
            if (c == '\\' && i + 1 < pattern.Length)
            {
                i++; // skip escaped char
                continue;
            }
            if (inCharClass)
            {
                if (c == ']') inCharClass = false;
                continue;
            }
            if (c == '[') { inCharClass = true; continue; }
            if (c == '(' && (i + 1 >= pattern.Length || pattern[i + 1] != '?'))
                count++;
        }
        return count;
    }

    /// <summary>
    /// Validates that a regex pattern conforms to XSD/XPath regex syntax.
    /// Rejects constructs that .NET regex accepts but XSD regex does not:
    /// \b, \B, \u, \x, octal escapes, (?...), bare ], forward backreferences, {,n}.
    /// </summary>
    public static void ValidateXsdRegex(string pattern)
    {
        int charClassDepth = 0;
        int groupCount = 0;

        // First pass: find the closing position of each capturing group
        // groupClosePos[n] = index of ')' that closes capturing group n (1-based)
        var groupOpenStack = new Stack<int>(); // stack of group numbers for open parens
        var ncGroupStack = new Stack<bool>(); // true = non-capturing group
        var groupClosePos = new Dictionary<int, int>();

        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];
            if (c == '\\' && i + 1 < pattern.Length) { i++; continue; }
            if (charClassDepth > 0) { if (c == '[') charClassDepth++; else if (c == ']') charClassDepth--; continue; }
            if (c == '[') { charClassDepth = 1; continue; }
            if (c == '(')
            {
                bool isNonCapturing = i + 1 < pattern.Length && pattern[i + 1] == '?';
                ncGroupStack.Push(isNonCapturing);
                if (!isNonCapturing)
                {
                    groupCount++;
                    groupOpenStack.Push(groupCount);
                }
                else
                {
                    groupOpenStack.Push(0); // sentinel for non-capturing
                }
            }
            else if (c == ')' && groupOpenStack.Count > 0)
            {
                int gn = groupOpenStack.Pop();
                ncGroupStack.Pop();
                if (gn > 0)
                    groupClosePos[gn] = i;
            }
        }

        charClassDepth = 0;
        int charClassMemberCount = 0;

        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];

            if (c == '\\' && i + 1 < pattern.Length)
            {
                char next = pattern[i + 1];
                switch (next)
                {
                    case 'b' or 'B':
                        throw new InvalidOperationException($"FORX0002: Invalid regular expression: \\{next} (word boundary) is not supported in XSD regex");
                    case 'u':
                        throw new InvalidOperationException($"FORX0002: Invalid regular expression: \\u (Unicode escape) is not supported in XSD regex");
                    case 'x':
                        throw new InvalidOperationException($"FORX0002: Invalid regular expression: \\x (hex escape) is not supported in XSD regex");
                    case 'a' or 'e' or 'f' or 'v':
                        throw new InvalidOperationException($"FORX0002: Invalid regular expression: \\{next} is not a recognized escape in XSD regex");
                    case 'A' or 'Z' or 'z':
                        throw new InvalidOperationException($"FORX0002: Invalid regular expression: \\{next} (anchor) is not supported in XSD regex");
                }
                // \0 is never valid in XSD regex (no octal, no null)
                if (next == '0')
                    throw new InvalidOperationException("FORX0002: Invalid regular expression: \\0 (octal/null escape) is not supported in XSD regex");
                // Back references \1-\9 are not allowed inside character classes
                if (next >= '1' && next <= '9' && charClassDepth > 0)
                    throw new InvalidOperationException($"FORX0002: Invalid regular expression: back reference \\{next} is not allowed inside a character class");
                // Backreference validation: \1-\9 and multi-digit \10+ references
                if (next >= '1' && next <= '9' && charClassDepth == 0)
                {
                    // Collect all consecutive digits to handle multi-digit backrefs
                    int digitStart = i + 1;
                    int digitEnd = digitStart + 1;
                    while (digitEnd < pattern.Length && char.IsAsciiDigit(pattern[digitEnd]))
                        digitEnd++;
                    string allDigits = pattern[digitStart..digitEnd];
                    int fullNum = int.Parse(allDigits, System.Globalization.CultureInfo.InvariantCulture);

                    // Find longest valid prefix (must be ≤ groupCount)
                    int validLen = allDigits.Length;
                    int validNum = fullNum;
                    while (validNum > groupCount && validLen > 1)
                    {
                        validLen--;
                        validNum = int.Parse(allDigits[..validLen], System.Globalization.CultureInfo.InvariantCulture);
                    }

                    if (validNum >= 1 && validNum <= groupCount)
                    {
                        // Forward backreference check: the referenced group must be closed before this position
                        if (!groupClosePos.TryGetValue(validNum, out var closePos) || closePos > i)
                            throw new InvalidOperationException($"FORX0002: Invalid regular expression: back reference \\{validNum} is a forward reference (group {validNum} has not been closed at this point)");
                    }
                    else if (validNum > groupCount)
                    {
                        throw new InvalidOperationException($"FORX0002: Invalid regular expression: back reference \\{validNum} exceeds number of capturing groups ({groupCount})");
                    }
                    // Skip past all consumed digits (loop will i++ past the first)
                    i = digitStart + validLen - 1;
                }
                // \p{...} and \P{...} — skip the entire Unicode property block
                if (next is 'p' or 'P' && i + 2 < pattern.Length && pattern[i + 2] == '{')
                {
                    int closeBrace = pattern.IndexOf('}', i + 3);
                    if (closeBrace < 0)
                        throw new InvalidOperationException("FORX0002: Invalid regular expression: unterminated Unicode property escape \\p{...}");
                    // Spaces in \p{...} block names are invalid in XSD regex.
                    // When the 'x' flag is active, StripXModeWhitespace removes them before we get here.
                    var blockContent = pattern[(i + 3)..closeBrace];
                    if (blockContent.Contains(' ', StringComparison.Ordinal) || blockContent.Contains('\t', StringComparison.Ordinal))
                        throw new InvalidOperationException($"FORX0002: Invalid regular expression: invalid Unicode property name '\\{next}{{{blockContent}}}'");
                    if (charClassDepth > 0)
                        charClassMemberCount++; // \p{...} counts as class content
                    i = closeBrace; // skip to closing }
                    continue;
                }
                if (charClassDepth > 0)
                    charClassMemberCount++; // escaped char is class content
                i++; // skip escaped character
                continue;
            }

            if (charClassDepth > 0)
            {
                if (c == '[')
                {
                    // In XSD regex, '[' inside a character class is only valid as part of subtraction: -[subclass]
                    // Also check for POSIX character class syntax: [[:, [[=, [[.
                    if (i + 1 < pattern.Length && pattern[i + 1] is ':' or '=' or '.')
                        throw new InvalidOperationException($"FORX0002: Invalid regular expression: POSIX character class syntax '[{pattern[i + 1]}' is not supported in XSD regex");
                    // Check for unescaped '-' before this '[' — must be subtraction (not escaped, not at start/after ^)
                    bool prevIsDash = i > 0 && pattern[i - 1] == '-' && !(i >= 2 && pattern[i - 2] == '\\');
                    if (!prevIsDash)
                        throw new InvalidOperationException("FORX0002: Invalid regular expression: '[' inside character class is only valid as part of subtraction syntax '-[...]'");
                    // The '-' before '[' must not be a literal dash at the class start.
                    // charClassMemberCount tracks non-dash, non-bracket, non-^ chars before this point.
                    if (charClassMemberCount == 0)
                        throw new InvalidOperationException("FORX0002: Invalid regular expression: character class subtraction '-[...]' requires preceding class members");
                    charClassDepth++;
                }
                else if (c == ']')
                {
                    charClassDepth--;
                }
                else if (c == '-')
                {
                    // After a range (e.g., a-d), a '-' may start another range (XSD 1.1),
                    // introduce subtraction if followed by '[', or be a literal dash at end of class.
                    // We no longer reject this — .NET regex handles it correctly.
                }
                else
                {
                    charClassMemberCount++; // count actual class content (not dashes)
                }
                continue;
            }

            if (c == '[')
            {
                charClassDepth = 1;
                charClassMemberCount = 0;
                // Skip '^' after '[' for negated classes
                if (i + 1 < pattern.Length && pattern[i + 1] == '^')
                    i++;
                continue;
            }

            if (c == '(')
            {
                // (?:...) non-capturing groups are allowed; other (?...) constructs are not
                if (i + 1 < pattern.Length && pattern[i + 1] == '?')
                {
                    if (i + 2 >= pattern.Length || pattern[i + 2] != ':')
                        throw new InvalidOperationException("FORX0002: Invalid regular expression: (?...) constructs (other than (?:...)) are not supported in XSD regex");
                }
                continue;
            }

            if (c == ']')
            {
                // Bare ] outside character class is not valid in XSD regex
                throw new InvalidOperationException("FORX0002: Invalid regular expression: unescaped ']' outside character class");
            }

            if (c == '}' && charClassDepth == 0)
            {
                // In XSD regex, } outside character classes must close a quantifier.
                // A bare } without a preceding { is invalid (must be escaped as \}).
                // Valid quantifiers ({n}, {n,}, {n,m}) are handled by the { case below
                // which skips past the closing }, so we only reach here for stray }.
                throw new InvalidOperationException($"FORX0002: Invalid regular expression: unescaped '}}' at position {i} does not close a quantifier");
            }

            if (c == '{' && charClassDepth == 0)
            {
                // In XSD regex, { outside character classes must form a valid quantifier: {n}, {n,}, or {n,m}
                // Otherwise it's an error (bare { must be escaped as \{)
                int j = i + 1;
                // Must start with a digit
                if (j >= pattern.Length || !char.IsAsciiDigit(pattern[j]))
                    throw new InvalidOperationException($"FORX0002: Invalid regular expression: unescaped '{{' at position {i} does not start a valid quantifier");
                // Skip digits for minimum value
                while (j < pattern.Length && char.IsAsciiDigit(pattern[j])) j++;
                if (j >= pattern.Length)
                    throw new InvalidOperationException($"FORX0002: Invalid regular expression: unterminated quantifier starting at position {i}");
                if (pattern[j] == '}')
                {
                    i = j; // skip {n}
                    continue;
                }
                if (pattern[j] == ',')
                {
                    j++;
                    // Skip optional digits for maximum value
                    while (j < pattern.Length && char.IsAsciiDigit(pattern[j])) j++;
                    if (j >= pattern.Length || pattern[j] != '}')
                        throw new InvalidOperationException($"FORX0002: Invalid regular expression: unterminated quantifier starting at position {i}");
                    i = j; // skip {n,} or {n,m}
                    continue;
                }
                throw new InvalidOperationException($"FORX0002: Invalid regular expression: invalid quantifier at position {i}");
            }
        }
    }

    // XML NameStartChar: ":" | [A-Z] | "_" | [a-z] | [#xC0-#xD6] | ... per XML 1.0 §2.3
    private const string NameStartCharClass = @":A-Z_a-z\xC0-\xD6\xD8-\xF6\u00F8-\u02FF\u0370-\u037D\u037F-\u1FFF\u200C-\u200D\u2070-\u218F\u2C00-\u2FEF\u3001-\uD7FF\uF900-\uFDCF\uFDF0-\uFFFD";
    // XML NameChar adds: "-" | "." | [0-9] | #xB7 | [#x0300-#x036F] | [#x203F-#x2040]
    private const string NameCharExtra = @"\-.0-9\xB7\u0300-\u036F\u203F-\u2040";
    private const string NameCharClass = NameStartCharClass + NameCharExtra;

    // Unicode block ranges not natively supported by .NET regex engine.
    // Maps XSD block name → (startCodepoint, endCodepoint).
    private static readonly Dictionary<string, (int Start, int End)> UnsupportedUnicodeBlocks = new(StringComparer.OrdinalIgnoreCase)
    {
        ["IsOldItalic"] = (0x10300, 0x1032F),
        ["IsGothic"] = (0x10330, 0x1034F),
        ["IsDeseret"] = (0x10400, 0x1044F),
        ["IsByzantineMusicalSymbols"] = (0x1D000, 0x1D0FF),
        ["IsMusicalSymbols"] = (0x1D100, 0x1D1FF),
        ["IsMathematicalAlphanumericSymbols"] = (0x1D400, 0x1D7FF),
        ["IsCJKUnifiedIdeographsExtensionB"] = (0x20000, 0x2A6DF),
        ["IsCJKCompatibilityIdeographsSupplement"] = (0x2F800, 0x2FA1F),
        ["IsTags"] = (0xE0000, 0xE007F),
        ["IsSupplementaryPrivateUseArea-A"] = (0xF0000, 0xFFFFF),
        ["IsSupplementaryPrivateUseArea-B"] = (0x100000, 0x10FFFF),
        ["IsEmoticons"] = (0x1F600, 0x1F64F),
        ["IsMiscellaneousSymbolsAndPictographs"] = (0x1F300, 0x1F5FF),
        ["IsSupplementalSymbolsAndPictographs"] = (0x1F900, 0x1F9FF),
        ["IsSymbolsAndPictographsExtended-A"] = (0x1FA00, 0x1FA6F),
    };

    /// <summary>
    /// Converts a supplementary Unicode codepoint range to a .NET regex surrogate pair range.
    /// E.g., U+10300-U+1032F → \uD800[\uDF00-\uDF2F] (when both share the same high surrogate).
    /// For ranges spanning multiple high surrogates, generates alternations.
    /// </summary>
    private static string SupplementaryRangeToSurrogatePairs(int start, int end)
    {
        var sb = new System.Text.StringBuilder();
        int cur = start;
        while (cur <= end)
        {
            int highStart = 0xD800 + ((cur - 0x10000) >> 10);
            int lowStart = 0xDC00 + ((cur - 0x10000) & 0x3FF);

            // Find end of this high surrogate's range
            int highEnd = 0xD800 + ((end - 0x10000) >> 10);

            if (highStart == highEnd)
            {
                // Same high surrogate for entire remaining range
                int lowEnd = 0xDC00 + ((end - 0x10000) & 0x3FF);
                if (sb.Length > 0) sb.Append('|');
                sb.Append($"\\u{highStart:X4}[\\u{lowStart:X4}-\\u{lowEnd:X4}]");
                break;
            }
            else
            {
                // This high surrogate covers from lowStart to 0xDFFF
                if (sb.Length > 0) sb.Append('|');
                sb.Append($"\\u{highStart:X4}[\\u{lowStart:X4}-\\uDFFF]");
                // Move to next high surrogate
                cur = 0x10000 + ((highStart - 0xD800 + 1) << 10);

                // Middle high surrogates (full range DC00-DFFF)
                while (cur <= end)
                {
                    int h = 0xD800 + ((cur - 0x10000) >> 10);
                    int hEnd2 = 0xD800 + ((end - 0x10000) >> 10);
                    if (h == hEnd2)
                    {
                        int lowEnd = 0xDC00 + ((end - 0x10000) & 0x3FF);
                        sb.Append($"|\\u{h:X4}[\\uDC00-\\u{lowEnd:X4}]");
                        cur = end + 1; // done
                    }
                    else
                    {
                        sb.Append($"|\\u{h:X4}[\\uDC00-\\uDFFF]");
                        cur = 0x10000 + ((h - 0xD800 + 1) << 10);
                    }
                }
            }
        }
        return sb.Length > 0 && sb.ToString().Contains('|') ? $"(?:{sb})" : sb.ToString();
    }

    // XSD \w = [^\p{P}\p{Z}\p{C}] — everything except punctuation, separators, and control/format chars.
    // This is different from .NET \w which is [\p{L}\p{Mn}\p{Nd}\p{Pc}] (includes underscore).
    // XSD \w notably EXCLUDES underscore (which is \p{Pc}, a punctuation category).
    private const string XsdWordClass = @"\p{L}\p{M}\p{N}\p{S}";
    private const string XsdWordClassFull = @"[\p{L}\p{M}\p{N}\p{S}]";
    private const string XsdNonWordClassFull = @"[\p{P}\p{Z}\p{C}]";

    /// <summary>
    /// Known supplementary plane ranges for Unicode general categories that .NET regex
    /// cannot match via \p{...} (because .NET sees surrogate pairs as individual code units).
    /// Maps category name → list of (start, end) codepoint ranges in supplementary planes.
    /// </summary>
    private static readonly Dictionary<string, (int Start, int End)[]> SupplementaryCategoryRanges = new(StringComparer.Ordinal)
    {
        // Lu (Uppercase Letter) — Mathematical Alphanumeric Symbols + Deseret + others
        ["Lu"] = [
            (0x10400, 0x10427), // Deseret uppercase
            (0x104B0, 0x104D3), // Osage uppercase
            (0x10C80, 0x10CB2), // Old Hungarian uppercase
            (0x118A0, 0x118BF), // Warang Citi uppercase
            (0x16E40, 0x16E5F), // Medefaidrin uppercase
            (0x1D400, 0x1D419), // Math Bold Capital A-Z
            (0x1D434, 0x1D44D), // Math Italic Capital A-Z
            (0x1D468, 0x1D481), // Math Bold Italic Capital A-Z
            (0x1D49C, 0x1D4B5), // Math Script Capital (with gaps)
            (0x1D4D0, 0x1D4E9), // Math Bold Script Capital A-Z
            (0x1D504, 0x1D51C), // Math Fraktur Capital (with gaps)
            (0x1D538, 0x1D550), // Math Double-Struck Capital (with gaps)
            (0x1D56C, 0x1D585), // Math Bold Fraktur Capital A-Z
            (0x1D5A0, 0x1D5B9), // Math Sans-Serif Capital A-Z
            (0x1D5D4, 0x1D5ED), // Math Sans-Serif Bold Capital A-Z
            (0x1D608, 0x1D621), // Math Sans-Serif Italic Capital A-Z
            (0x1D63C, 0x1D655), // Math Sans-Serif Bold Italic Capital A-Z
            (0x1D670, 0x1D689), // Math Monospace Capital A-Z
            (0x1D6A8, 0x1D6C0), // Math Bold Capital Greek
            (0x1D6E2, 0x1D6FA), // Math Italic Capital Greek
            (0x1D71C, 0x1D734), // Math Bold Italic Capital Greek
            (0x1D756, 0x1D76E), // Math Sans-Serif Bold Capital Greek
            (0x1D790, 0x1D7A8), // Math Sans-Serif Bold Italic Capital Greek
            (0x1E900, 0x1E921), // Adlam uppercase
        ],
        // Ll (Lowercase Letter)
        ["Ll"] = [
            (0x10428, 0x1044F), // Deseret lowercase
            (0x104D8, 0x104FB), // Osage lowercase
            (0x10CC0, 0x10CF2), // Old Hungarian lowercase (small)
            (0x118C0, 0x118DF), // Warang Citi lowercase
            (0x16E60, 0x16E7F), // Medefaidrin lowercase
            (0x1D41A, 0x1D433), // Math Bold Small a-z
            (0x1D44E, 0x1D467), // Math Italic Small a-z
            (0x1D482, 0x1D49B), // Math Bold Italic Small a-z
            (0x1D4B6, 0x1D4CF), // Math Script Small (with gaps)
            (0x1D4EA, 0x1D503), // Math Bold Script Small a-z
            (0x1D51E, 0x1D537), // Math Fraktur Small a-z
            (0x1D552, 0x1D56B), // Math Double-Struck Small a-z
            (0x1D586, 0x1D59F), // Math Bold Fraktur Small a-z
            (0x1D5BA, 0x1D5D3), // Math Sans-Serif Small a-z
            (0x1D5EE, 0x1D607), // Math Sans-Serif Bold Small a-z
            (0x1D622, 0x1D63B), // Math Sans-Serif Italic Small a-z
            (0x1D656, 0x1D66F), // Math Sans-Serif Bold Italic Small a-z
            (0x1D68A, 0x1D6A5), // Math Monospace Small a-z
            (0x1D6C2, 0x1D6DA), // Math Bold Small Greek
            (0x1D6DC, 0x1D6E1), // Math Bold Greek symbols
            (0x1D6FC, 0x1D714), // Math Italic Small Greek
            (0x1D716, 0x1D71B), // Math Italic Greek symbols
            (0x1D736, 0x1D74E), // Math Bold Italic Small Greek
            (0x1D750, 0x1D755), // Math Bold Italic Greek symbols
            (0x1D770, 0x1D788), // Math Sans-Serif Bold Small Greek
            (0x1D78A, 0x1D78F), // Math Sans-Serif Bold Greek symbols
            (0x1D7AA, 0x1D7C2), // Math Sans-Serif Bold Italic Small Greek
            (0x1D7C4, 0x1D7C9), // Math Sans-Serif Bold Italic Greek symbols
            (0x1E922, 0x1E943), // Adlam lowercase
        ],
        // Nd (Decimal Digit Number)
        ["Nd"] = [
            (0x104A0, 0x104A9), // Osmanya digits
            (0x10D30, 0x10D39), // Hanifi Rohingya digits
            (0x11066, 0x1106F), // Brahmi digits
            (0x110F0, 0x110F9), // Sora Sompeng digits
            (0x11136, 0x1113F), // Chakma digits
            (0x111D0, 0x111D9), // Sharada digits
            (0x112F0, 0x112F9), // Khudawadi digits
            (0x11450, 0x11459), // Newa digits
            (0x114D0, 0x114D9), // Tirhuta digits
            (0x11650, 0x11659), // Modi digits
            (0x116C0, 0x116C9), // Takri digits
            (0x11730, 0x11739), // Ahom digits
            (0x118E0, 0x118E9), // Warang Citi digits
            (0x11950, 0x11959), // Dives Akuru digits
            (0x11C50, 0x11C59), // Bhaiksuki digits
            (0x11D50, 0x11D59), // Masaram Gondi digits
            (0x11DA0, 0x11DA9), // Gunjala Gondi digits
            (0x16A60, 0x16A69), // Mro digits
            (0x16AC0, 0x16AC9), // Tangsa digits
            (0x16B50, 0x16B59), // Pahawh Hmong digits
            (0x1D7CE, 0x1D7FF), // Mathematical digits (bold, double-struck, etc.)
            (0x1E140, 0x1E149), // Nyiakeng Puachue Hmong digits
            (0x1E2F0, 0x1E2F9), // Wancho digits
            (0x1E4F0, 0x1E4F9), // Nag Mundari digits
            (0x1E950, 0x1E959), // Adlam digits
            (0x1FBF0, 0x1FBF9), // Segmented digit forms
        ],
        // So (Other Symbol)
        ["So"] = [
            (0x10137, 0x1013F), // Aegean number signs
            (0x10179, 0x10189), // Greek numeric symbols
            (0x1018C, 0x1018E), // Greek numeric symbols
            (0x10190, 0x1019C), // Ancient symbols
            (0x101A0, 0x101A0), // Greek symbol
            (0x101D0, 0x101FC), // Phaistos Disc signs
            (0x10877, 0x10878), // Nabataean symbols
            (0x1091F, 0x1091F), // Phoenician word separator
            (0x1D000, 0x1D0F5), // Byzantine musical symbols
            (0x1D100, 0x1D126), // Musical symbols
            (0x1D129, 0x1D1EA), // Musical symbols (continued)
            (0x1D200, 0x1D245), // Ancient Greek musical notation
            (0x1D300, 0x1D356), // Tai Xuan Jing symbols
            (0x1F000, 0x1F02B), // Mahjong tiles
            (0x1F030, 0x1F093), // Domino tiles
            (0x1F0A0, 0x1F0F5), // Playing cards
            (0x1F110, 0x1F16C), // Enclosed alphanumerics supplement
            (0x1F170, 0x1F1AC), // Enclosed alphanumerics supplement (continued)
            (0x1F1E0, 0x1F1FF), // Regional indicator symbols
            (0x1F300, 0x1F5FF), // Misc symbols and pictographs (broad range)
            (0x1F600, 0x1F64F), // Emoticons
            (0x1F650, 0x1F67F), // Ornamental dingbats
            (0x1F680, 0x1F6FF), // Transport/map symbols
            (0x1F700, 0x1F77F), // Alchemical symbols
            (0x1F780, 0x1F7FF), // Geometric shapes extended
            (0x1F800, 0x1F8FF), // Supplemental arrows-C
            (0x1F900, 0x1F9FF), // Supplemental symbols and pictographs
            (0x1FA00, 0x1FA6F), // Chess symbols
            (0x1FA70, 0x1FAFF), // Symbols and pictographs extended-A
        ],
        // S (Symbol — union of Sm, Sc, Sk, So)
        ["S"] = [
            (0x10137, 0x1013F),
            (0x10179, 0x10189),
            (0x1018C, 0x1018E),
            (0x10190, 0x1019C),
            (0x101A0, 0x101A0),
            (0x101D0, 0x101FC),
            (0x10877, 0x10878),
            (0x1091F, 0x1091F),
            (0x1D000, 0x1D0F5),
            (0x1D100, 0x1D126),
            (0x1D129, 0x1D1EA),
            (0x1D200, 0x1D245),
            (0x1D300, 0x1D356),
            (0x1D400, 0x1D7FF), // Mathematical Alphanumeric Symbols (Sm portion)
            (0x1EE00, 0x1EEBB), // Arabic Mathematical Alphabetic Symbols
            (0x1F000, 0x1F02B),
            (0x1F030, 0x1F093),
            (0x1F0A0, 0x1F0F5),
            (0x1F110, 0x1F16C),
            (0x1F170, 0x1F1AC),
            (0x1F1E0, 0x1F1FF),
            (0x1F300, 0x1FAFF),
        ],
        // Lo (Other Letter) — CJK Ideographs, syllabaries, and other scripts in supplementary planes
        ["Lo"] = [
            (0x10000, 0x1000B), (0x1000D, 0x10026), (0x10028, 0x1003A), (0x1003C, 0x1003D),
            (0x1003F, 0x1004D), (0x10050, 0x1005D), (0x10080, 0x100FA), // Linear B
            (0x10280, 0x1029C), (0x102A0, 0x102D0), // Lycian, Carian
            (0x10300, 0x10323), (0x1032D, 0x10340), (0x10342, 0x10349), // Old Italic, Gothic
            (0x10350, 0x10375), // Old Permic
            (0x10380, 0x1039D), (0x103A0, 0x103C3), (0x103C8, 0x103CF), // Ugaritic, Old Persian
            (0x103D1, 0x103D5), // Old Persian numbers (Lo per Unicode)
            (0x10450, 0x1049D), // Shavian + Osmanya letters
            (0x10500, 0x10527), // Elbasan
            (0x10530, 0x10563), // Caucasian Albanian
            (0x10600, 0x10736), // Linear A
            (0x10800, 0x10855), // Cypriot
            (0x10860, 0x10876), // Palmyrene
            (0x10900, 0x10915), // Phoenician
            (0x10920, 0x1092B), // Lydian
            (0x10980, 0x109B7), // Meroitic Cursive
            (0x10A00, 0x10A00), (0x10A10, 0x10A13), (0x10A15, 0x10A17), (0x10A19, 0x10A35), // Kharoshthi
            (0x10A60, 0x10A7C), // Old South Arabian
            (0x10A80, 0x10A9C), // Old North Arabian
            (0x10AC0, 0x10AC7), (0x10AC9, 0x10AE4), // Manichaean
            (0x10B00, 0x10B35), // Avestan
            (0x10B40, 0x10B55), // Inscriptional Parthian
            (0x10B60, 0x10B72), // Inscriptional Pahlavi
            (0x10B80, 0x10B91), // Psalter Pahlavi
            (0x10C00, 0x10C48), // Old Turkic
            (0x10D00, 0x10D23), // Hanifi Rohingya
            (0x10E80, 0x10EA9), // Yezidi
            (0x10EB0, 0x10EB1), // Yezidi supplement
            (0x10F00, 0x10F1C), (0x10F27, 0x10F27), (0x10F30, 0x10F45), // Old Sogdian, Sogdian
            (0x10FB0, 0x10FC4), // Chorasmian
            (0x10FE0, 0x10FF6), // Elymaic
            (0x11003, 0x11037), // Brahmi
            (0x11071, 0x11072), (0x11075, 0x11075),
            (0x11083, 0x110AF), // Kaithi
            (0x110D0, 0x110E8), // Sora Sompeng
            (0x11103, 0x11126), // Chakma
            (0x11144, 0x11147),
            (0x11150, 0x11172), (0x11176, 0x11176), // Mahajani
            (0x11183, 0x111B2), // Sharada
            (0x111C1, 0x111C4),
            (0x11200, 0x11211), (0x11213, 0x1122B), // Khojki
            (0x11280, 0x11286), (0x11288, 0x11288), (0x1128A, 0x1128D), (0x1128F, 0x1129D), (0x1129F, 0x112A8), // Multani
            (0x11305, 0x1130C), (0x1130F, 0x11310), (0x11313, 0x11328), (0x1132A, 0x11330),
            (0x11332, 0x11333), (0x11335, 0x11339), (0x1133D, 0x1133D), // Grantha
            (0x11350, 0x11350),
            (0x1135D, 0x11361), // Grantha supplement
            (0x11400, 0x11434), // Newa
            (0x11447, 0x1144A),
            (0x1145F, 0x11461),
            (0x11480, 0x114AF), // Tirhuta
            (0x114C4, 0x114C5), (0x114C7, 0x114C7),
            (0x11580, 0x115AE), // Siddham
            (0x115D8, 0x115DB),
            (0x11600, 0x1162F), // Modi
            (0x11644, 0x11644),
            (0x11680, 0x116AA), // Takri
            (0x116B8, 0x116B8),
            (0x11700, 0x1171A), // Ahom
            (0x11740, 0x11746),
            (0x11800, 0x1182B), // Dogra
            (0x118FF, 0x11906), // Warang Citi letter
            (0x11909, 0x11909),
            (0x1190C, 0x11913), (0x11915, 0x11916), (0x11918, 0x1192F), // Dives Akuru
            (0x1193F, 0x1193F), (0x11941, 0x11941),
            (0x119A0, 0x119A7), (0x119AA, 0x119D0), // Nandinagari
            (0x119E1, 0x119E1), (0x119E3, 0x119E3),
            (0x11A00, 0x11A00), (0x11A0B, 0x11A32), // Zanabazar Square
            (0x11A3A, 0x11A3A),
            (0x11A50, 0x11A50), (0x11A5C, 0x11A89), // Soyombo
            (0x11A9D, 0x11A9D),
            (0x11AB0, 0x11AF8), // Canadian Aboriginal Extended-A
            (0x11C00, 0x11C08), (0x11C0A, 0x11C2E), // Bhaiksuki
            (0x11C40, 0x11C40),
            (0x11C72, 0x11C8F), // Marchen
            (0x11D00, 0x11D06), (0x11D08, 0x11D09), (0x11D0B, 0x11D30), // Masaram Gondi
            (0x11D46, 0x11D46),
            (0x11D60, 0x11D65), (0x11D67, 0x11D68), (0x11D6A, 0x11D89), // Gunjala Gondi
            (0x11D98, 0x11D98),
            (0x11EE0, 0x11EF2), // Makasar
            (0x11FB0, 0x11FB0), // Lisu supplement
            (0x12000, 0x12399), // Cuneiform
            (0x12480, 0x12543), // Early Dynastic Cuneiform
            (0x12F90, 0x12FF0), // Cypro-Minoan
            (0x13000, 0x1342E), // Egyptian Hieroglyphs
            (0x14400, 0x14646), // Anatolian Hieroglyphs
            (0x16800, 0x16A38), // Bamum Supplement
            (0x16A40, 0x16A5E), // Mro
            (0x16A70, 0x16ABE), // Tangsa
            (0x16AD0, 0x16AED), // Bassa Vah
            (0x16B00, 0x16B2F), // Pahawh Hmong
            (0x16B40, 0x16B43),
            (0x16B63, 0x16B77), (0x16B7D, 0x16B8F),
            (0x16F00, 0x16F4A), (0x16F50, 0x16F50), (0x16F93, 0x16F9F), // Miao
            (0x16FE0, 0x16FE1), (0x16FE3, 0x16FE3),
            (0x17000, 0x187F7), // Tangut
            (0x18800, 0x18CD5), // Tangut Components
            (0x18D00, 0x18D08),
            (0x1AFF0, 0x1AFF3), (0x1AFF5, 0x1AFFB), (0x1AFFD, 0x1AFFE), // Kana Extended-B
            (0x1B000, 0x1B122), (0x1B150, 0x1B152), (0x1B164, 0x1B167), // Kana Supplement
            (0x1B170, 0x1B2FB), // Nushu
            (0x1BC00, 0x1BC6A), (0x1BC70, 0x1BC7C), (0x1BC80, 0x1BC88), (0x1BC90, 0x1BC99), // Duployan
            (0x1E100, 0x1E12C), (0x1E137, 0x1E13D), // Nyiakeng Puachue Hmong
            (0x1E14E, 0x1E14E),
            (0x1E290, 0x1E2AD), // Toto
            (0x1E2C0, 0x1E2EB), // Wancho
            (0x1E4D0, 0x1E4EB), // Nag Mundari
            (0x1E7E0, 0x1E7E6), (0x1E7E8, 0x1E7EB), (0x1E7ED, 0x1E7EE), (0x1E7F0, 0x1E7FE), // Ethiopic Extended-B
            (0x1E800, 0x1E8C4), // Mende Kikakui
            (0x1EE00, 0x1EE03), (0x1EE05, 0x1EE1F), (0x1EE21, 0x1EE22), (0x1EE24, 0x1EE24), // Arabic Mathematical
            (0x1EE27, 0x1EE27), (0x1EE29, 0x1EE32), (0x1EE34, 0x1EE37), (0x1EE39, 0x1EE39),
            (0x1EE3B, 0x1EE3B), (0x1EE42, 0x1EE42), (0x1EE47, 0x1EE47), (0x1EE49, 0x1EE49),
            (0x1EE4B, 0x1EE4B), (0x1EE4D, 0x1EE4F), (0x1EE51, 0x1EE52), (0x1EE54, 0x1EE54),
            (0x1EE57, 0x1EE57), (0x1EE59, 0x1EE59), (0x1EE5B, 0x1EE5B), (0x1EE5D, 0x1EE5D),
            (0x1EE5F, 0x1EE5F), (0x1EE61, 0x1EE62), (0x1EE64, 0x1EE64), (0x1EE67, 0x1EE6A),
            (0x1EE6C, 0x1EE72), (0x1EE74, 0x1EE77), (0x1EE79, 0x1EE7C), (0x1EE7E, 0x1EE7E),
            (0x1EE80, 0x1EE89), (0x1EE8B, 0x1EE9B), (0x1EEA1, 0x1EEA3), (0x1EEA5, 0x1EEA9),
            (0x1EEAB, 0x1EEBB),
            (0x20000, 0x2A6DF), // CJK Unified Ideographs Extension B
            (0x2A700, 0x2B739), // CJK Extension C
            (0x2B740, 0x2B81D), // CJK Extension D
            (0x2B820, 0x2CEA1), // CJK Extension E
            (0x2CEB0, 0x2EBE0), // CJK Extension F
            (0x2F800, 0x2FA1D), // CJK Compatibility Ideographs Supplement
            (0x30000, 0x3134A), // CJK Extension G
            (0x31350, 0x323AF), // CJK Extension H
        ],
        // Mn (Nonspacing Mark)
        ["Mn"] = [
            (0x10A01, 0x10A03), (0x10A05, 0x10A06), (0x10A0C, 0x10A0F), // Kharoshthi
            (0x10A38, 0x10A3A), (0x10A3F, 0x10A3F),
            (0x10AE5, 0x10AE6), // Manichaean
            (0x10D24, 0x10D27), // Hanifi Rohingya
            (0x10EAB, 0x10EAC), // Yezidi
            (0x10F46, 0x10F50), // Sogdian
            (0x10F82, 0x10F85), // Old Uyghur
            (0x11001, 0x11001), // Brahmi
            (0x11038, 0x11046),
            (0x11070, 0x11070), (0x11073, 0x11074),
            (0x1107F, 0x11081),
            (0x110B3, 0x110B6), (0x110B9, 0x110BA), // Kaithi
            (0x11100, 0x11102), // Chakma
            (0x11127, 0x1112B), (0x1112D, 0x11134),
            (0x11173, 0x11173), // Mahajani
            (0x11180, 0x11181), // Sharada
            (0x111B6, 0x111BE),
            (0x111C9, 0x111CC), (0x111CF, 0x111CF),
            (0x1122F, 0x11231), (0x11234, 0x11234), (0x11236, 0x11237), // Khojki
            (0x1123E, 0x1123E),
            (0x112DF, 0x112DF), (0x112E3, 0x112EA), // Khudawadi
            (0x1133B, 0x1133C), // Grantha
            (0x11340, 0x11340),
            (0x11366, 0x1136C), (0x11370, 0x11374),
            (0x11438, 0x1143F), (0x11442, 0x11444), (0x11446, 0x11446), // Newa
            (0x1145E, 0x1145E),
            (0x114B3, 0x114B8), (0x114BA, 0x114BA), (0x114BF, 0x114C0), (0x114C2, 0x114C3), // Tirhuta
            (0x115B2, 0x115B5), (0x115BC, 0x115BD), (0x115BF, 0x115C0), (0x115DC, 0x115DD), // Siddham
            (0x11633, 0x1163A), (0x1163D, 0x1163D), (0x1163F, 0x11640), // Modi
            (0x116AB, 0x116AB), (0x116AD, 0x116AD), (0x116B0, 0x116B5), (0x116B7, 0x116B7), // Takri
            (0x1171D, 0x1171F), (0x11722, 0x11725), (0x11727, 0x1172B), // Ahom
            (0x1182F, 0x11837), (0x11839, 0x1183A), // Dogra
            (0x1193B, 0x1193C), (0x1193E, 0x1193E), (0x11943, 0x11943), // Dives Akuru
            (0x119D4, 0x119D7), (0x119DA, 0x119DB), (0x119E0, 0x119E0), // Nandinagari
            (0x11A01, 0x11A0A), // Zanabazar Square
            (0x11A33, 0x11A38), (0x11A3B, 0x11A3E),
            (0x11A47, 0x11A47),
            (0x11A51, 0x11A56), (0x11A59, 0x11A5B), // Soyombo
            (0x11A8A, 0x11A96), (0x11A98, 0x11A99),
            (0x11C30, 0x11C36), (0x11C38, 0x11C3D), (0x11C3F, 0x11C3F), // Bhaiksuki
            (0x11C92, 0x11CA7), (0x11CAA, 0x11CB0), (0x11CB2, 0x11CB3), (0x11CB5, 0x11CB6), // Marchen
            (0x11D31, 0x11D36), (0x11D3A, 0x11D3A), (0x11D3C, 0x11D3D), (0x11D3F, 0x11D45), // Masaram Gondi
            (0x11D47, 0x11D47),
            (0x11D90, 0x11D91), (0x11D95, 0x11D95), (0x11D97, 0x11D97), // Gunjala Gondi
            (0x11EF3, 0x11EF4), // Makasar
            (0x11F00, 0x11F01), (0x11F36, 0x11F3A), (0x11F40, 0x11F40), // Sinhala Archaic
            (0x13440, 0x13440), (0x13447, 0x13455), // Egyptian Hieroglyphs format
            (0x16AF0, 0x16AF4), // Bassa Vah combining
            (0x16B30, 0x16B36), // Pahawh Hmong marks
            (0x16F4F, 0x16F4F), (0x16F8F, 0x16F92), // Miao
            (0x16FE4, 0x16FE4),
            (0x1BC9D, 0x1BC9E), // Duployan
            (0x1CF00, 0x1CF2D), (0x1CF30, 0x1CF46), // Znamenny Musical
            (0x1D167, 0x1D169), // Musical symbols combining
            (0x1D17B, 0x1D182), (0x1D185, 0x1D18B), (0x1D1AA, 0x1D1AD),
            (0x1D242, 0x1D244), // Combining Greek Musical
            (0x1DA00, 0x1DA36), (0x1DA3B, 0x1DA6C), (0x1DA75, 0x1DA75), (0x1DA84, 0x1DA84), // Signwriting
            (0x1DA9B, 0x1DA9F), (0x1DAA1, 0x1DAAF),
            (0x1E000, 0x1E006), (0x1E008, 0x1E018), (0x1E01B, 0x1E021), (0x1E023, 0x1E024), // Glagolitic supplement
            (0x1E026, 0x1E02A),
            (0x1E130, 0x1E136), // Nyiakeng Puachue Hmong
            (0x1E2AE, 0x1E2AE), // Toto
            (0x1E2EC, 0x1E2EF), // Wancho
            (0x1E4EC, 0x1E4EF), // Nag Mundari
            (0x1E8D0, 0x1E8D6), // Mende Kikakui combining
            (0x1E944, 0x1E94A), // Adlam
        ],
        // Mc (Spacing Mark)
        ["Mc"] = [
            (0x11000, 0x11000), (0x11002, 0x11002), // Brahmi
            (0x11082, 0x11082), // Kaithi
            (0x110B0, 0x110B2), (0x110B7, 0x110B8), // Sora Sompeng
            (0x1112C, 0x1112C), // Chakma
            (0x11145, 0x11146), // Mahajani
            (0x11182, 0x11182), // Sharada
            (0x111B3, 0x111B5), (0x111BF, 0x111C0),
            (0x1122C, 0x1122E), (0x11232, 0x11233), (0x11235, 0x11235), // Khojki
            (0x112E0, 0x112E2), // Khudawadi
            (0x11300, 0x11303), // Grantha
            (0x1133E, 0x1133F), (0x11341, 0x11344), (0x11347, 0x11348), (0x1134B, 0x1134D),
            (0x11357, 0x11357),
            (0x11362, 0x11363),
            (0x11435, 0x11437), (0x11440, 0x11441), (0x11445, 0x11445), // Newa
            (0x114B0, 0x114B2), (0x114B9, 0x114B9), (0x114BB, 0x114BE), (0x114C1, 0x114C1), // Tirhuta
            (0x115AF, 0x115B1), (0x115B8, 0x115BB), (0x115BE, 0x115BE), // Siddham
            (0x11630, 0x11632), (0x1163B, 0x1163C), (0x1163E, 0x1163E), // Modi
            (0x116AC, 0x116AC), (0x116AE, 0x116AF), (0x116B6, 0x116B6), // Takri
            (0x11720, 0x11721), (0x11726, 0x11726), // Ahom
            (0x1182C, 0x1182E), (0x11838, 0x11838), // Dogra
            (0x11930, 0x11935), (0x11937, 0x11938), // Dives Akuru
            (0x1193D, 0x1193D), (0x11940, 0x11940), (0x11942, 0x11942),
            (0x119D1, 0x119D3), (0x119DC, 0x119DF), (0x119E4, 0x119E4), // Nandinagari
            (0x11A39, 0x11A39), // Zanabazar Square
            (0x11A57, 0x11A58), (0x11A97, 0x11A97), // Soyombo
            (0x11C2F, 0x11C2F), // Bhaiksuki
            (0x11CA9, 0x11CA9), (0x11CB1, 0x11CB1), (0x11CB4, 0x11CB4), // Marchen
            (0x11D8A, 0x11D8E), (0x11D93, 0x11D94), (0x11D96, 0x11D96), // Gunjala Gondi
            (0x11EF5, 0x11EF6), // Makasar
            (0x11F03, 0x11F03), (0x11F34, 0x11F35), (0x11F3E, 0x11F3F), (0x11F41, 0x11F41), // Sinhala
            (0x16F51, 0x16F87), // Miao vowels
            (0x1D165, 0x1D166), (0x1D16D, 0x1D172), // Musical symbols
        ],
        // Nl (Letter Number) — Gothic, Aegean, etc.
        ["Nl"] = [
            (0x10140, 0x10174), // Greek Acrophonic Numerals
            (0x1018A, 0x1018B), // Greek number signs
            (0x10341, 0x10341), // Gothic letter ninety
            (0x1034A, 0x1034A), // Gothic letter nine hundred
            (0x103D1, 0x103D5), // Old Persian number signs
            (0x12400, 0x1246E), // Cuneiform Numbers and Punctuation
        ],
        // No (Other Number)
        ["No"] = [
            (0x10107, 0x10133), // Aegean numbers
            (0x10175, 0x10178), // Greek acrophonic numerals (fractions)
            (0x1018C, 0x1018E), // Greek number signs
            (0x102E1, 0x102FB), // Coptic Epact Numbers
            (0x10320, 0x10323), // Old Italic numerals
            (0x10858, 0x1085F), // Imperial Aramaic numbers
            (0x10879, 0x1087F), // Nabataean numbers
            (0x108A7, 0x108AF), // Nabataean numbers
            (0x108FB, 0x108FF), // Hatran numbers
            (0x10916, 0x1091B), // Phoenician numbers
            (0x109BC, 0x109BD), (0x109C0, 0x109CF), (0x109D2, 0x109FF), // Meroitic numbers
            (0x10A40, 0x10A48), // Kharoshthi numbers
            (0x10A7D, 0x10A7E), // Old South Arabian numbers
            (0x10A9D, 0x10A9F), // Old North Arabian numbers
            (0x10AEB, 0x10AEF), // Manichaean numbers
            (0x10B58, 0x10B5F), // Inscriptional Parthian numbers
            (0x10B78, 0x10B7F), // Inscriptional Pahlavi numbers
            (0x10BA9, 0x10BAF), // Psalter Pahlavi numbers
            (0x10CFA, 0x10CFF), // Old Turkic numbers
            (0x10E60, 0x10E7E), // Rumi numeral symbols
            (0x10F1D, 0x10F26), // Old Sogdian numbers
            (0x10F51, 0x10F54), // Sogdian numbers
            (0x10FC5, 0x10FCB), // Chorasmian numbers
            (0x11052, 0x11065), // Brahmi numbers
            (0x111E1, 0x111F4), // Sinhala Archaic numbers
            (0x11730, 0x1173B), // Ahom numbers
            (0x118EA, 0x118F2), // Warang Citi numbers
            (0x11C5A, 0x11C6C), // Bhaiksuki numbers
            (0x16B5B, 0x16B61), // Pahawh Hmong number letters
            (0x16E80, 0x16E96), // Medefaidrin numbers
            (0x1D7CE, 0x1D7FF), // Mathematical digits (portion classified as No)
            (0x1E8C7, 0x1E8CF), // Mende Kikakui numbers
            (0x1EC71, 0x1ECAB), (0x1ECAD, 0x1ECAF), (0x1ECB1, 0x1ECB4), // Indic Siyaq Numbers
            (0x1ED01, 0x1ED2D), (0x1ED2F, 0x1ED3D), // Ottoman Siyaq Numbers
            (0x1F100, 0x1F10C), // Enclosed Alphanumeric Supplement (digit forms)
        ],
        // Cf (Format)
        ["Cf"] = [
            (0x110BD, 0x110BD), // Kaithi number sign
            (0x110CD, 0x110CD), // Kaithi number sign above
            (0x13430, 0x1345F), // Egyptian hieroglyph format controls
            (0x1BCA0, 0x1BCA3), // Shorthand format controls
            (0x1D173, 0x1D17A), // Musical symbol formatting
            (0xE0001, 0xE0001), // Language tag
            (0xE0020, 0xE007F), // Tag characters
        ],
        // Co (Private Use)
        ["Co"] = [
            (0xF0000, 0xFFFFD),   // Supplementary Private Use Area-A
            (0x100000, 0x10FFFD), // Supplementary Private Use Area-B
        ],
        // L (Letter — union of Lu, Ll, Lt, Lm, Lo)
        ["L"] = [
            (0x10000, 0x1000B), (0x1000D, 0x10026), (0x10028, 0x1003A), (0x1003C, 0x1003D),
            (0x1003F, 0x1004D), (0x10050, 0x1005D), (0x10080, 0x100FA),
            (0x10280, 0x1029C), (0x102A0, 0x102D0),
            (0x10300, 0x10323), (0x1032D, 0x1034A),
            (0x10350, 0x10375),
            (0x10400, 0x1049D), // Deseret + Shavian
            (0x104B0, 0x104D3), (0x104D8, 0x104FB), // Osage
            (0x10500, 0x10527), // Elbasan
            (0x10530, 0x10563), // Caucasian Albanian
            (0x10570, 0x1057A), (0x1057C, 0x1058A), (0x1058C, 0x10592), (0x10594, 0x10595),
            (0x10597, 0x105A1), (0x105A3, 0x105B1), (0x105B3, 0x105B9), (0x105BB, 0x105BC),
            (0x10600, 0x10736), // Linear A
            (0x10800, 0x10855), // Cypriot
            (0x10860, 0x10876), // Palmyrene
            (0x10900, 0x10915), // Phoenician
            (0x10920, 0x1092B),
            (0x10980, 0x109B7),
            (0x10A00, 0x10A00), (0x10A10, 0x10A13), (0x10A15, 0x10A17), (0x10A19, 0x10A35),
            (0x10A60, 0x10A7C),
            (0x10AC0, 0x10AC7), (0x10AC9, 0x10AE4),
            (0x10B00, 0x10B35),
            (0x10B40, 0x10B55),
            (0x10B60, 0x10B72),
            (0x10B80, 0x10B91),
            (0x10C00, 0x10C48),
            (0x10C80, 0x10CB2), (0x10CC0, 0x10CF2),
            (0x10D00, 0x10D23),
            (0x10E80, 0x10EA9),
            (0x10EB0, 0x10EB1),
            (0x10F00, 0x10F1C), (0x10F27, 0x10F27), (0x10F30, 0x10F45),
            (0x10FB0, 0x10FC4),
            (0x10FE0, 0x10FF6),
            (0x11003, 0x11037), // Brahmi
            (0x11071, 0x11072), (0x11075, 0x11075),
            (0x11083, 0x110AF),
            (0x110D0, 0x110E8),
            (0x11103, 0x11126),
            (0x11144, 0x11147),
            (0x11150, 0x11172), (0x11176, 0x11176),
            (0x11183, 0x111B2),
            (0x111C1, 0x111C4),
            (0x11200, 0x11211), (0x11213, 0x1122B),
            (0x11280, 0x11286), (0x11288, 0x11288), (0x1128A, 0x1128D), (0x1128F, 0x1129D), (0x1129F, 0x112A8),
            (0x11300, 0x11303),
            (0x11305, 0x1130C), (0x1130F, 0x11310), (0x11313, 0x11328), (0x1132A, 0x11330),
            (0x11332, 0x11333), (0x11335, 0x11339), (0x1133D, 0x1133D),
            (0x11350, 0x11350),
            (0x1135D, 0x11361),
            (0x11400, 0x11434),
            (0x11447, 0x1144A),
            (0x1145F, 0x11461),
            (0x11480, 0x114AF),
            (0x114C4, 0x114C5), (0x114C7, 0x114C7),
            (0x11580, 0x115AE),
            (0x115D8, 0x115DB),
            (0x11600, 0x1162F),
            (0x11644, 0x11644),
            (0x11680, 0x116AA),
            (0x116B8, 0x116B8),
            (0x11700, 0x1171A),
            (0x11740, 0x11746),
            (0x11800, 0x1182B),
            (0x118A0, 0x118DF), (0x118FF, 0x11906),
            (0x11909, 0x11909),
            (0x1190C, 0x11913), (0x11915, 0x11916), (0x11918, 0x1192F),
            (0x1193F, 0x1193F), (0x11941, 0x11941),
            (0x119A0, 0x119A7), (0x119AA, 0x119D0),
            (0x119E1, 0x119E1), (0x119E3, 0x119E3),
            (0x11A00, 0x11A00), (0x11A0B, 0x11A32),
            (0x11A3A, 0x11A3A),
            (0x11A50, 0x11A50), (0x11A5C, 0x11A89),
            (0x11A9D, 0x11A9D),
            (0x11AB0, 0x11AF8),
            (0x11C00, 0x11C08), (0x11C0A, 0x11C2E),
            (0x11C40, 0x11C40),
            (0x11C72, 0x11C8F),
            (0x11D00, 0x11D06), (0x11D08, 0x11D09), (0x11D0B, 0x11D30),
            (0x11D46, 0x11D46),
            (0x11D60, 0x11D65), (0x11D67, 0x11D68), (0x11D6A, 0x11D89),
            (0x11D98, 0x11D98),
            (0x11EE0, 0x11EF2),
            (0x11FB0, 0x11FB0),
            (0x12000, 0x12399), // Cuneiform
            (0x12480, 0x12543),
            (0x12F90, 0x12FF0),
            (0x13000, 0x1342E), // Egyptian Hieroglyphs
            (0x14400, 0x14646), // Anatolian Hieroglyphs
            (0x16800, 0x16A38), // Bamum Supplement
            (0x16A40, 0x16A5E), // Mro
            (0x16A70, 0x16ABE),
            (0x16AD0, 0x16AED),
            (0x16B00, 0x16B2F),
            (0x16B40, 0x16B43),
            (0x16B63, 0x16B77), (0x16B7D, 0x16B8F),
            (0x16E40, 0x16E7F), // Medefaidrin
            (0x16F00, 0x16F4A), (0x16F50, 0x16F50), (0x16F93, 0x16F9F),
            (0x16FE0, 0x16FE1), (0x16FE3, 0x16FE3),
            (0x17000, 0x187F7), // Tangut
            (0x18800, 0x18CD5),
            (0x18D00, 0x18D08),
            (0x1AFF0, 0x1AFF3), (0x1AFF5, 0x1AFFB), (0x1AFFD, 0x1AFFE),
            (0x1B000, 0x1B122), (0x1B150, 0x1B152), (0x1B164, 0x1B167),
            (0x1B170, 0x1B2FB),
            (0x1BC00, 0x1BC6A), (0x1BC70, 0x1BC7C), (0x1BC80, 0x1BC88), (0x1BC90, 0x1BC99),
            (0x1D400, 0x1D454), // Mathematical Alphanumeric Symbols (letters)
            (0x1D456, 0x1D49C), (0x1D49E, 0x1D49F), (0x1D4A2, 0x1D4A2),
            (0x1D4A5, 0x1D4A6), (0x1D4A9, 0x1D4AC), (0x1D4AE, 0x1D4B9),
            (0x1D4BB, 0x1D4BB), (0x1D4BD, 0x1D4C3), (0x1D4C5, 0x1D505),
            (0x1D507, 0x1D50A), (0x1D50D, 0x1D514), (0x1D516, 0x1D51C),
            (0x1D51E, 0x1D539), (0x1D53B, 0x1D53E), (0x1D540, 0x1D544),
            (0x1D546, 0x1D546), (0x1D54A, 0x1D550), (0x1D552, 0x1D6A5),
            (0x1D6A8, 0x1D7CB),
            (0x1E100, 0x1E12C), (0x1E137, 0x1E13D),
            (0x1E14E, 0x1E14E),
            (0x1E290, 0x1E2AD),
            (0x1E2C0, 0x1E2EB),
            (0x1E4D0, 0x1E4EB),
            (0x1E7E0, 0x1E7E6), (0x1E7E8, 0x1E7EB), (0x1E7ED, 0x1E7EE), (0x1E7F0, 0x1E7FE),
            (0x1E800, 0x1E8C4), // Mende Kikakui
            (0x1E900, 0x1E943), // Adlam
            (0x1EE00, 0x1EE03), (0x1EE05, 0x1EE1F), (0x1EE21, 0x1EE22), (0x1EE24, 0x1EE24),
            (0x1EE27, 0x1EE27), (0x1EE29, 0x1EE32), (0x1EE34, 0x1EE37), (0x1EE39, 0x1EE39),
            (0x1EE3B, 0x1EE3B), (0x1EE42, 0x1EE42), (0x1EE47, 0x1EE47), (0x1EE49, 0x1EE49),
            (0x1EE4B, 0x1EE4B), (0x1EE4D, 0x1EE4F), (0x1EE51, 0x1EE52), (0x1EE54, 0x1EE54),
            (0x1EE57, 0x1EE57), (0x1EE59, 0x1EE59), (0x1EE5B, 0x1EE5B), (0x1EE5D, 0x1EE5D),
            (0x1EE5F, 0x1EE5F), (0x1EE61, 0x1EE62), (0x1EE64, 0x1EE64), (0x1EE67, 0x1EE6A),
            (0x1EE6C, 0x1EE72), (0x1EE74, 0x1EE77), (0x1EE79, 0x1EE7C), (0x1EE7E, 0x1EE7E),
            (0x1EE80, 0x1EE89), (0x1EE8B, 0x1EE9B), (0x1EEA1, 0x1EEA3), (0x1EEA5, 0x1EEA9),
            (0x1EEAB, 0x1EEBB),
            (0x20000, 0x2A6DF), // CJK Unified Ideographs Extension B
            (0x2A700, 0x2B739), // CJK Extension C
            (0x2B740, 0x2B81D), // CJK Extension D
            (0x2B820, 0x2CEA1), // CJK Extension E
            (0x2CEB0, 0x2EBE0), // CJK Extension F
            (0x2F800, 0x2FA1D), // CJK Compatibility Ideographs Supplement
            (0x30000, 0x3134A), // CJK Extension G
            (0x31350, 0x323AF), // CJK Extension H
        ],
        // N (Number — union of Nd, Nl, No)
        ["N"] = [
            (0x10107, 0x10133), // Aegean numbers
            (0x10140, 0x10178), // Greek acrophonic numerals
            (0x1018A, 0x1018B),
            (0x102E1, 0x102FB),
            (0x10320, 0x10323),
            (0x10341, 0x10341), (0x1034A, 0x1034A),
            (0x103D1, 0x103D5),
            (0x104A0, 0x104A9), // Osmanya digits
            (0x10858, 0x1085F),
            (0x10879, 0x1087F),
            (0x108A7, 0x108AF),
            (0x108FB, 0x108FF),
            (0x10916, 0x1091B),
            (0x109BC, 0x109BD), (0x109C0, 0x109CF), (0x109D2, 0x109FF),
            (0x10A40, 0x10A48),
            (0x10A7D, 0x10A7E),
            (0x10A9D, 0x10A9F),
            (0x10AEB, 0x10AEF),
            (0x10B58, 0x10B5F),
            (0x10B78, 0x10B7F),
            (0x10BA9, 0x10BAF),
            (0x10CFA, 0x10CFF),
            (0x10D30, 0x10D39),
            (0x10E60, 0x10E7E),
            (0x10F1D, 0x10F26),
            (0x10F51, 0x10F54),
            (0x10FC5, 0x10FCB),
            (0x11052, 0x1106F),
            (0x110F0, 0x110F9),
            (0x11136, 0x1113F),
            (0x111D0, 0x111D9),
            (0x111E1, 0x111F4),
            (0x112F0, 0x112F9),
            (0x11450, 0x11459),
            (0x114D0, 0x114D9),
            (0x11650, 0x11659),
            (0x116C0, 0x116C9),
            (0x11730, 0x1173B),
            (0x118E0, 0x118F2),
            (0x11950, 0x11959),
            (0x11C50, 0x11C6C),
            (0x11D50, 0x11D59),
            (0x11DA0, 0x11DA9),
            (0x11F50, 0x11F59),
            (0x16A60, 0x16A69),
            (0x16AC0, 0x16AC9),
            (0x16B50, 0x16B59), (0x16B5B, 0x16B61),
            (0x16E80, 0x16E96),
            (0x1D7CE, 0x1D7FF), // Mathematical digits
            (0x1E140, 0x1E149),
            (0x1E2F0, 0x1E2F9),
            (0x1E4F0, 0x1E4F9),
            (0x1E8C7, 0x1E8CF),
            (0x1E950, 0x1E959),
            (0x1EC71, 0x1ECAB), (0x1ECAD, 0x1ECAF), (0x1ECB1, 0x1ECB4),
            (0x1ED01, 0x1ED2D), (0x1ED2F, 0x1ED3D),
            (0x1F100, 0x1F10C),
            (0x1FBF0, 0x1FBF9),
        ],
        // M (Mark — union of Mn, Mc, Me)
        ["M"] = [
            (0x10A01, 0x10A03), (0x10A05, 0x10A06), (0x10A0C, 0x10A0F),
            (0x10A38, 0x10A3A), (0x10A3F, 0x10A3F),
            (0x10AE5, 0x10AE6),
            (0x10D24, 0x10D27),
            (0x10EAB, 0x10EAC),
            (0x10F46, 0x10F50),
            (0x10F82, 0x10F85),
            (0x11000, 0x11002),
            (0x11038, 0x11046),
            (0x11070, 0x11070), (0x11073, 0x11074),
            (0x1107F, 0x11082),
            (0x110B0, 0x110BA),
            (0x11100, 0x11102),
            (0x11127, 0x11134),
            (0x11145, 0x11146),
            (0x11173, 0x11173),
            (0x11180, 0x11182),
            (0x111B3, 0x111C0),
            (0x111C9, 0x111CC),
            (0x111CE, 0x111CF),
            (0x1122C, 0x11237),
            (0x1123E, 0x1123E),
            (0x112DF, 0x112EA),
            (0x11300, 0x11303),
            (0x1133B, 0x1133C),
            (0x1133E, 0x11344), (0x11347, 0x11348), (0x1134B, 0x1134D),
            (0x11357, 0x11357),
            (0x11362, 0x11363), (0x11366, 0x1136C), (0x11370, 0x11374),
            (0x11435, 0x11446),
            (0x1145E, 0x1145E),
            (0x114B0, 0x114C3),
            (0x115AF, 0x115B5), (0x115B8, 0x115C0), (0x115DC, 0x115DD),
            (0x11630, 0x11640),
            (0x116AB, 0x116B7),
            (0x1171D, 0x1172B),
            (0x1182C, 0x1183A),
            (0x11930, 0x11935), (0x11937, 0x11938), (0x1193B, 0x1193E),
            (0x11940, 0x11940), (0x11942, 0x11943),
            (0x119D1, 0x119D7), (0x119DA, 0x119E0), (0x119E4, 0x119E4),
            (0x11A01, 0x11A0A), (0x11A33, 0x11A39), (0x11A3B, 0x11A3E),
            (0x11A47, 0x11A47),
            (0x11A51, 0x11A5B), (0x11A8A, 0x11A99),
            (0x11C2F, 0x11C36), (0x11C38, 0x11C3F),
            (0x11C92, 0x11CA7), (0x11CA9, 0x11CB6),
            (0x11D31, 0x11D36), (0x11D3A, 0x11D3A), (0x11D3C, 0x11D3D), (0x11D3F, 0x11D45),
            (0x11D47, 0x11D47),
            (0x11D8A, 0x11D8E), (0x11D90, 0x11D91), (0x11D93, 0x11D97),
            (0x11EF3, 0x11EF6),
            (0x11F00, 0x11F01), (0x11F03, 0x11F03), (0x11F34, 0x11F3A), (0x11F3E, 0x11F42),
            (0x13440, 0x13440), (0x13447, 0x13455),
            (0x16AF0, 0x16AF4),
            (0x16B30, 0x16B36),
            (0x16F4F, 0x16F4F), (0x16F51, 0x16F87), (0x16F8F, 0x16F92),
            (0x16FE4, 0x16FE4),
            (0x1BC9D, 0x1BC9E),
            (0x1CF00, 0x1CF2D), (0x1CF30, 0x1CF46),
            (0x1D165, 0x1D169), (0x1D16D, 0x1D172),
            (0x1D17B, 0x1D182), (0x1D185, 0x1D18B), (0x1D1AA, 0x1D1AD),
            (0x1D242, 0x1D244),
            (0x1DA00, 0x1DA36), (0x1DA3B, 0x1DA6C), (0x1DA75, 0x1DA75), (0x1DA84, 0x1DA84),
            (0x1DA9B, 0x1DA9F), (0x1DAA1, 0x1DAAF),
            (0x1E000, 0x1E006), (0x1E008, 0x1E018), (0x1E01B, 0x1E021), (0x1E023, 0x1E024),
            (0x1E026, 0x1E02A),
            (0x1E130, 0x1E136),
            (0x1E2AE, 0x1E2AE),
            (0x1E2EC, 0x1E2EF),
            (0x1E4EC, 0x1E4EF),
            (0x1E8D0, 0x1E8D6),
            (0x1E944, 0x1E94A),
        ],
    };

    /// <summary>
    /// Builds a surrogate pair alternation for all supplementary ranges of a Unicode category.
    /// Returns null if the category has no known supplementary ranges.
    /// </summary>
    private static string? GetSupplementaryPatternForCategory(string categoryName)
    {
        if (!SupplementaryCategoryRanges.TryGetValue(categoryName, out var ranges))
            return null;

        var sb = new System.Text.StringBuilder();
        foreach (var (start, end) in ranges)
        {
            if (sb.Length > 0) sb.Append('|');
            sb.Append(SupplementaryRangeToSurrogatePairs(start, end));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Converts XSD/XPath-specific regex features to .NET equivalents:
    /// - \i → XML NameStartChar, \I → not NameStartChar
    /// - \c → XML NameChar, \C → not NameChar
    /// - \w → XSD word char (different from .NET \w), \W → complement
    /// - \d / \D → augmented with supplementary plane digits
    /// - \p{IsXxx} → surrogate pair ranges for unsupported Unicode blocks
    /// - \p{Lu} etc. → augmented with supplementary plane ranges
    /// - Non-BMP characters inside character classes → surrogate pair alternations
    /// </summary>
    public static string ConvertXsdEscapesToNet(string pattern)
    {
        // Quick check: if no special constructs and no surrogates, return as-is
        bool hasSpecial = false;
        for (int i = 0; i < pattern.Length; i++)
        {
            if (char.IsHighSurrogate(pattern[i]))
            {
                hasSpecial = true;
                break;
            }
            if (i + 1 < pattern.Length && pattern[i] == '\\' && (pattern[i + 1] is 'c' or 'C' or 'i' or 'I'
                or 'p' or 'P' or 'w' or 'W' or 'd' or 'D'))
            {
                hasSpecial = true;
                break;
            }
        }
        if (!hasSpecial) return pattern;

        // Phase 1: Rewrite character classes that contain negated multi-char escapes (\C, \I)
        // or surrogate pairs. These need to be split into alternations.
        pattern = RewriteComplexCharClasses(pattern);

        var sb = new System.Text.StringBuilder(pattern.Length + 64);
        bool inCharClass = false;

        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];

            if (c == '\\' && i + 1 < pattern.Length)
            {
                char next = pattern[i + 1];
                switch (next)
                {
                    case 'c':
                        sb.Append(inCharClass ? NameCharClass : $"[{NameCharClass}]");
                        i++;
                        continue;
                    case 'C':
                        // Outside char class: [^NameCharClass]. Inside char class is handled
                        // by RewriteComplexCharClasses which extracts \C into an alternation.
                        sb.Append($"[^{NameCharClass}]");
                        i++;
                        continue;
                    case 'i':
                        sb.Append(inCharClass ? NameStartCharClass : $"[{NameStartCharClass}]");
                        i++;
                        continue;
                    case 'I':
                        // Outside char class: [^NameStartCharClass]. Inside char class is handled
                        // by RewriteComplexCharClasses which extracts \I into an alternation.
                        sb.Append($"[^{NameStartCharClass}]");
                        i++;
                        continue;
                    case 'w':
                    {
                        // XSD \w = [^\p{P}\p{Z}\p{C}] — everything except punctuation, separators, and other.
                        // This notably EXCLUDES underscore (which is \p{Pc}).
                        if (inCharClass)
                            sb.Append(XsdWordClass);
                        else
                            sb.Append(XsdWordClassFull);
                        i++;
                        continue;
                    }
                    case 'W':
                    {
                        // XSD \W = [\p{P}\p{Z}\p{C}]
                        if (inCharClass)
                            sb.Append(@"\p{P}\p{Z}\p{C}");
                        else
                            sb.Append(XsdNonWordClassFull);
                        i++;
                        continue;
                    }
                    case 'd':
                    {
                        // XSD \d matches \p{Nd} including supplementary plane digits
                        var suppDigits = GetSupplementaryPatternForCategory("Nd");
                        if (inCharClass)
                        {
                            // Inside char class, we can only include BMP \p{Nd};
                            // supplementary digits require alternation which is handled by
                            // RewriteComplexCharClasses for \d inside char classes.
                            sb.Append(@"\p{Nd}");
                        }
                        else
                        {
                            sb.Append($"(?:\\p{{Nd}}|{suppDigits})");
                        }
                        i++;
                        continue;
                    }
                    case 'D':
                    {
                        // XSD \D = not \d. For BMP, \P{Nd} works. Non-BMP digits are rare
                        // enough that excluding them via negative lookahead is impractical.
                        // We use \P{Nd} and accept that a few non-BMP digits won't be excluded.
                        // However, for the test suite the \D tests check that non-BMP NON-digits
                        // are matched, which \P{Nd} handles correctly for BMP, and the
                        // supplementary range handling of surrounding context handles for non-BMP.
                        var suppDigits = GetSupplementaryPatternForCategory("Nd");
                        if (inCharClass)
                        {
                            sb.Append(@"\P{Nd}");
                        }
                        else
                        {
                            // Match any char (including surrogate pair) that is NOT a digit
                            sb.Append($"(?:(?!(?:{suppDigits}))(?:[\\uD800-\\uDBFF][\\uDC00-\\uDFFF]|\\P{{Nd}}))");
                        }
                        i++;
                        continue;
                    }
                    case 'p' or 'P' when i + 2 < pattern.Length && pattern[i + 2] == '{':
                    {
                        int closeBrace = pattern.IndexOf('}', i + 3);
                        if (closeBrace > 0)
                        {
                            var blockName = pattern[(i + 3)..closeBrace];
                            // Strip whitespace from block names (may appear when 'x' flag is active)
                            var normalizedName = blockName.Replace(" ", "", StringComparison.Ordinal);
                            if (UnsupportedUnicodeBlocks.TryGetValue(normalizedName, out var range))
                            {
                                var surrogatePattern = SupplementaryRangeToSurrogatePairs(range.Start, range.End);
                                if (next == 'P')
                                {
                                    sb.Append(c);
                                    sb.Append(pattern, i + 1, closeBrace - i);
                                }
                                else
                                {
                                    sb.Append(surrogatePattern);
                                }
                                i = closeBrace;
                                continue;
                            }

                            // Check if this general category has supplementary plane ranges
                            var suppPattern = GetSupplementaryPatternForCategory(normalizedName);
                            if (suppPattern != null && !inCharClass)
                            {
                                if (next == 'p')
                                {
                                    sb.Append($"(?:\\p{{{normalizedName}}}|{suppPattern})");
                                }
                                else
                                {
                                    // \P{Cat} = not in category. Must exclude supplementary ranges too.
                                    sb.Append($"(?:(?!(?:{suppPattern}))(?:[\\uD800-\\uDBFF][\\uDC00-\\uDFFF]|\\P{{{normalizedName}}}))");
                                }
                                i = closeBrace;
                                continue;
                            }
                        }
                        // Not an unsupported block and no supplementary ranges — pass through with normalized name
                        if (closeBrace > 0)
                        {
                            var rawName = pattern[(i + 3)..closeBrace];
                            var stripped = rawName.Replace(" ", "", StringComparison.Ordinal);
                            sb.Append('\\').Append(next).Append('{').Append(stripped).Append('}');
                            i = closeBrace;
                        }
                        else
                        {
                            sb.Append(c);
                            sb.Append(next);
                            i++;
                        }
                        continue;
                    }
                    default:
                        sb.Append(c);
                        sb.Append(next);
                        i++;
                        continue;
                }
            }

            if (c == '[' && !inCharClass)
            {
                inCharClass = true;
                sb.Append(c);
                continue;
            }
            if (c == ']' && inCharClass)
            {
                inCharClass = false;
                sb.Append(c);
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Rewrites character classes that contain constructs incompatible with .NET char classes:
    /// 1. Negated multi-char escapes (\C, \I) inside [...] — converted to alternation groups.
    /// 2. Surrogate pairs inside [...] — extracted into alternations.
    /// E.g., [\C\?a-c] → (?:[^NameCharClass]|[\?a-c])
    /// E.g., [𐀀abc] → (?:\uD800\uDC00|[abc])
    /// </summary>
    private static string RewriteComplexCharClasses(string pattern)
    {
        // Quick scan: does any char class contain \C, \I, or surrogate pairs?
        bool needsRewrite = false;
        bool inCC = false;
        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];
            if (c == '\\' && i + 1 < pattern.Length)
            {
                if (inCC && pattern[i + 1] is 'C' or 'I')
                {
                    needsRewrite = true;
                    break;
                }
                i++; // skip escape
                continue;
            }
            if (!inCC && c == '[') { inCC = true; continue; }
            if (inCC && c == ']') { inCC = false; continue; }
            if (inCC && char.IsHighSurrogate(c))
            {
                needsRewrite = true;
                break;
            }
        }
        if (!needsRewrite) return pattern;

        var sb = new System.Text.StringBuilder(pattern.Length + 64);
        int pos = 0;

        while (pos < pattern.Length)
        {
            if (pattern[pos] == '\\' && pos + 1 < pattern.Length)
            {
                sb.Append(pattern[pos]);
                sb.Append(pattern[pos + 1]);
                // Skip \p{...} blocks
                if (pattern[pos + 1] is 'p' or 'P' && pos + 2 < pattern.Length && pattern[pos + 2] == '{')
                {
                    int cb = pattern.IndexOf('}', pos + 3);
                    if (cb > 0)
                    {
                        sb.Append(pattern, pos + 2, cb - pos - 1);
                        pos = cb + 1;
                        continue;
                    }
                }
                pos += 2;
                continue;
            }

            if (pattern[pos] != '[')
            {
                sb.Append(pattern[pos]);
                pos++;
                continue;
            }

            // Found '[' — parse the entire character class
            int classStart = pos;
            pos++; // skip '['
            bool negated = false;
            if (pos < pattern.Length && pattern[pos] == '^')
            {
                negated = true;
                pos++;
            }

            // Collect the character class content, tracking nested [...] for subtraction
            var members = new System.Text.StringBuilder();
            var negatedEscapes = new List<string>(); // \C, \I entries
            var surrogatePairs = new List<string>();  // surrogate pair entries
            int depth = 1;

            while (pos < pattern.Length && depth > 0)
            {
                char ch = pattern[pos];

                if (ch == '\\' && pos + 1 < pattern.Length)
                {
                    char esc = pattern[pos + 1];
                    if (depth == 1 && esc is 'C' or 'I')
                    {
                        // Extract negated escape
                        negatedEscapes.Add(esc == 'C' ? $"[^{NameCharClass}]" : $"[^{NameStartCharClass}]");
                        pos += 2;
                        continue;
                    }
                    // \p{...} or \P{...}
                    if (esc is 'p' or 'P' && pos + 2 < pattern.Length && pattern[pos + 2] == '{')
                    {
                        int cb = pattern.IndexOf('}', pos + 3);
                        if (cb > 0)
                        {
                            members.Append(pattern, pos, cb - pos + 1);
                            pos = cb + 1;
                            continue;
                        }
                    }
                    members.Append(ch);
                    members.Append(esc);
                    pos += 2;
                    continue;
                }

                if (ch == '[')
                {
                    depth++;
                    members.Append(ch);
                    pos++;
                    continue;
                }
                if (ch == ']')
                {
                    depth--;
                    if (depth > 0)
                    {
                        members.Append(ch);
                    }
                    pos++;
                    continue;
                }

                // Check for surrogate pairs at depth 1
                if (depth == 1 && char.IsHighSurrogate(ch) && pos + 1 < pattern.Length && char.IsLowSurrogate(pattern[pos + 1]))
                {
                    int cp = char.ConvertToUtf32(ch, pattern[pos + 1]);
                    // Check if this is part of a range: surr-surr
                    if (pos + 2 < pattern.Length && pattern[pos + 2] == '-' &&
                        pos + 3 < pattern.Length && char.IsHighSurrogate(pattern[pos + 3]) &&
                        pos + 4 < pattern.Length && char.IsLowSurrogate(pattern[pos + 4]))
                    {
                        int cpEnd = char.ConvertToUtf32(pattern[pos + 3], pattern[pos + 4]);
                        surrogatePairs.Add(SupplementaryRangeToSurrogatePairs(cp, cpEnd));
                        pos += 5; // skip surr-surr
                        continue;
                    }
                    // Single supplementary char
                    surrogatePairs.Add(SupplementaryRangeToSurrogatePairs(cp, cp));
                    pos += 2;
                    continue;
                }

                members.Append(ch);
                pos++;
            }

            // If no complex elements found, output original char class
            if (negatedEscapes.Count == 0 && surrogatePairs.Count == 0)
            {
                sb.Append(pattern, classStart, pos - classStart);
                continue;
            }

            // Build alternation group
            var parts = new List<string>();
            foreach (var ne in negatedEscapes)
                parts.Add(ne);
            foreach (var sp in surrogatePairs)
                parts.Add(sp);

            string memberStr = members.ToString();
            if (memberStr.Length > 0)
            {
                parts.Add(negated ? $"[^{memberStr}]" : $"[{memberStr}]");
            }
            else if (negated && negatedEscapes.Count == 0)
            {
                // Empty negated class with only surrogate pairs — unlikely but handle
            }

            if (parts.Count == 1)
                sb.Append(parts[0]);
            else
                sb.Append("(?:").Append(string.Join('|', parts)).Append(')');
        }

        return sb.ToString();
    }

    /// <summary>
    /// Converts XPath regex pattern backreferences to unambiguous .NET syntax.
    /// XPath \19 with 2 groups = \1 + literal 9; .NET would misinterpret as octal.
    /// Uses \k&lt;N&gt; syntax for disambiguation.
    /// </summary>
    public static string ConvertXPathPatternToNet(string pattern)
    {
        int groupCount = CountCapturingGroups(pattern);
        if (groupCount == 0) return pattern;

        var sb = new System.Text.StringBuilder(pattern.Length + 8);
        bool inCharClass = false;

        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];

            if (inCharClass)
            {
                sb.Append(c);
                if (c == '\\' && i + 1 < pattern.Length)
                {
                    i++;
                    sb.Append(pattern[i]);
                }
                else if (c == ']')
                {
                    inCharClass = false;
                }
                continue;
            }

            if (c == '[')
            {
                inCharClass = true;
                sb.Append(c);
                continue;
            }

            if (c == '\\' && i + 1 < pattern.Length)
            {
                char next = pattern[i + 1];
                if (char.IsAsciiDigit(next) && next != '0')
                {
                    // Backreference: collect all following digits
                    int digitStart = i + 1;
                    int digitEnd = digitStart;
                    while (digitEnd < pattern.Length && char.IsAsciiDigit(pattern[digitEnd]))
                        digitEnd++;
                    string allDigits = pattern[digitStart..digitEnd];
                    int fullNum = int.Parse(allDigits);

                    // Find longest valid prefix (must be ≤ groupCount)
                    int validLen = allDigits.Length;
                    int validNum = fullNum;
                    while (validNum > groupCount && validLen > 1)
                    {
                        validLen--;
                        validNum = int.Parse(allDigits[..validLen]);
                    }

                    if (validNum >= 1 && validNum <= groupCount)
                    {
                        if (validLen < allDigits.Length)
                        {
                            // Need to disambiguate: \k<N> prevents .NET from
                            // combining the backreference digits with trailing literal digits
                            sb.Append("\\k<").Append(validNum).Append('>');
                            sb.Append(allDigits[validLen..]);
                        }
                        else
                        {
                            // Full number is a valid backreference, output as-is
                            sb.Append('\\').Append(allDigits);
                        }
                    }
                    else
                    {
                        // No valid backreference at all, output as-is
                        sb.Append('\\').Append(allDigits);
                    }

                    i = digitEnd - 1; // loop will increment
                }
                else
                {
                    // Other escape, output as-is
                    sb.Append('\\').Append(next);
                    i++;
                }
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Converts an XPath fn:replace replacement string to .NET Regex replacement syntax.
    /// Handles: \$ → $$ (literal $), \\ → \ (literal \), $N → ${N} (group ref).
    /// For multi-digit $N, uses longest-valid-prefix matching per XPath spec.
    /// Non-existent group references produce empty string.
    /// </summary>
    public static string ConvertXPathReplacementToNet(string replacement, int groupCount)
    {
        var sb = new System.Text.StringBuilder(replacement.Length + 4);
        for (int i = 0; i < replacement.Length; i++)
        {
            var c = replacement[i];
            if (c == '\\')
            {
                if (i + 1 >= replacement.Length)
                    throw new InvalidOperationException(
                        "FORX0004: Invalid replacement string: '\\' at end of string");
                var next = replacement[i + 1];
                if (next == '$')
                {
                    sb.Append("$$");
                    i++;
                }
                else if (next == '\\')
                {
                    sb.Append('\\');
                    i++;
                }
                else
                {
                    throw new InvalidOperationException(
                        $"FORX0004: Invalid replacement string: '\\{next}' is not a valid escape sequence");
                }
            }
            else if (c == '$')
            {
                if (i + 1 < replacement.Length && char.IsAsciiDigit(replacement[i + 1]))
                {
                    // Collect all digits
                    int digitStart = i + 1;
                    int digitEnd = digitStart;
                    while (digitEnd < replacement.Length && char.IsAsciiDigit(replacement[digitEnd]))
                        digitEnd++;
                    string allDigits = replacement[digitStart..digitEnd];
                    int fullNum = int.Parse(allDigits);

                    // $0 always refers to entire match
                    if (fullNum == 0)
                    {
                        sb.Append("$0");
                        i = digitEnd - 1;
                        continue;
                    }

                    // Find longest valid prefix ≤ groupCount
                    int validLen = allDigits.Length;
                    int validNum = fullNum;
                    while (validNum > groupCount && validLen > 1)
                    {
                        validLen--;
                        validNum = int.Parse(allDigits[..validLen]);
                    }

                    if (validNum >= 1 && validNum <= groupCount)
                    {
                        // Use ${N} for unambiguous .NET group reference
                        sb.Append("${").Append(validNum).Append('}');
                        // Remaining digits are literal
                        sb.Append(allDigits[validLen..]);
                    }
                    // else: no valid group → replace with empty string (output nothing)

                    i = digitEnd - 1;
                }
                else
                {
                    throw new InvalidOperationException(
                        "FORX0004: Invalid replacement string: '$' must be followed by a digit");
                }
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Overload for backward compatibility — counts groups from pattern automatically.
    /// </summary>
    public static string ConvertXPathReplacementToNet(string replacement, string pattern)
    {
        return ConvertXPathReplacementToNet(replacement, CountCapturingGroups(pattern));
    }
}

/// <summary>
/// fn:tokenize($input) as xs:string* — 1-arg tokenize splits on whitespace
/// </summary>
public sealed class Tokenize1Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "tokenize");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.String, Occurrence = Occurrence.ZeroOrMore };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "input"), Type = XdmSequenceType.OptionalString }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        // Per XPath 3.1 §5.4.3.2: 1-arg tokenize splits on XML whitespace only
        // (U+0020, U+0009, U+000A, U+000D), NOT Unicode whitespace like NBSP.
        var raw = ConcatFunction.XQueryStringValue(Execution.QueryExecutionContext.Atomize(arguments[0]));
        var input = raw.Trim(' ', '\t', '\n', '\r');
        if (string.IsNullOrEmpty(input))
            return ValueTask.FromResult<object?>(Array.Empty<string>());

        var tokens = System.Text.RegularExpressions.Regex.Split(input, @"[ \t\n\r]+");
        return ValueTask.FromResult<object?>(tokens);
    }
}

/// <summary>
/// fn:compare($comparand1, $comparand2, $collation) as xs:integer?
/// </summary>
public sealed class Compare3Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "compare");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Integer, Occurrence = Occurrence.ZeroOrOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "comparand1"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "comparand2"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "collation"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var a0 = Execution.QueryExecutionContext.Atomize(arguments[0]);
        var a1 = Execution.QueryExecutionContext.Atomize(arguments[1]);
        if (a0 == null || a1 == null)
            return ValueTask.FromResult<object?>(null);
        StringLengthFunction.RequireStringLike(a0, "compare");
        StringLengthFunction.RequireStringLike(a1, "compare");
        var s1 = ConcatFunction.XQueryStringValue(a0);
        var s2 = ConcatFunction.XQueryStringValue(a1);
        var collUri = arguments[2]?.ToString();
        var cmp = CollationHelper.CompareWithCollation(s1, s2, collUri);
        return ValueTask.FromResult<object?>((long)Math.Sign(cmp));
    }
}

/// <summary>
/// fn:starts-with($arg1, $arg2, $collation) as xs:boolean
/// </summary>
public sealed class StartsWith3Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "starts-with");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "arg1"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "arg2"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "collation"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var str = ConcatFunction.XQueryStringValue(Execution.QueryExecutionContext.Atomize(arguments[0]));
        var prefix = ConcatFunction.XQueryStringValue(Execution.QueryExecutionContext.Atomize(arguments[1]));
        var comparison = CollationHelper.GetStringComparison(arguments[2]?.ToString());
        return ValueTask.FromResult<object?>(str.StartsWith(prefix, comparison));
    }
}

/// <summary>
/// fn:ends-with($arg1, $arg2, $collation) as xs:boolean
/// </summary>
public sealed class EndsWith3Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "ends-with");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "arg1"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "arg2"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "collation"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var str = ConcatFunction.XQueryStringValue(Execution.QueryExecutionContext.Atomize(arguments[0]));
        var suffix = ConcatFunction.XQueryStringValue(Execution.QueryExecutionContext.Atomize(arguments[1]));
        var comparison = CollationHelper.GetStringComparison(arguments[2]?.ToString());
        return ValueTask.FromResult<object?>(str.EndsWith(suffix, comparison));
    }
}

/// <summary>
/// fn:substring-before($arg1, $arg2, $collation) as xs:string
/// </summary>
public sealed class SubstringBefore3Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "substring-before");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "arg1"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "arg2"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "collation"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var str = ConcatFunction.XQueryStringValue(arguments[0]);
        var search = ConcatFunction.XQueryStringValue(arguments[1]);
        if (string.IsNullOrEmpty(search))
            return ValueTask.FromResult<object?>("");
        var comparison = CollationHelper.ResolveAndGetComparison(arguments[2]?.ToString(), context);
        var idx = str.IndexOf(search, comparison);
        return ValueTask.FromResult<object?>(idx < 0 ? "" : str[..idx]);
    }
}

/// <summary>
/// fn:substring-after($arg1, $arg2, $collation) as xs:string
/// </summary>
public sealed class SubstringAfter3Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "substring-after");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "arg1"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "arg2"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "collation"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var str = ConcatFunction.XQueryStringValue(arguments[0]);
        var search = ConcatFunction.XQueryStringValue(arguments[1]);
        if (string.IsNullOrEmpty(search))
            return ValueTask.FromResult<object?>(str);
        var comparison = CollationHelper.ResolveAndGetComparison(arguments[2]?.ToString(), context);
        var idx = str.IndexOf(search, comparison);
        return ValueTask.FromResult<object?>(idx < 0 ? "" : str[(idx + search.Length)..]);
    }
}

/// <summary>
/// fn:default-collation() as xs:string
/// </summary>
public sealed class DefaultCollationFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "default-collation");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        if (context is Execution.QueryExecutionContext qec && qec.DefaultCollation != null)
            return ValueTask.FromResult<object?>(qec.DefaultCollation);
        return ValueTask.FromResult<object?>("http://www.w3.org/2005/xpath-functions/collation/codepoint");
    }
}

/// <summary>
/// Helper for resolving collation URIs to StringComparison values.
/// Supports the standard XPath codepoint collation, HTML ASCII case-insensitive collation,
/// and UCA collations per XPath 3.1 section 5.3.4.
/// </summary>
internal static class CollationHelper
{
    private const string UcaPrefix = "http://www.w3.org/2013/collation/UCA";
    internal const string HtmlAsciiCaseInsensitiveUri = "http://www.w3.org/2005/xpath-functions/collation/html-ascii-case-insensitive";

    public static StringComparison GetStringComparison(string? collationUri)
    {
        // Guard against empty-sequence-as-array ToString → "System.Object[]"
        if (collationUri != null && collationUri.StartsWith("System.", StringComparison.Ordinal))
            collationUri = null;
        return collationUri switch
        {
            null or "" or "http://www.w3.org/2005/xpath-functions/collation/codepoint" => StringComparison.Ordinal,
            HtmlAsciiCaseInsensitiveUri => StringComparison.OrdinalIgnoreCase, // ASCII case-insensitive
            "http://www.w3.org/2010/09/qt-fots-catalog/collation/caseblind"
                or "http://www.w3.org/2005/xpath-functions/collation/caseblind" => StringComparison.OrdinalIgnoreCase,
            _ when collationUri.StartsWith(UcaPrefix, StringComparison.Ordinal)
                => MapUcaToStringComparison(collationUri),
            _ => throw new XQueryRuntimeException("FOCH0002",
                $"FOCH0002: Unknown collation: {collationUri}")
        };
    }

    /// <summary>
    /// Resolves a potentially relative collation URI against the static base URI,
    /// then delegates to the single-parameter overload.
    /// </summary>
    public static StringComparison ResolveAndGetComparison(string? collationUri, Ast.ExecutionContext context)
    {
        collationUri = ResolveCollationUri(collationUri, context);
        return GetStringComparison(collationUri);
    }

    /// <summary>
    /// Resolves a potentially relative collation URI against the static base URI.
    /// </summary>
    internal static string? ResolveCollationUri(string? collationUri, Ast.ExecutionContext context)
    {
        if (collationUri != null && !Uri.TryCreate(collationUri, UriKind.Absolute, out _))
        {
            var baseUri = context.StaticBaseUri;
            if (baseUri != null && Uri.TryCreate(baseUri, UriKind.Absolute, out var baseUriObj)
                && Uri.TryCreate(baseUriObj, collationUri, out var resolved))
            {
                collationUri = resolved.AbsoluteUri;
            }
        }
        return collationUri;
    }

    /// <summary>
    /// Compares two strings using Unicode codepoint ordering for the codepoint collation.
    /// For non-BMP characters, .NET's string.Compare with Ordinal uses UTF-16 code unit ordering,
    /// which differs from Unicode codepoint ordering. This method handles that correctly.
    /// </summary>
    public static int CompareStrings(string s1, string s2, StringComparison comparison)
    {
        if (comparison != StringComparison.Ordinal)
            return string.Compare(s1, s2, comparison);

        // For Ordinal (codepoint collation), use codepoint-by-codepoint comparison
        // to handle non-BMP characters correctly
        var e1 = System.Globalization.StringInfo.GetTextElementEnumerator(s1);
        var e2 = System.Globalization.StringInfo.GetTextElementEnumerator(s2);
        while (true)
        {
            var has1 = e1.MoveNext();
            var has2 = e2.MoveNext();
            if (!has1 && !has2) return 0;
            if (!has1) return -1;
            if (!has2) return 1;
            var cp1 = char.ConvertToUtf32(s1, e1.ElementIndex);
            var cp2 = char.ConvertToUtf32(s2, e2.ElementIndex);
            if (cp1 != cp2) return cp1.CompareTo(cp2);
        }
    }

    /// <summary>
    /// Compares two strings using the collation identified by its URI.
    /// For UCA collations, uses <see cref="System.Globalization.CompareInfo"/> for full fidelity (accent/case handling).
    /// </summary>
    public static int CompareWithCollation(string s1, string s2, string? collationUri)
    {
        if (collationUri != null && collationUri.StartsWith(UcaPrefix, StringComparison.Ordinal))
            return CompareUca(s1, s2, collationUri);
        if (collationUri == HtmlAsciiCaseInsensitiveUri)
            return CompareHtmlAsciiCaseInsensitive(s1, s2);
        return CompareStrings(s1, s2, GetStringComparison(collationUri));
    }

    /// <summary>
    /// HTML ASCII case-insensitive comparison: only folds ASCII A-Z (U+0041–U+005A)
    /// to lowercase before codepoint comparison. Non-ASCII characters compare by codepoint.
    /// </summary>
    private static int CompareHtmlAsciiCaseInsensitive(string s1, string s2)
    {
        int len = Math.Min(s1.Length, s2.Length);
        for (int i = 0; i < len; i++)
        {
            var c1 = s1[i];
            var c2 = s2[i];
            // Fold only ASCII uppercase A-Z to lowercase
            if (c1 >= 'A' && c1 <= 'Z') c1 = (char)(c1 + 32);
            if (c2 >= 'A' && c2 <= 'Z') c2 = (char)(c2 + 32);
            if (c1 != c2) return c1.CompareTo(c2);
        }
        return s1.Length.CompareTo(s2.Length);
    }

    /// <summary>
    /// Returns true if the given collation URI is the HTML ASCII case-insensitive collation.
    /// </summary>
    public static bool IsHtmlAsciiCaseInsensitive(string? collationUri) =>
        collationUri == HtmlAsciiCaseInsensitiveUri;

    /// <summary>
    /// Converts a string to ASCII-only lowercase (a-z). Non-ASCII characters are preserved as-is.
    /// This implements the HTML ASCII case-insensitive collation per XPath F&amp;O 5.3.6.
    /// </summary>
    internal static string AsciiLower(string s)
    {
        var chars = s.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (chars[i] >= 'A' && chars[i] <= 'Z')
                chars[i] = (char)(chars[i] + 32);
        }
        return new string(chars);
    }

    /// <summary>
    /// Parses a UCA collation URI and returns a <see cref="System.Globalization.CompareInfo"/>
    /// and <see cref="System.Globalization.CompareOptions"/> for locale-aware comparison.
    /// </summary>
    /// <remarks>
    /// Supports the XPath 3.1 section 5.3.4 UCA collation URI format:
    /// <c>http://www.w3.org/2013/collation/UCA?lang=en;strength=primary</c>
    /// <para>Recognized parameters:</para>
    /// <list type="bullet">
    ///   <item><description><c>lang</c> -- maps to a <see cref="System.Globalization.CultureInfo"/></description></item>
    ///   <item><description><c>strength</c> -- primary, secondary, tertiary (default), quaternary, identical</description></item>
    ///   <item><description><c>fallback</c> -- yes (default) or no; when no, throws FOCH0002 on unknown lang</description></item>
    /// </list>
    /// </remarks>
    public static (System.Globalization.CompareInfo CompareInfo, System.Globalization.CompareOptions Options) GetUcaCollation(string collationUri)
    {
        var parameters = ParseUcaParameters(collationUri);
        var fallback = !parameters.TryGetValue("fallback", out var fb) || !fb.Equals("no", StringComparison.OrdinalIgnoreCase);
        var compareInfo = ResolveCompareInfo(parameters, fallback);
        var options = MapStrengthToCompareOptions(parameters);
        return (compareInfo, options);
    }

    /// <summary>
    /// Extracts the caseFirst parameter from a UCA collation URI.
    /// Returns "lower", "upper", or null (off/default).
    /// </summary>
    internal static string? GetCaseFirst(string collationUri)
    {
        var parameters = ParseUcaParameters(collationUri);
        return parameters.TryGetValue("caseFirst", out var cf) ? cf.ToLowerInvariant() : null;
    }

    /// <summary>
    /// Compares two strings using UCA collation parameters extracted from the given URI.
    /// </summary>
    public static int CompareUca(string? s1, string? s2, string collationUri)
    {
        var (compareInfo, options) = GetUcaCollation(collationUri);
        var result = compareInfo.Compare(s1 ?? "", s2 ?? "", options);
        if (result != 0) return result;
        // Apply caseFirst tiebreaker when primary comparison is equal
        var caseFirst = GetCaseFirst(collationUri);
        if (caseFirst == "lower")
            return string.Compare(s1 ?? "", s2 ?? "", StringComparison.Ordinal);
        if (caseFirst == "upper")
            return -string.Compare(s1 ?? "", s2 ?? "", StringComparison.Ordinal);
        return 0;
    }

    /// <summary>
    /// Gets the StringComparison to use for the default collation from the execution context.
    /// </summary>
    public static StringComparison GetDefaultComparison(Ast.ExecutionContext context)
    {
        if (context is Execution.QueryExecutionContext qec && qec.DefaultCollation != null)
            return GetStringComparison(qec.DefaultCollation);
        return StringComparison.Ordinal;
    }

    /// <summary>
    /// Maps a UCA collation URI to the best available <see cref="StringComparison"/>.
    /// This provides a reasonable approximation; for full fidelity use <see cref="GetUcaCollation"/>.
    /// </summary>
    private static StringComparison MapUcaToStringComparison(string collationUri)
    {
        var parameters = ParseUcaParameters(collationUri);
        var fallback = !parameters.TryGetValue("fallback", out var fb) || !fb.Equals("no", StringComparison.OrdinalIgnoreCase);

        // Validate lang if fallback=no
        if (!fallback && parameters.TryGetValue("lang", out var lang))
        {
            try
            {
                _ = System.Globalization.CultureInfo.GetCultureInfo(lang);
            }
            catch (System.Globalization.CultureNotFoundException)
            {
                throw new XQueryRuntimeException("FOCH0002",
                    $"FOCH0002: UCA collation language '{lang}' is not supported and fallback=no");
            }
        }

        // Map strength to the best StringComparison approximation
        parameters.TryGetValue("strength", out var strength);
        return (strength?.ToLowerInvariant()) switch
        {
            "primary" => StringComparison.InvariantCultureIgnoreCase,     // ignore case + accents
            "secondary" => StringComparison.InvariantCultureIgnoreCase,   // ignore case
            null or "" or "tertiary" => StringComparison.InvariantCulture, // case-sensitive (default)
            "quaternary" => StringComparison.InvariantCulture,
            "identical" => StringComparison.InvariantCulture,
            _ => throw new XQueryRuntimeException("FOCH0002",
                $"FOCH0002: Unknown UCA collation strength '{strength}'")
        };
    }

    /// <summary>
    /// Parses the query string and semicolon-separated parameters from a UCA collation URI.
    /// </summary>
    private static Dictionary<string, string> ParseUcaParameters(string collationUri)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var queryIndex = collationUri.IndexOf('?');
        if (queryIndex < 0)
            return parameters;

        var queryString = collationUri[(queryIndex + 1)..];
        // UCA parameters are separated by ';' (per XPath spec) or '&'
        var pairs = queryString.Split([';', '&'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var pair in pairs)
        {
            var eqIndex = pair.IndexOf('=');
            if (eqIndex > 0)
            {
                var key = pair[..eqIndex].Trim();
                var val = pair[(eqIndex + 1)..].Trim();
                parameters[key] = val;
            }
        }
        return parameters;
    }

    /// <summary>
    /// Resolves the <c>lang</c> parameter to a <see cref="System.Globalization.CompareInfo"/>.
    /// </summary>
    private static System.Globalization.CompareInfo ResolveCompareInfo(
        Dictionary<string, string> parameters, bool fallback)
    {
        if (!parameters.TryGetValue("lang", out var lang) || string.IsNullOrEmpty(lang))
            return System.Globalization.CultureInfo.InvariantCulture.CompareInfo;

        try
        {
            return System.Globalization.CultureInfo.GetCultureInfo(lang).CompareInfo;
        }
        catch (System.Globalization.CultureNotFoundException)
        {
            if (!fallback)
                throw new XQueryRuntimeException("FOCH0002",
                    $"FOCH0002: UCA collation language '{lang}' is not supported and fallback=no");
            // fallback=yes (default): use invariant culture
            return System.Globalization.CultureInfo.InvariantCulture.CompareInfo;
        }
    }

    /// <summary>
    /// Maps the <c>strength</c> UCA parameter to .NET <see cref="System.Globalization.CompareOptions"/>.
    /// </summary>
    private static System.Globalization.CompareOptions MapStrengthToCompareOptions(
        Dictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("strength", out var strength) || string.IsNullOrEmpty(strength))
            return System.Globalization.CompareOptions.None; // tertiary (default): case & accent sensitive

        var caseLevel = parameters.TryGetValue("caseLevel", out var cl)
            && cl.Equals("yes", StringComparison.OrdinalIgnoreCase);

        return strength.ToLowerInvariant() switch
        {
            // Primary: ignore accents; also ignore case unless caseLevel=yes
            "primary" when caseLevel => System.Globalization.CompareOptions.IgnoreNonSpace,
            "primary" => System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace,
            "secondary" => System.Globalization.CompareOptions.IgnoreCase,
            "tertiary" => System.Globalization.CompareOptions.None,
            "quaternary" => System.Globalization.CompareOptions.None,
            "identical" => System.Globalization.CompareOptions.None,
            _ => throw new XQueryRuntimeException("FOCH0002",
                $"FOCH0002: Unknown UCA collation strength '{strength}'")
        };
    }
}
