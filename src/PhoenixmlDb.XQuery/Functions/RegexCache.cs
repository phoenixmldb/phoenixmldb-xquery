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

    public static System.Text.RegularExpressions.Regex GetOrCreate(string pattern, string? flags = null, TimeSpan? timeout = null)
    {
        // A Regex's timeout is fixed when it is built, so it is part of the key.
        var cacheKey = $"{pattern}\x00{flags}\x00{timeout?.Ticks}";
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
            return XQueryRegexHelper.CreateRegex(netPattern, options, timeout);
        });
    }
}
