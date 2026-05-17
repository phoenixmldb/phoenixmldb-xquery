using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using PhoenixmlDb.XQuery.LanguageServer.Lsp;
using Range = PhoenixmlDb.XQuery.LanguageServer.Lsp.Range;
using Position = PhoenixmlDb.XQuery.LanguageServer.Lsp.Position;

namespace PhoenixmlDb.XQuery.LanguageServer.Handlers;

/// <summary>
/// Server-side <c>textDocument/references</c> for XQuery. Returns every occurrence
/// of the symbol at the given position — both declaration and use sites.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><c>$varname</c> → every <c>\$varname\b</c>.</item>
///   <item>function name → every <c>(?:prefix:)?name\s*\(</c>.</item>
/// </list>
/// Word-boundary anchored to avoid <c>$xy</c> matching when asking about <c>$x</c>.
/// </remarks>
public static class ReferencesHandler
{
    public static Location[] Handle(DocumentBuffer buf, Position pos)
    {
        ArgumentNullException.ThrowIfNull(buf);
        ArgumentNullException.ThrowIfNull(pos);

        var offset = PositionToOffset(buf.Text, pos);
        var token = ExtractTokenAt(buf.Text, offset);
        if (token is null) return Array.Empty<Location>();

        var locations = new List<Location>();
        Regex regex;
        if (token.Value.IsVariable)
        {
            // \$name not followed by a word char
            regex = new Regex(@"\$(" + Regex.Escape(token.Value.Name) + @")\b", RegexOptions.Compiled);
            foreach (Match m in regex.Matches(buf.Text))
            {
                var g = m.Groups[1];
                var s = OffsetToPosition(buf.Text, g.Index);
                var e = OffsetToPosition(buf.Text, g.Index + g.Length);
                locations.Add(new Location(buf.Uri, new Range(s, e)));
            }
        }
        else
        {
            // (?:prefix:)?name followed by (
            regex = new Regex(@"(?:(?<![-A-Za-z0-9_:])[A-Za-z_][-A-Za-z0-9_]*:)?\b(" +
                Regex.Escape(token.Value.Name) + @")\s*\(", RegexOptions.Compiled);
            foreach (Match m in regex.Matches(buf.Text))
            {
                var g = m.Groups[1];
                var s = OffsetToPosition(buf.Text, g.Index);
                var e = OffsetToPosition(buf.Text, g.Index + g.Length);
                locations.Add(new Location(buf.Uri, new Range(s, e)));
            }
        }
        return locations.ToArray();
    }

    private static (string Name, bool IsVariable)? ExtractTokenAt(string text, int offset)
    {
        if (offset < 0 || offset > text.Length) return null;
        int start = offset;
        while (start > 0 && IsWordChar(text[start - 1])) start--;
        int end = offset;
        while (end < text.Length && IsWordChar(text[end])) end++;
        if (start == end) return null;
        var name = text.Substring(start, end - start);
        var isVariable = start > 0 && text[start - 1] == '$';
        if (!isVariable)
        {
            var colon = name.IndexOf(':', StringComparison.Ordinal);
            if (colon >= 0) name = name[(colon + 1)..];
        }
        return (name, isVariable);
    }

    private static bool IsWordChar(char c) =>
        char.IsLetterOrDigit(c) || c == '-' || c == '_';

    private static int PositionToOffset(string text, Position pos)
    {
        int line = 0, col = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (line == pos.Line && col == pos.Character) return i;
            if (text[i] == '\n') { line++; col = 0; }
            else col++;
        }
        return text.Length;
    }

    private static Position OffsetToPosition(string text, int offset)
    {
        if (offset < 0) offset = 0;
        if (offset > text.Length) offset = text.Length;
        int line = 0, col = 0;
        for (int i = 0; i < offset; i++)
        {
            if (text[i] == '\n') { line++; col = 0; }
            else col++;
        }
        return new Position(line, col);
    }
}
