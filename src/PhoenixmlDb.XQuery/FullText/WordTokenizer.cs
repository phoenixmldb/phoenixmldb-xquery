using static PhoenixmlDb.XQuery.FullText.WordBreakData;

namespace PhoenixmlDb.XQuery.FullText;

/// <summary>
/// Finds the words of a text by the word boundary rules of Unicode Standard Annex #29 (Unicode
/// 6.3 data), and returns those that hold a letter, a digit or an ideograph. Three additions,
/// which the annex leaves to the implementation, keep the tokens of the earlier analysis:
/// a run of Line_Break=Complex_Context characters (Thai, Lao, Khmer, Myanmar) is one token;
/// a Han or Hiragana character is a token by itself; and digits in the Halfwidth and
/// Fullwidth Forms block are numeric.
/// </summary>
internal static class WordTokenizer
{
    private enum State { Start, LeadingConnector, Letter, Number, Katakana, AfterConnector, AfterHebrewQuote }

    /// <summary>Adds the [start, end) UTF-16 span of each token of <paramref name="text"/>.</summary>
    internal static void Tokenize(string text, List<(int Start, int End)> spans)
    {
        var i = 0;
        while (i < text.Length)
        {
            var cls = ClassAt(text, i, out var width);
            if ((cls & 0xF) == RegionalIndicator && SkipRegionalIndicators(text, i) is var skipTo && skipTo > i)
            {
                i = skipTo;   // WB13c: a flag sequence is not a token, nor are marks attached to it
                continue;
            }
            var end = MatchWord(text, i);
            if ((cls & ComplexContext) != 0)
                end = Math.Max(end, MatchComplexContext(text, i));
            if ((cls & (Han | Hiragana)) != 0)
                end = Math.Max(end, AttachExtends(text, i + width));
            if (end > i)
            {
                spans.Add((i, end));
                i = end;
            }
            else
            {
                i += width;   // not part of any token: one character is passed over
            }
        }
    }

    private static int ClassAt(string text, int i, out int width)
    {
        int codePoint = text[i];
        width = 1;
        if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
        {
            codePoint = char.ConvertToUtf32(text[i], text[i + 1]);
            width = 2;
        }
        return Of(codePoint);
    }

    /// <summary>The index after any Extend and Format characters from <paramref name="i"/> (WB4).</summary>
    private static int AttachExtends(string text, int i)
    {
        while (i < text.Length && (ClassAt(text, i, out var width) & 0xF) == Extend)
            i += width;
        return i;
    }

    /// <summary>A character with its attached Extend and Format characters.</summary>
    private static int Unit(string text, int i, out int next)
    {
        var cls = ClassAt(text, i, out var width) & 0xF;
        next = AttachExtends(text, i + width);
        return cls;
    }

    private static bool IsLetter(int cls) => cls is ALetter or HebrewLetter;

    /// <summary>The end of two or more regional indicators with their attached marks, or -1.</summary>
    private static int SkipRegionalIndicators(string text, int i)
    {
        var count = 0;
        while (i < text.Length && Unit(text, i, out var next) == RegionalIndicator)
        {
            count++;
            i = next;
        }
        return count >= 2 ? i : -1;
    }

    /// <summary>The end of the longest word token from <paramref name="start"/>, or -1.</summary>
    private static int MatchWord(string text, int start)
    {
        var state = State.Start;
        var hebrew = false;
        var free = false;     // the last letter could begin a word of its own
        var locked = false;   // the last letter ends a Hebrew-quote-Hebrew sequence
        var accepted = -1;
        var i = start;
        while (i < text.Length)
        {
            var cls = Unit(text, i, out var next);
            State to;
            if (cls == ExtendNumLet)                                     // WB13a, WB13b
            {
                to = state == State.Start || state == State.LeadingConnector
                    ? State.LeadingConnector : State.AfterConnector;
            }
            else if (IsLetter(cls) && state is not State.Katakana)      // WB5, WB9, WB10, WB13b
            {
                to = State.Letter;
                hebrew = cls == HebrewLetter;
                free = true;
                locked = false;
            }
            else if (cls == Numeric && state is not State.Katakana)     // WB8, WB9, WB10, WB13b
            {
                to = State.Number;
            }
            else if (cls == Katakana && state is State.Start or State.LeadingConnector
                         or State.Katakana or State.AfterConnector)     // WB13, WB13b
            {
                to = State.Katakana;
            }
            else if (state == State.Letter && cls is MidLetter or MidNumLet or SingleQuote or DoubleQuote)
            {
                // WB6, WB7: a letter on both sides; WB7a: a Hebrew letter before a single
                // quote; WB7b, WB7c: Hebrew letters on both sides of a double quote. As in the
                // earlier analysis, the quote rules take a Hebrew letter that could begin a word
                // of its own ("free"): not one reached across a middle character, nor the second
                // letter of a Hebrew-quote-Hebrew sequence, which no middle character follows
                // either ("locked").
                var afterNext = next;
                var after = next < text.Length ? Unit(text, next, out afterNext) : Other;
                var hebrewStart = hebrew && free;
                if (cls == SingleQuote && hebrewStart)
                {
                    // A letter after the quote begins a word of its own, so it stays free.
                    to = State.AfterHebrewQuote;
                }
                else if (cls == DoubleQuote ? after == HebrewLetter && hebrewStart : IsLetter(after) && !locked)
                {
                    hebrew = after == HebrewLetter;
                    locked = cls == DoubleQuote;
                    free = false;
                    i = afterNext;
                    accepted = i;
                    continue;
                }
                else
                {
                    break;
                }
            }
            else if (state == State.Number && cls is MidNum or MidNumLet or SingleQuote)
            {
                // WB11, WB12: a digit on both sides
                if (next < text.Length && Unit(text, next, out var afterNext) == Numeric)
                {
                    i = afterNext;
                    accepted = i;
                    continue;
                }
                break;
            }
            else
            {
                break;
            }

            state = to;
            i = next;
            if (state != State.LeadingConnector)
                accepted = i;
        }
        return accepted;
    }

    /// <summary>
    /// A run of Complex_Context characters; their own combining marks are Complex_Context too,
    /// but no other Extend or Format character joins the run.
    /// </summary>
    private static int MatchComplexContext(string text, int i)
    {
        while (i < text.Length)
        {
            var cls = ClassAt(text, i, out var width);
            if ((cls & ComplexContext) == 0)
                break;
            i += width;
        }
        return i;
    }
}
