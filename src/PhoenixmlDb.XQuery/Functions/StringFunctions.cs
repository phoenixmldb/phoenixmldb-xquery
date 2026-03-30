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
            netPattern = XQueryRegexHelper.FixDotForSurrogatePairs(netPattern);
            var options = System.Text.RegularExpressions.RegexOptions.None;
            if (flags != null)
                options = XQueryRegexHelper.ParseFlags(flags);
            return new System.Text.RegularExpressions.Regex(netPattern, options);
        });
    }
}

/// <summary>
/// fn:string-length($arg as xs:string?) as xs:integer
/// </summary>
public sealed class StringLengthFunction : XQueryFunction
{
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
        var arg = ConcatFunction.XQueryStringValue(atomized);
        // Count Unicode codepoints, not UTF-16 code units
        var info = new System.Globalization.StringInfo(arg);
        return ValueTask.FromResult<object?>((long)info.LengthInTextElements);
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
        var info = new System.Globalization.StringInfo(str);
        return ValueTask.FromResult<object?>((long)info.LengthInTextElements);
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
            if (!s.Contains('E', StringComparison.Ordinal) && !s.Contains('e', StringComparison.Ordinal))
            {
                if (s.Contains('.', StringComparison.Ordinal))
                {
                    s = s.TrimEnd('0');
                    if (s[^1] == '.') s += "0";
                }
                return s;
            }
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
            if (!s.Contains('E', StringComparison.Ordinal) && !s.Contains('e', StringComparison.Ordinal))
            {
                if (s.Contains('.', StringComparison.Ordinal))
                {
                    s = s.TrimEnd('0');
                    if (s[^1] == '.') s += "0";
                }
                return s;
            }
        }

        // Use .NET's round-trip format which correctly handles float precision
        // (uses Dragon4/Ryu internally), then post-process to XPath canonical form
        var raw = f.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        // .NET produces "3.4028235E+38" — XPath wants "3.4028235E38" (no + in exponent)
        var eIdx = raw.IndexOf('E');
        if (eIdx < 0) eIdx = raw.IndexOf('e');
        if (eIdx >= 0)
        {
            var mantissaStr = raw[..eIdx];
            var expStr = raw[(eIdx + 1)..];
            // Strip leading + from exponent
            if (expStr.StartsWith('+')) expStr = expStr[1..];
            // Ensure mantissa has decimal point with at least one digit
            if (mantissaStr.Contains('.'))
            {
                mantissaStr = mantissaStr.TrimEnd('0');
                if (mantissaStr[^1] == '.') mantissaStr += "0";
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
        var str = ConcatFunction.XQueryStringValue(Execution.QueryExecutionContext.Atomize(arguments[0]));
        var search = ConcatFunction.XQueryStringValue(Execution.QueryExecutionContext.Atomize(arguments[1]));
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
        var comparison = CollationHelper.GetStringComparison(arguments[2]?.ToString());
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
        var str = ConcatFunction.XQueryStringValue(Execution.QueryExecutionContext.Atomize(arguments[0]));
        var suffix = ConcatFunction.XQueryStringValue(Execution.QueryExecutionContext.Atomize(arguments[1]));
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
        var str = ConcatFunction.XQueryStringValue(arguments[0]);
        return ValueTask.FromResult<object?>(str.ToUpperInvariant());
    }
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
        return ValueTask.FromResult<object?>(str.ToLowerInvariant());
    }
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
        var form = (arguments[1]?.ToString() ?? "NFC").Trim().ToUpperInvariant();
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
        var str = arguments[0]?.ToString() ?? "";
        var mapString = arguments[1]?.ToString() ?? "";
        var transString = arguments[2]?.ToString() ?? "";

        var result = new char[str.Length];
        var resultLen = 0;

        foreach (var c in str)
        {
            var index = mapString.IndexOf(c);
            if (index < 0)
            {
                result[resultLen++] = c;
            }
            else if (index < transString.Length)
            {
                result[resultLen++] = transString[index];
            }
            // else: character is deleted
        }

        return ValueTask.FromResult<object?>(new string(result, 0, resultLen));
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
        // For element/document nodes, compute string value by walking descendant text nodes
        if (arg is Xdm.Nodes.XdmElement || arg is Xdm.Nodes.XdmDocument)
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
        var atomized = Execution.QueryExecutionContext.Atomize(item);
        return ValueTask.FromResult<object?>(atomized?.ToString() ?? "");
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
            var tokens = regex.Split(input);
            return ValueTask.FromResult<object?>(tokens);
        }
        catch (InvalidOperationException)
        {
            throw; // FORX0003 errors
        }
        catch (ArgumentException)
        {
            // Invalid regex pattern — return empty sequence
            return ValueTask.FromResult<object?>(Array.Empty<string>());
        }
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
            if (!isLiteral)
                XQueryRegexHelper.ValidateXsdRegex(pattern);
            var netPattern = isLiteral
                ? System.Text.RegularExpressions.Regex.Escape(pattern)
                : XQueryRegexHelper.ConvertXPathPatternToNet(pattern);
            netPattern = XQueryRegexHelper.ConvertXsdEscapesToNet(netPattern);
            if (!flags.Contains('m', StringComparison.Ordinal))
                netPattern = XQueryRegexHelper.FixDollarAnchor(netPattern);
            netPattern = XQueryRegexHelper.FixDotForSurrogatePairs(netPattern);
            var options = XQueryRegexHelper.ParseFlags(flags);
            var regex = new System.Text.RegularExpressions.Regex(netPattern, options);
            // FORX0003: pattern must not match empty string
            if (regex.IsMatch(""))
                throw new InvalidOperationException("FORX0003: The supplied pattern matches a zero-length string");
            var tokens = regex.Split(input);
            return ValueTask.FromResult<object?>(tokens);
        }
        catch (InvalidOperationException)
        {
            throw; // FORX0003 errors
        }
        catch (ArgumentException)
        {
            // Invalid regex pattern — return empty sequence
            return ValueTask.FromResult<object?>(Array.Empty<string>());
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
        var pattern = arguments[1]?.ToString() ?? "";
        try
        {
            var regex = RegexCache.GetOrCreate(pattern);
            return ValueTask.FromResult<object?>(regex.IsMatch(input));
        }
        catch (System.Text.RegularExpressions.RegexParseException ex)
        {
            throw new InvalidOperationException($"FORX0002: Invalid regular expression '{pattern}': {ex.Message}", ex);
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
        var pattern = arguments[1]?.ToString() ?? "";
        var flags = arguments[2]?.ToString() ?? "";
        try
        {
            var isLiteral = flags.Contains('q', StringComparison.Ordinal);
            if (!isLiteral)
                XQueryRegexHelper.ValidateXsdRegex(pattern);
            var netPattern = isLiteral
                ? System.Text.RegularExpressions.Regex.Escape(pattern)
                : XQueryRegexHelper.ConvertXPathPatternToNet(pattern);
            netPattern = XQueryRegexHelper.ConvertXsdEscapesToNet(netPattern);
            if (!flags.Contains('m', StringComparison.Ordinal))
                netPattern = XQueryRegexHelper.FixDollarAnchor(netPattern);
            netPattern = XQueryRegexHelper.FixDotForSurrogatePairs(netPattern);
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
        var pattern = arguments[1]?.ToString() ?? "";
        var replacement = arguments[2]?.ToString() ?? "";
        try
        {
            XQueryRegexHelper.ValidateXsdRegex(pattern);
            var netPattern = XQueryRegexHelper.ConvertXPathPatternToNet(pattern);
            netPattern = XQueryRegexHelper.ConvertXsdEscapesToNet(netPattern);
            netPattern = XQueryRegexHelper.FixDollarAnchor(netPattern);
            netPattern = XQueryRegexHelper.FixDotForSurrogatePairs(netPattern);
            var netReplacement = XQueryRegexHelper.ConvertXPathReplacementToNet(replacement, pattern);
            var regex = new System.Text.RegularExpressions.Regex(netPattern);
            return ValueTask.FromResult<object?>(regex.Replace(input, netReplacement));
        }
        catch (InvalidOperationException)
        {
            throw; // FORX0004 errors
        }
        catch (ArgumentException)
        {
            // Invalid regex pattern — return input unchanged
            return ValueTask.FromResult<object?>(input);
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
            if (!flags.Contains('m', StringComparison.Ordinal))
                netPattern = XQueryRegexHelper.FixDollarAnchor(netPattern);
            netPattern = XQueryRegexHelper.FixDotForSurrogatePairs(netPattern);
            var options = XQueryRegexHelper.ParseFlags(flags);
            var regex = new System.Text.RegularExpressions.Regex(netPattern, options);
            return ValueTask.FromResult<object?>(regex.Replace(input, netReplacement));
        }
        catch (InvalidOperationException)
        {
            throw; // FORX0004 errors
        }
        catch (ArgumentException)
        {
            // Invalid regex pattern — return input unchanged
            return ValueTask.FromResult<object?>(input);
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
        if (arguments[0] == null || arguments[1] == null)
            return ValueTask.FromResult<object?>(null);
        var s1 = ConcatFunction.XQueryStringValue(arguments[0]);
        var s2 = ConcatFunction.XQueryStringValue(arguments[1]);
        var comparison = CollationHelper.GetDefaultComparison(context);
        var cmp = string.Compare(s1, s2, comparison);
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
        var str = arguments[0]?.ToString();
        if (str == null) return ValueTask.FromResult<object?>(null);
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
            if (cp < 0 || cp > 0x10FFFF || (cp >= 0xD800 && cp <= 0xDFFF))
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
        var s1 = arguments[0]?.ToString();
        var s2 = arguments[1]?.ToString();
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
        var str = arguments[0]?.ToString() ?? "";
        return ValueTask.FromResult<object?>(Uri.EscapeDataString(str));
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
        var str = arguments[0]?.ToString() ?? "";
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
        var str = arguments[0]?.ToString() ?? "";
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
        var str = arguments[0]?.ToString() ?? "";
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
                'x' => System.Text.RegularExpressions.RegexOptions.IgnorePatternWhitespace,
                _ => System.Text.RegularExpressions.RegexOptions.None
            };
        }
        return options;
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
    /// In XPath regex, "." matches any Unicode codepoint. In .NET regex, "." matches
    /// a single UTF-16 char, so surrogate pairs (non-BMP characters) are treated as two chars.
    /// This method replaces unescaped "." outside character classes with a subexpression
    /// that matches either a surrogate pair or a BMP character.
    /// </summary>
    public static string FixDotForSurrogatePairs(string pattern)
    {
        if (!pattern.Contains('.', StringComparison.Ordinal))
            return pattern;

        const string surrogateDot = @"(?:[\uD800-\uDBFF][\uDC00-\uDFFF]|.)";
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
        bool charClassAfterRange = false;
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
                if (next == '0' && charClassDepth == 0)
                    throw new InvalidOperationException("FORX0002: Invalid regular expression: \\0 (octal/null escape) is not supported in XSD regex");
                // Backreference validation: \1-\9 (single-digit only in XSD regex)
                if (next >= '1' && next <= '9' && charClassDepth == 0)
                {
                    int singleRef = next - '0';
                    if (singleRef > groupCount)
                        throw new InvalidOperationException($"FORX0002: Invalid regular expression: back reference \\{singleRef} exceeds number of capturing groups ({groupCount})");
                    // Forward backreference check: the referenced group must be closed before this position
                    if (!groupClosePos.TryGetValue(singleRef, out var closePos) || closePos > i)
                        throw new InvalidOperationException($"FORX0002: Invalid regular expression: back reference \\{singleRef} is a forward reference (group {singleRef} has not been closed at this point)");
                }
                // \p{...} and \P{...} — skip the entire Unicode property block
                if (next is 'p' or 'P' && i + 2 < pattern.Length && pattern[i + 2] == '{')
                {
                    int closeBrace = pattern.IndexOf('}', i + 3);
                    if (closeBrace < 0)
                        throw new InvalidOperationException("FORX0002: Invalid regular expression: unterminated Unicode property escape \\p{...}");
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
                    charClassAfterRange = false;
                }
                else if (c == ']')
                {
                    charClassDepth--;
                    charClassAfterRange = false;
                }
                else if (c == '-')
                {
                    // After a range (e.g., a-d), a '-' is only valid if followed by '[' (subtraction) or ']'
                    if (charClassAfterRange && i + 1 < pattern.Length && pattern[i + 1] != '[' && pattern[i + 1] != ']')
                        throw new InvalidOperationException("FORX0002: Invalid regular expression: '-' after a character range must introduce subtraction '-[...]' or be at end of class");
                    charClassAfterRange = false;
                }
                else
                {
                    charClassMemberCount++; // count actual class content (not dashes)
                    // Check if this character is the end of a range: prev was '-' and before that was a single char
                    if (i >= 2 && pattern[i - 1] == '-' && pattern[i - 2] != '\\' && pattern[i - 2] != '[' && pattern[i - 2] != '^')
                        charClassAfterRange = true;
                    else
                        charClassAfterRange = false;
                }
                continue;
            }

            if (c == '[')
            {
                charClassDepth = 1;
                charClassMemberCount = 0;
                charClassAfterRange = false;
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

    // XML NameStartChar: [A-Z_a-z\xC0-\xD6\xD8-\xF6\xF8-\u02FF\u0370-\u037D\u037F-\u1FFF\u200C-\u200D\u2070-\u218F\u2C00-\u2FEF\u3001-\uD7FF\uF900-\uFDCF\uFDF0-\uFFFD]
    private const string NameStartCharClass = @"A-Z_a-z\xC0-\xD6\xD8-\xF6\u00F8-\u02FF\u0370-\u037D\u037F-\u1FFF\u200C-\u200D\u2070-\u218F\u2C00-\u2FEF\u3001-\uD7FF\uF900-\uFDCF\uFDF0-\uFFFD";
    // XML NameChar adds: [-.0-9\xB7\u0300-\u036F\u203F-\u2040]
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

    /// <summary>
    /// Converts XSD/XPath-specific regex features to .NET equivalents:
    /// - \i → XML NameStartChar, \I → not NameStartChar
    /// - \c → XML NameChar, \C → not NameChar
    /// - \p{IsXxx} → surrogate pair ranges for unsupported Unicode blocks
    /// </summary>
    public static string ConvertXsdEscapesToNet(string pattern)
    {
        // Quick check: if no special constructs, return as-is
        bool hasSpecial = false;
        for (int i = 0; i < pattern.Length - 1; i++)
        {
            if (pattern[i] == '\\' && (pattern[i + 1] is 'c' or 'C' or 'i' or 'I'
                or 'p' or 'P'))
            {
                hasSpecial = true;
                break;
            }
        }
        if (!hasSpecial) return pattern;

        var sb = new System.Text.StringBuilder(pattern.Length + 32);
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
                        sb.Append(inCharClass ? $"^{NameCharClass}" : $"[^{NameCharClass}]");
                        i++;
                        continue;
                    case 'i':
                        sb.Append(inCharClass ? NameStartCharClass : $"[{NameStartCharClass}]");
                        i++;
                        continue;
                    case 'I':
                        sb.Append(inCharClass ? $"^{NameStartCharClass}" : $"[^{NameStartCharClass}]");
                        i++;
                        continue;
                    case 'p' or 'P' when i + 2 < pattern.Length && pattern[i + 2] == '{':
                    {
                        int closeBrace = pattern.IndexOf('}', i + 3);
                        if (closeBrace > 0)
                        {
                            var blockName = pattern[(i + 3)..closeBrace];
                            if (UnsupportedUnicodeBlocks.TryGetValue(blockName, out var range))
                            {
                                var surrogatePattern = SupplementaryRangeToSurrogatePairs(range.Start, range.End);
                                if (next == 'P')
                                {
                                    // \P{...} = NOT in block — not easily expressible with surrogate pairs
                                    // Use a negative lookahead: (?:(?!surrogatePattern)[\s\S][\s\S])
                                    // For simplicity, just pass through (will fail in .NET but at least doesn't crash validation)
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
                        }
                        // Not an unsupported block — pass through
                        sb.Append(c);
                        sb.Append(next);
                        i++;
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
        var input = ConcatFunction.XQueryStringValue(Execution.QueryExecutionContext.Atomize(arguments[0])).Trim();
        if (string.IsNullOrEmpty(input))
            return ValueTask.FromResult<object?>(Array.Empty<string>());

        var tokens = System.Text.RegularExpressions.Regex.Split(input, @"\s+");
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
        if (arguments[0] == null || arguments[1] == null)
            return ValueTask.FromResult<object?>(null);
        var s1 = ConcatFunction.XQueryStringValue(arguments[0]);
        var s2 = ConcatFunction.XQueryStringValue(arguments[1]);
        var comparison = CollationHelper.GetStringComparison(arguments[2]?.ToString());
        var cmp = string.Compare(s1, s2, comparison);
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
        var comparison = CollationHelper.GetStringComparison(arguments[2]?.ToString());
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
        var comparison = CollationHelper.GetStringComparison(arguments[2]?.ToString());
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

    public static StringComparison GetStringComparison(string? collationUri)
    {
        return collationUri switch
        {
            null or "" or "http://www.w3.org/2005/xpath-functions/collation/codepoint" => StringComparison.Ordinal,
            "http://www.w3.org/2005/xpath-functions/collation/html-ascii-case-insensitive" => StringComparison.OrdinalIgnoreCase,
            _ when collationUri.StartsWith(UcaPrefix, StringComparison.Ordinal)
                => MapUcaToStringComparison(collationUri),
            _ => throw new XQueryRuntimeException("FOCH0002",
                $"FOCH0002: Unknown collation: {collationUri}")
        };
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
    /// Compares two strings using UCA collation parameters extracted from the given URI.
    /// </summary>
    public static int CompareUca(string? s1, string? s2, string collationUri)
    {
        var (compareInfo, options) = GetUcaCollation(collationUri);
        return compareInfo.Compare(s1 ?? "", s2 ?? "", options);
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

        return strength.ToLowerInvariant() switch
        {
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
