using System.Collections.Concurrent;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// Helper for resolving collation URIs to StringComparison values.
/// Supports the standard XPath codepoint collation, HTML ASCII case-insensitive collation,
/// and UCA collations per XPath 3.1 section 5.3.4.
/// </summary>
internal static class CollationHelper
{
    private const string UcaPrefix = "http://www.w3.org/2013/collation/UCA";
    internal const string HtmlAsciiCaseInsensitiveUri = "http://www.w3.org/2005/xpath-functions/collation/html-ascii-case-insensitive";

    public static StringComparison GetStringComparison(string? collationUri, Ast.ExecutionContext? context = null)
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
    public static int CompareStrings(string s1, string s2, StringComparison comparison, Ast.ExecutionContext? context = null)
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
    public static int CompareWithCollation(string s1, string s2, string? collationUri, Ast.ExecutionContext? context = null)
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
    private static int CompareHtmlAsciiCaseInsensitive(string s1, string s2, Ast.ExecutionContext? context = null)
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
    internal static string AsciiLower(string s, Ast.ExecutionContext? context = null)
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
        ValidateUcaParameters(parameters, fallback);
        var compareInfo = ResolveCompareInfo(parameters, fallback);
        var options = MapStrengthToCompareOptions(parameters);
        return (compareInfo, options);
    }

    /// <summary>
    /// When <c>fallback=no</c>, the implementation must signal FOCH0002 for any UCA collation
    /// parameter or value it cannot honor with full fidelity (XPath F&amp;O 5.3.4). .NET's
    /// <see cref="System.Globalization.CompareInfo"/> / <see cref="System.Globalization.CompareOptions"/>
    /// can faithfully realize a limited subset of the UCA tailoring parameters; the rest
    /// (reorder, maxVariable shifting, backwards accent ordering, caseFirst, numeric ordering,
    /// version pinning, and any unrecognized keyword or value) cannot be expressed and therefore
    /// raise FOCH0002 under <c>fallback=no</c>. When fallback is allowed (the default) the same
    /// parameters are silently ignored and a best-effort comparison is performed.
    /// </summary>
    private static void ValidateUcaParameters(Dictionary<string, string> parameters, bool fallback)
    {
        if (fallback)
            return;

        foreach (var (key, value) in parameters)
        {
            bool supported = key.ToLowerInvariant() switch
            {
                // Control + faithfully-honored parameters.
                "fallback" => value.Equals("yes", StringComparison.OrdinalIgnoreCase)
                              || value.Equals("no", StringComparison.OrdinalIgnoreCase),
                "lang" => true, // resolved to a CultureInfo (or validated below by ResolveCompareInfo)
                "strength" => value.ToLowerInvariant() is "primary" or "secondary" or "tertiary"
                              or "quaternary" or "identical" or "1" or "2" or "3" or "4" or "5",
                "caselevel" => value.Equals("yes", StringComparison.OrdinalIgnoreCase)
                               || value.Equals("no", StringComparison.OrdinalIgnoreCase),
                "normalization" => value.Equals("yes", StringComparison.OrdinalIgnoreCase)
                                   || value.Equals("no", StringComparison.OrdinalIgnoreCase),
                // alternate=blanked maps to IgnoreSymbols; non-ignorable is the default no-op.
                // alternate=shifted requires variable-weight shifting we cannot realize.
                "alternate" => value.Equals("blanked", StringComparison.OrdinalIgnoreCase)
                               || value.Equals("non-ignorable", StringComparison.OrdinalIgnoreCase),
                // backwards=no is the default; backwards=yes (French accent ordering) is unsupported.
                "backwards" => value.Equals("no", StringComparison.OrdinalIgnoreCase),
                // numeric=no is the default; numeric=yes (numeric-aware ordering) is unsupported.
                "numeric" => value.Equals("no", StringComparison.OrdinalIgnoreCase),
                // Unsupported tailoring parameters: reorder, maxVariable, caseFirst, version.
                _ => false,
            };

            if (!supported)
                throw new XQueryRuntimeException("FOCH0002",
                    $"FOCH0002: UCA collation parameter '{key}={value}' cannot be honored and fallback=no");
        }
    }

    /// <summary>
    /// Extracts the caseFirst parameter from a UCA collation URI.
    /// Returns "lower", "upper", or null (off/default).
    /// </summary>
    internal static string? GetCaseFirst(string collationUri, Ast.ExecutionContext? context = null)
    {
        var parameters = ParseUcaParameters(collationUri);
        return parameters.TryGetValue("caseFirst", out var cf) ? cf.ToLowerInvariant() : null;
    }

    /// <summary>
    /// Compares two strings using UCA collation parameters extracted from the given URI.
    /// </summary>
    public static int CompareUca(string? s1, string? s2, string collationUri, Ast.ExecutionContext? context = null)
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
    /// Returns the default collation URI from the execution context, or null when it is the
    /// codepoint collation (or unset). Use with <see cref="CompareWithCollation"/> /
    /// the UCA substring helpers to preserve full collation fidelity.
    /// </summary>
    public static string? GetDefaultCollationUri(Ast.ExecutionContext context) =>
        context is Execution.QueryExecutionContext qec ? qec.DefaultCollation : null;

    /// <summary>
    /// Maps a UCA collation URI to the best available <see cref="StringComparison"/>.
    /// This provides a reasonable approximation; for full fidelity use <see cref="GetUcaCollation"/>.
    /// </summary>
    private static StringComparison MapUcaToStringComparison(string collationUri, Ast.ExecutionContext? context = null)
    {
        var parameters = ParseUcaParameters(collationUri);
        var fallback = !parameters.TryGetValue("fallback", out var fb) || !fb.Equals("no", StringComparison.OrdinalIgnoreCase);

        // Under fallback=no, reject any parameter/value we cannot honor (FOCH0002).
        ValidateUcaParameters(parameters, fallback);

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
    private static Dictionary<string, string> ParseUcaParameters(string collationUri, Ast.ExecutionContext? context = null)
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
        var options = System.Globalization.CompareOptions.None;

        parameters.TryGetValue("strength", out var strengthForAlt);
        // The identical (level 5) strength compares strings by their full Unicode
        // code points as a final tie-breaker, so even "blanked" variable elements
        // (which zero a character's L1–L3 weights) are still distinguished at this
        // level. Suppress the IgnoreSymbols mapping when strength=identical so that
        // e.g. compare("database","data base", "...;alternate=blanked;strength=identical")
        // is non-zero, matching UCA semantics (QT3 fn-compare-042).
        bool identicalStrength = strengthForAlt is not null
            && (strengthForAlt.Equals("identical", StringComparison.OrdinalIgnoreCase)
                || strengthForAlt == "5");

        // alternate=blanked: variable collation elements (punctuation, whitespace,
        // symbols) are ignored. .NET's closest analogue is IgnoreSymbols, which
        // skips punctuation and symbol characters during comparison/search.
        if (!identicalStrength
            && parameters.TryGetValue("alternate", out var alternate)
            && alternate.Equals("blanked", StringComparison.OrdinalIgnoreCase))
            options |= System.Globalization.CompareOptions.IgnoreSymbols;

        if (!parameters.TryGetValue("strength", out var strength) || string.IsNullOrEmpty(strength))
            return options; // tertiary (default): case & accent sensitive

        var caseLevel = parameters.TryGetValue("caseLevel", out var cl)
            && cl.Equals("yes", StringComparison.OrdinalIgnoreCase);

        return options | strength.ToLowerInvariant() switch
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

    /// <summary>
    /// True when the collation URI is a UCA collation (needs <see cref="System.Globalization.CompareInfo"/>
    /// for substring operations rather than a plain <see cref="StringComparison"/>).
    /// </summary>
    public static bool IsUca(string? collationUri) =>
        collationUri != null && collationUri.StartsWith(UcaPrefix, StringComparison.Ordinal);

    /// <summary>
    /// fn:contains semantics under a UCA collation: does <paramref name="str"/> contain
    /// <paramref name="search"/> as a substring (collation-aware)? Empty search → true.
    /// </summary>
    public static bool UcaContains(string str, string search, string collationUri)
    {
        if (search.Length == 0) return true;
        var (ci, opts) = GetUcaCollation(collationUri);
        if (CollapsesToEmpty(ci, search, opts)) return true;
        return ci.IndexOf(str, search, opts) >= 0;
    }

    /// <summary>fn:starts-with under a UCA collation.</summary>
    public static bool UcaStartsWith(string str, string prefix, string collationUri)
    {
        if (prefix.Length == 0) return true;
        var (ci, opts) = GetUcaCollation(collationUri);
        // A search string consisting solely of collation-ignorable characters (e.g. all
        // punctuation under alternate=blanked) collapses to the empty string and matches.
        if (CollapsesToEmpty(ci, prefix, opts)) return true;
        // .NET's IsPrefix mishandles leading ignorable symbols, so locate the first match
        // and require everything before it to be ignorable (an effective position 0).
        var idx = ci.IndexOf(str, prefix, opts, out _);
        return idx >= 0 && (idx == 0 || CollapsesToEmpty(ci, str[..idx], opts));
    }

    /// <summary>fn:ends-with under a UCA collation.</summary>
    public static bool UcaEndsWith(string str, string suffix, string collationUri)
    {
        if (suffix.Length == 0) return true;
        var (ci, opts) = GetUcaCollation(collationUri);
        if (CollapsesToEmpty(ci, suffix, opts)) return true;
        // Find the last match and require everything after it to be ignorable.
        var idx = ci.LastIndexOf(str, suffix, opts, out var matchLength);
        if (idx < 0) return false;
        var end = idx + matchLength;
        return end == str.Length || CollapsesToEmpty(ci, str[end..], opts);
    }

    /// <summary>True when the whole string is collation-ignorable (compares equal to "").</summary>
    private static bool CollapsesToEmpty(System.Globalization.CompareInfo ci, string s, System.Globalization.CompareOptions opts) =>
        s.Length == 0 || ci.Compare(s, "", opts) == 0;

    /// <summary>
    /// fn:substring-before under a UCA collation: the part of <paramref name="str"/> that
    /// precedes the first collation-aware occurrence of <paramref name="search"/>.
    /// Returns "" when <paramref name="search"/> is absent; "" when search is empty.
    /// </summary>
    public static string UcaSubstringBefore(string str, string search, string collationUri)
    {
        if (search.Length == 0) return "";
        var (ci, opts) = GetUcaCollation(collationUri);
        if (CollapsesToEmpty(ci, search, opts)) return "";
        var idx = ci.IndexOf(str, search, opts);
        return idx < 0 ? "" : str[..idx];
    }

    /// <summary>
    /// fn:substring-after under a UCA collation: the part of <paramref name="str"/> that
    /// follows the first collation-aware occurrence of <paramref name="search"/>.
    /// Returns "" when <paramref name="search"/> is absent; the whole string when search is empty.
    /// </summary>
    public static string UcaSubstringAfter(string str, string search, string collationUri)
    {
        if (search.Length == 0) return str;
        var (ci, opts) = GetUcaCollation(collationUri);
        if (CollapsesToEmpty(ci, search, opts)) return str;
        var idx = ci.IndexOf(str, search, opts, out var matchLength);
        return idx < 0 ? "" : str[(idx + matchLength)..];
    }
}
