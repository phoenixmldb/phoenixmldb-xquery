using System.Text;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// Writes one <see cref="Rune"/> into a <see cref="StringBuilder"/> without a string per rune.
/// <c>StringBuilder.Append(Rune)</c> exists only from .NET 11, and <c>Append(rune.ToString())</c>
/// allocates; the UTF-16 encoding of a rune is at most two chars, so a stack buffer carries it on
/// every target framework.
/// </summary>
internal static class StringBuilderRunes
{
    internal static StringBuilder AppendRune(this StringBuilder sb, Rune rune)
    {
        Span<char> utf16 = stackalloc char[2];
        return sb.Append(utf16[..rune.EncodeToUtf16(utf16)]);
    }

    internal static StringBuilder InsertRune(this StringBuilder sb, int index, Rune rune)
    {
        Span<char> utf16 = stackalloc char[2];
        return sb.Insert(index, utf16[..rune.EncodeToUtf16(utf16)]);
    }
}
