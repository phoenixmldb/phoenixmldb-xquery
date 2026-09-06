using System.Collections.Concurrent;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

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

    public static string XQueryStringValue(object? value, INodeProvider? nodeProvider, Ast.ExecutionContext? context = null)
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
            throw context.Error("FOTY0014", "The string value of a function item is not defined");
        if (value is IDictionary<object, object?>)
            throw context.Error("FOTY0014", "The string value of a map is not defined");
        if (value is object?[] arr) return string.Join(" ", arr.Select(XQueryStringValue));
        if (value is IEnumerable<object?> seq) return string.Join(" ", seq.Select(XQueryStringValue));
        // Casting xs:QName to xs:string yields its LEXICAL form — "prefix:local", or the bare
        // local name when there is no prefix (XPath 3.1 19.2). Without this branch the value
        // fell through to QName.ToString(), which renders the EQName form "Q{uri}local" as soon
        // as an expanded namespace is attached. That is a good DEBUGGING rendering and the wrong
        // VALUE: a stylesheet doing xsl:value-of on $err:code saw Q{http://...}XTDE3086 instead
        // of err:XTDE3086. Delegating a spec-defined conversion to a .NET ToString() is what
        // allowed a diagnostic form to become a value.
        if (value is QName qname)
            return string.IsNullOrEmpty(qname.Prefix) ? qname.LocalName : qname.Prefix + ":" + qname.LocalName;
        return value.ToString() ?? "";
    }

    /// <summary>
    /// Formats a decimal per XPath canonical rules:
    /// No trailing fractional zeros, no decimal point if integer,
    /// negative zero maps to "0".
    /// </summary>
    public static string FormatDecimalXPath(decimal dec, Ast.ExecutionContext? context = null)
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
    public static string FormatDoubleXPath(double d, Ast.ExecutionContext? context = null)
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
    public static string FormatFloatXPath(float f, Ast.ExecutionContext? context = null)
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
