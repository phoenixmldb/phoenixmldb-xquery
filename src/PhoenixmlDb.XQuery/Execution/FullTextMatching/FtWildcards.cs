using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PhoenixmlDb.XQuery.Execution.FullTextMatching;

/// <summary>
/// Query strings under <c>using wildcards</c> (Full Text 3.0 §3.4.2): "." matches one character,
/// ".?" none or one, ".*" any number, ".+" one or more, ".{m,n}" from m to n; a backslash makes
/// the next character literal.
/// </summary>
internal static class FtWildcards
{
    /// <summary>
    /// The tokens of <paramref name="query"/>: each with its text and, when it holds a wildcard,
    /// the pattern a source token must match whole. A token without a wildcard has no pattern and
    /// is analyzed like any search word.
    /// </summary>
    public static List<(string Text, Regex? Pattern)> Tokenize(string query, bool caseSensitive, bool diacriticsSensitive)
    {
        var tokens = new List<(string, Regex?)>();
        var text = new StringBuilder();
        var pattern = new StringBuilder();
        var hasWildcard = false;

        void Flush()
        {
            if (text.Length > 0 || hasWildcard)
            {
                tokens.Add((text.ToString(), hasWildcard
                    ? new Regex("^" + pattern + "$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
                    : null));
            }
            text.Clear();
            pattern.Clear();
            hasWildcard = false;
        }

        void Literal(char c)
        {
            text.Append(c);
            var folded = caseSensitive ? c.ToString() : c.ToString().ToLowerInvariant();
            folded = FtEvaluator.Fold(folded, diacriticsSensitive);
            pattern.Append(Regex.Escape(folded));
        }

        for (var i = 0; i < query.Length; i++)
        {
            var c = query[i];
            if (c == '\\')
            {
                if (i + 1 >= query.Length)
                    throw new XQueryRuntimeException("FTDY0020", $"The query string \"{query}\" ends with an unescaped backslash");
                var escaped = query[++i];
                // An escaped character is literal: a letter or digit is part of the token, and
                // punctuation separates tokens as it does in the text ("nex.\?" is the token
                // "nex." before a question mark).
                if (char.IsLetterOrDigit(escaped) || char.IsSurrogate(escaped))
                    Literal(escaped);
                else
                    Flush();
                continue;
            }
            if (c == '.')
            {
                hasWildcard = true;
                text.Append(c);
                var next = i + 1 < query.Length ? query[i + 1] : '\0';
                switch (next)
                {
                    case '?': pattern.Append(".?"); text.Append(next); i++; break;
                    case '*': pattern.Append(".*"); text.Append(next); i++; break;
                    case '+': pattern.Append(".+"); text.Append(next); i++; break;
                    case '{':
                    {
                        var close = query.IndexOf('}', i + 2);
                        var m = close < 0 ? null : Regex.Match(query[(i + 1)..(close + 1)], @"^\{([0-9]+),([0-9]+)\}$");
                        if (m is null || !m.Success)
                            throw new XQueryRuntimeException("FTDY0020", $"Malformed wildcard qualifier in \"{query}\"");
                        pattern.Append(CultureInfo.InvariantCulture, $".{{{m.Groups[1].Value},{m.Groups[2].Value}}}");
                        text.Append(query[(i + 1)..(close + 1)]);
                        i = close;
                        break;
                    }
                    default:
                        pattern.Append('.');
                        break;
                }
                continue;
            }
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (char.IsLetterOrDigit(c) || char.IsSurrogate(c) || category is UnicodeCategory.NonSpacingMark
                    or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.ConnectorPunctuation)
            {
                Literal(c);
                continue;
            }
            Flush();   // any other character separates tokens
        }
        Flush();
        return tokens;
    }
}
