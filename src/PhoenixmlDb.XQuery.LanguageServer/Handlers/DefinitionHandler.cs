using System;
using System.Text.RegularExpressions;
using PhoenixmlDb.XQuery.LanguageServer.Lsp;
using Range = PhoenixmlDb.XQuery.LanguageServer.Lsp.Range;
using Position = PhoenixmlDb.XQuery.LanguageServer.Lsp.Position;

namespace PhoenixmlDb.XQuery.LanguageServer.Handlers;

/// <summary>
/// Server-side <c>textDocument/definition</c>. MVP scope: local (intra-buffer) only.
/// For <c>$variable</c> references: finds the matching <c>declare variable $name</c>
/// or <c>for $name in</c> / <c>let $name :=</c> binding. For unprefixed identifiers
/// followed by <c>(</c>: finds the matching <c>declare function (prefix:)?name(</c>.
/// Cross-module/cross-file resolution is a Plan 29 candidate.
/// </summary>
public static class DefinitionHandler
{
    /// <summary>Resolves the symbol at <paramref name="pos"/> to its declaration site.</summary>
    public static Location? Handle(DocumentBuffer buf, Position pos)
    {
        ArgumentNullException.ThrowIfNull(buf);
        ArgumentNullException.ThrowIfNull(pos);

        var offset = PositionToOffset(buf.Text, pos);
        if (offset < 0 || offset > buf.Text.Length) return null;

        var token = ExtractTokenAt(buf.Text, offset);
        if (token is null) return null;

        // Variable: scan for "declare variable $name" or "for $name in" or "let $name :="
        // Function: scan for "declare function (prefix:)?name("
        var pattern = token.Value.IsVariable
            ? new Regex(@"(?:declare\s+variable|for|let)\s+(\$" + Regex.Escape(token.Value.Name) + @")\b", RegexOptions.Compiled)
            : new Regex(@"declare\s+function\s+(?:[A-Za-z_][A-Za-z0-9_-]*:)?(" + Regex.Escape(token.Value.Name) + @")\s*\(", RegexOptions.Compiled);

        var match = pattern.Match(buf.Text);
        if (!match.Success) return null;

        var declNameGroup = match.Groups[1];
        var startPos = OffsetToPosition(buf.Text, declNameGroup.Index);
        var endPos = OffsetToPosition(buf.Text, declNameGroup.Index + declNameGroup.Length);
        return new Location(buf.Uri, new Range(startPos, endPos));
    }

    /// <summary>
    /// Reads the identifier under <paramref name="offset"/>. Returns null if not on
    /// a word. <c>IsVariable</c> = true when the identifier is preceded by <c>$</c>.
    /// </summary>
    private static (string Name, bool IsVariable)? ExtractTokenAt(string text, int offset)
    {
        if (offset < 0 || offset > text.Length) return null;
        // Move backward from offset to find the token start
        int start = offset;
        while (start > 0 && IsWordChar(text[start - 1])) start--;
        int end = offset;
        while (end < text.Length && IsWordChar(text[end])) end++;
        if (start == end) return null;
        var name = text.Substring(start, end - start);
        var isVariable = start > 0 && text[start - 1] == '$';
        // Strip any namespace prefix for function lookup
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
