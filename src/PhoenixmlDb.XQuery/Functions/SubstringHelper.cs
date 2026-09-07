using System.Collections.Concurrent;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

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
    internal static string SubstringByCodepoints(string source, int start, int length, Ast.ExecutionContext? context = null)
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
