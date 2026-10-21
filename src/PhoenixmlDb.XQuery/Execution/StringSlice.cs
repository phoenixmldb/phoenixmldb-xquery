using PhoenixmlDb.XQuery.Functions;

namespace PhoenixmlDb.XQuery.Execution;

/// <summary>
/// A substring that shares the characters of the string it was taken from, so that
/// <c>substring($s, 2)</c> costs no copy. The ISO Schematron skeleton walks every XPath of a schema
/// one character per call (<c>sch-check:strip-strings</c>, <c>test-paren</c>, <c>test-sqb</c>,
/// passing <c>substring($string, 2)</c> to itself); with a copy per call, a 127,781-character
/// <c>sch:let</c> cost 21 GB (xquery#287).
/// </summary>
/// <remarks>
/// A slice is never an item that ordinary code sees. One is made only where its consumer is known:
/// an XSLT <c>xsl:with-param</c> of <c>xsl:call-template</c> that binds a parameter without an
/// <c>as</c> type, and the arguments of <c>fn:substring</c>, <c>fn:string-length</c> and
/// <c>fn:starts-with</c> (<see cref="StringSliceEvaluation"/>). Every other reader of a variable
/// gets the string (<see cref="Materialize"/>): <see cref="QueryExecutionContext.GetVariable"/>,
/// <see cref="VariableOperator"/>, closures, and the XSLT engine's own variable lookups.
/// </remarks>
internal sealed class StringSlice
{
    private string? _value;

    private StringSlice(string source, int start, int length, bool hasSurrogates)
    {
        Source = source;
        Start = start;
        Length = length;
        HasSurrogates = hasSurrogates;
    }

    internal string Source { get; }
    internal int Start { get; }
    internal int Length { get; }

    /// <summary>
    /// Whether the characters may contain a surrogate; when false, a codepoint is a char and
    /// positions and lengths need no scan. Inherited from the string a slice was taken from.
    /// </summary>
    internal bool HasSurrogates { get; }

    internal ReadOnlySpan<char> Span => Source.AsSpan(Start, Length);

    /// <summary>The whole of <paramref name="s"/>, scanned once for surrogates.</summary>
    internal static StringSlice Of(string s) => new(s, 0, s.Length, s.AsSpan().IndexOfAnyInRange('\uD800', '\uDFFF') >= 0);

    /// <summary>The string, made once.</summary>
    public override string ToString() => _value ??= Source.Substring(Start, Length);

    /// <summary><paramref name="value"/>, with a slice replaced by its string.</summary>
    internal static object? Materialize(object? value) => value is StringSlice slice ? slice.ToString() : value;

    /// <summary>The number of codepoints, as <see cref="StringLengthFunction.CountCodepoints"/> counts them.</summary>
    internal long CodepointLength
    {
        get
        {
            if (!HasSurrogates)
                return Length;
            var span = Span;
            long count = 0;
            for (var i = 0; i < span.Length; i++)
            {
                count++;
                if (char.IsHighSurrogate(span[i]) && i + 1 < span.Length && char.IsLowSurrogate(span[i + 1]))
                    i++;
            }
            return count;
        }
    }

    /// <summary>
    /// The codepoints from 1-based position <paramref name="start"/>, at most
    /// <paramref name="length"/> of them: the slice <see cref="SubstringHelper.SubstringByCodepoints"/>
    /// would copy out.
    /// </summary>
    internal StringSlice Substring(int start, int length)
    {
        if (Length == 0 || length <= 0)
            return Empty(this);
        if (!HasSurrogates)
        {
            // start >= 1 (the callers clamp it); a start past the end selects nothing.
            if (start - 1 >= Length)
                return Empty(this);
            var offset = start - 1;
            var count = (int)Math.Min(length, (long)Length - offset);
            return new StringSlice(Source, Start + offset, count, false);
        }

        var span = Span;
        var cpIndex = 1;
        var charStart = -1;
        var charEnd = -1;
        var cpCount = 0;
        for (var i = 0; i < span.Length;)
        {
            if (cpIndex == start)
                charStart = i;
            if (char.IsHighSurrogate(span[i]) && i + 1 < span.Length && char.IsLowSurrogate(span[i + 1]))
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
            return Empty(this);
        if (charEnd < 0)
            charEnd = span.Length;
        return new StringSlice(Source, Start + charStart, charEnd - charStart, true);
    }

    private static StringSlice Empty(StringSlice of) => new(of.Source, of.Start, 0, false);
}
