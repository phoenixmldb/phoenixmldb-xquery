using System.Globalization;
using System.Numerics;
using System.Text;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:format-integer($value, $picture) as xs:string
/// </summary>
public sealed class FormatIntegerFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "format-integer");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "value"), Type = new XdmSequenceType { ItemType = ItemType.Integer, Occurrence = Occurrence.ZeroOrOne } },
        new() { Name = new QName(NamespaceId.None, "picture"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        if (arguments[0] == null)
            return ValueTask.FromResult<object?>("");

        var value = Convert.ToInt64(Execution.QueryExecutionContext.Atomize(arguments[0]), CultureInfo.InvariantCulture);
        var picture = arguments[1]?.ToString() ?? "1";

        var result = FormatIntegerStatic(value, picture, null);
        return ValueTask.FromResult<object?>(result);
    }

    internal static string FormatIntegerStatic(long value, string picture, string? lang = null)
    {
        // Empty picture is invalid
        if (string.IsNullOrEmpty(picture))
            throw new XQueryException("FODF1310", "Empty picture string for format-integer");

        // Check for ordinal modifier (e.g. "1;o", "W;o(-er)")
        // The modifier separator is the LAST semicolon (earlier ones may be grouping separators)
        var ordinal = false;
        string? ordinalSuffix = null; // explicit ordinal suffix from o(-er) parenthesized form
        var basePicture = picture;
        var semiIdx = picture.LastIndexOf(';');
        if (semiIdx >= 0)
        {
            var modifier = picture[(semiIdx + 1)..].Trim();
            basePicture = picture[..semiIdx];

            // Parse format modifier per spec: [co?] optionally followed by (string)
            // c = cardinal, o = ordinal, t = traditional (implementation-defined)
            var modIdx = 0;
            while (modIdx < modifier.Length && modifier[modIdx] is 'c' or 'o' or 't')
            {
                if (modifier[modIdx] == 'o') ordinal = true;
                modIdx++;
            }
            // Optional parenthesized suffix (e.g. "(-er)", "(-o)", "(-a)")
            if (modIdx < modifier.Length && modifier[modIdx] == '(')
            {
                var closeIdx = modifier.IndexOf(')', modIdx + 1);
                if (closeIdx < 0)
                    throw new XQueryException("FODF1310", "Unmatched parenthesis in format modifier");
                ordinalSuffix = modifier[(modIdx + 1)..closeIdx];
                modIdx = closeIdx + 1;
            }
            // Nothing should follow
            if (modIdx < modifier.Length)
                throw new XQueryException("FODF1310", $"Invalid format modifier: unexpected '{modifier[modIdx]}' after format modifier");
        }

        if (string.IsNullOrEmpty(basePicture))
            throw new XQueryException("FODF1310", "Empty primary format token for format-integer");

        // Validate: in decimal-digit pictures, '#' (optional) must precede mandatory digits
        // Uses Rune enumeration to handle non-BMP digits correctly
        if (basePicture.Contains('#', StringComparison.Ordinal))
        {
            int lastHashIdx = -1;
            int firstMandatoryIdx = -1;
            int runeIdx = 0;
            foreach (var rune in basePicture.EnumerateRunes())
            {
                if (rune.Value == '#') lastHashIdx = runeIdx;
                if (firstMandatoryIdx < 0 && Rune.IsDigit(rune)) firstMandatoryIdx = runeIdx;
                runeIdx++;
            }
            if (firstMandatoryIdx >= 0 && firstMandatoryIdx < lastHashIdx)
                throw new XQueryException("FODF1310", "Invalid picture string for format-integer: mandatory digit cannot precede optional digit '#'");
        }

        // Validate grouping separator placement in decimal-digit pictures
        if (basePicture.Contains(',', StringComparison.Ordinal))
        {
            // Trailing grouping separator is invalid
            if (basePicture.EndsWith(','))
                throw new XQueryException("FODF1310", "Invalid picture: trailing grouping separator");
            // Leading grouping separator is invalid
            if (basePicture.StartsWith(','))
                throw new XQueryException("FODF1310", "Invalid picture: leading grouping separator");
            // Adjacent grouping separators are invalid
            if (basePicture.Contains(",,", StringComparison.Ordinal))
                throw new XQueryException("FODF1310", "Invalid picture: adjacent grouping separators");
        }

        // Validate decimal-digit pictures (using Rune enumeration for non-BMP digit support):
        // 1) No trailing non-digit characters after the last digit position
        // 2) No mixed digit families
        // Only apply when the picture contains at least one digit (not just '#' and non-digits)
        bool isDecimalPicture = false;
        foreach (var rune in basePicture.EnumerateRunes())
        {
            if (Rune.IsDigit(rune)) { isDecimalPicture = true; break; }
        }
        if (isDecimalPicture)
        {
            // Check for trailing non-digit after last digit/# (using Rune enumeration)
            var pictureRunes = new List<Rune>();
            foreach (var r in basePicture.EnumerateRunes()) pictureRunes.Add(r);
            int lastDigitRuneIdx = -1;
            for (int ci = pictureRunes.Count - 1; ci >= 0; ci--)
            {
                if (Rune.IsDigit(pictureRunes[ci]) || pictureRunes[ci].Value == '#')
                { lastDigitRuneIdx = ci; break; }
            }
            if (lastDigitRuneIdx >= 0 && lastDigitRuneIdx < pictureRunes.Count - 1)
                throw new XQueryException("FODF1310",
                    $"Invalid picture: trailing non-digit character in '{basePicture}'");

            // Check for mixed digit families
            int? firstZeroCodepoint = null;
            foreach (var rune in pictureRunes)
            {
                if (Rune.IsDigit(rune))
                {
                    var zeroCodepoint = rune.Value - (int)Rune.GetNumericValue(rune);
                    if (firstZeroCodepoint == null)
                        firstZeroCodepoint = zeroCodepoint;
                    else if (zeroCodepoint != firstZeroCodepoint.Value)
                        throw new XQueryException("FODF1310",
                            $"Invalid picture: mixed digit families in '{basePicture}'");
                }
            }
        }

        // Detect Unicode numbering sequence (non-decimal, offset-based sequences like circled, parenthesized, etc.)
        var unicodeSeqResult = TryFormatUnicodeSequence(value, basePicture);
        if (unicodeSeqResult != null)
            return unicodeSeqResult;

        // Determine effective language for word formatting
        var effectiveLang = lang != null ? (lang.Contains('-') ? lang[..lang.IndexOf('-')] : lang) : "en";

        var formatted = basePicture switch
        {
            "1" => value.ToString(CultureInfo.InvariantCulture),
            "01" => value.ToString("D2", CultureInfo.InvariantCulture),
            "001" => value.ToString("D3", CultureInfo.InvariantCulture),
            "a" => ToAlpha(value, lowercase: true),
            "A" => ToAlpha(value, lowercase: false),
            "i" => ToRoman(value, lowercase: true),
            "I" => ToRoman(value, lowercase: false),
            "w" when ordinal => FormatWordWithSign(value, "w", true, effectiveLang, ordinalSuffix),
            "W" when ordinal => FormatWordWithSign(value, "W", true, effectiveLang, ordinalSuffix),
            "Ww" when ordinal => FormatWordWithSign(value, "Ww", true, effectiveLang, ordinalSuffix),
            "w" => FormatWordWithSign(value, "w", false, effectiveLang),
            "W" => FormatWordWithSign(value, "W", false, effectiveLang),
            "Ww" => FormatWordWithSign(value, "Ww", false, effectiveLang),
            _ when HasGroupingSeparator(basePicture) => FormatWithGrouping(value, basePicture),
            _ when basePicture.Length > 0 && IsDecimalDigitRune(basePicture) =>
                FormatWithDecimalDigitFamily(value, basePicture),
            _ => value.ToString(CultureInfo.InvariantCulture)
        };

        if (ordinal && (basePicture is "1" or "w" or "W" or "Ww"
            || (basePicture.Length > 0 && IsDecimalDigitRune(basePicture))
            || !formatted.Any(c => char.IsLetter(c)))) // fallback: add ordinal if result is purely numeric
        {
            // Don't double-add ordinal for word forms (already handled above)
            if (basePicture is not ("w" or "W" or "Ww"))
                formatted += GetOrdinalSuffix(value);
        }

        return formatted;
    }

    /// <summary>Checks if a picture contains grouping separators (non-digit, non-# characters between digit positions).
    /// Uses Rune enumeration to correctly handle non-BMP digits (surrogate pairs).</summary>
    private static bool HasGroupingSeparator(string picture)
    {
        var seenDigit = false;
        foreach (var rune in picture.EnumerateRunes())
        {
            if (rune.Value == '#' || Rune.IsDigit(rune)) { seenDigit = true; }
            else if (seenDigit) return true; // non-digit after a digit = separator
        }
        return false;
    }

    private static string FormatWithGrouping(long value, string picture)
    {
        // Parse the picture to extract digit positions and separators
        // E.g. "#,000" → mandatory=3, groupSize=3, separator=','
        // E.g. "#(000)000-000" → irregular grouping with different separators
        // E.g. "# 000" → separator=' '
        // Uses Rune enumeration to correctly handle non-BMP digits (e.g. Osmanya U+104A0)

        var tempDigits = new List<bool>(); // true = mandatory (digit), false = optional (#)
        var tempSeps = new List<(int pos, string sep)>(); // separator at digit-count position (string for non-BMP support)
        int zeroCodepoint = '0'; // target digit family zero codepoint
        bool detectedFamily = false;

        // Parse left to right using Rune enumeration
        var digitCount = 0;
        foreach (var rune in picture.EnumerateRunes())
        {
            if (rune.Value == '#')
            {
                tempDigits.Add(false);
                digitCount++;
            }
            else if (Rune.IsDigit(rune))
            {
                tempDigits.Add(true);
                digitCount++;
                if (!detectedFamily)
                {
                    var numVal = (int)Rune.GetNumericValue(rune);
                    zeroCodepoint = rune.Value - numVal;
                    detectedFamily = true;
                }
            }
            else
            {
                // Grouping separator (supports both BMP and non-BMP characters)
                tempSeps.Add((digitCount, rune.ToString()));
            }
        }

        // Convert separator positions from left-to-right to right-to-left
        var totalDigits = digitCount;
        var sepsFromRight = new List<(int posFromRight, string sep)>();
        foreach (var (pos, sep) in tempSeps)
        {
            if (pos > 0 && pos < totalDigits) // skip leading/trailing separators
                sepsFromRight.Add((totalDigits - pos, sep));
        }

        // Count mandatory digits (non-# digit chars)
        var minDigits = tempDigits.Count(d => d);
        if (minDigits == 0) minDigits = 1;

        var absValue = Math.Abs(value);
        var digits = absValue.ToString(CultureInfo.InvariantCulture);

        // Pad with leading zeros if needed
        if (digits.Length < minDigits)
            digits = digits.PadLeft(minDigits, '0');

        // Insert grouping separators, repeating the leftmost group pattern for extra digits
        if (sepsFromRight.Count > 0)
        {
            // Find the primary grouping separator (most frequent, or rightmost if tied)
            var sepStr = sepsFromRight
                .GroupBy(s => s.sep)
                .OrderByDescending(g => g.Count())
                .ThenByDescending(g => g.Max(s => s.posFromRight))
                .First().Key;

            // Determine whether grouping is regular (all group sizes equal).
            // Only repeat the pattern beyond explicit positions if regular.
            var leftmostSepPos = sepsFromRight.Max(s => s.posFromRight);
            var sortedSeps = sepsFromRight.OrderBy(s => s.posFromRight).ToList();

            // Compute group sizes: rightmost group = first sep pos, then differences
            var groupSizes = new List<int> { sortedSeps[0].posFromRight };
            for (int gi = 1; gi < sortedSeps.Count; gi++)
                groupSizes.Add(sortedSeps[gi].posFromRight - sortedSeps[gi - 1].posFromRight);

            // Include leftmost group (digits before leftmost separator) in regularity check
            // only when there are 2+ separators, the leftmost group is all mandatory digits,
            // and its size differs from the inter-separator groups.
            // A single separator always defines a regular interval by itself.
            if (sortedSeps.Count >= 2)
            {
                int leftmostGroupSize = totalDigits - leftmostSepPos;
                bool leftmostHasOptional = false;
                for (int di = 0; di < totalDigits - leftmostSepPos; di++)
                {
                    if (!tempDigits[di]) { leftmostHasOptional = true; break; }
                }
                if (leftmostGroupSize > 0 && !leftmostHasOptional)
                    groupSizes.Add(leftmostGroupSize);
            }

            // Regular if all group sizes are the same
            bool isRegular = groupSizes.All(g => g == groupSizes[0]);
            int repeatGroupSize = isRegular ? groupSizes[0] : 0;

            var sb = new StringBuilder();
            int digitIdx = 0;
            for (var i = digits.Length - 1; i >= 0; i--)
            {
                if (digitIdx > 0)
                {
                    // Check explicit separator positions
                    bool inserted = false;
                    foreach (var (pos, sep) in sepsFromRight)
                    {
                        if (pos == digitIdx)
                        {
                            sb.Insert(0, sep);
                            inserted = true;
                            break;
                        }
                    }
                    // Beyond explicit positions: repeat leftmost group size
                    if (!inserted && digitIdx > leftmostSepPos
                        && repeatGroupSize > 0
                        && (digitIdx - leftmostSepPos) % repeatGroupSize == 0)
                    {
                        sb.Insert(0, sepStr);
                    }
                }
                sb.Insert(0, digits[i]);
                digitIdx++;
            }
            digits = sb.ToString();
        }

        // Convert ASCII digits to target digit family if non-ASCII
        if (zeroCodepoint != '0')
        {
            var sb = new StringBuilder(digits.Length * 2);
            foreach (var ch in digits)
            {
                if (ch >= '0' && ch <= '9')
                {
                    var targetRune = new Rune(zeroCodepoint + (ch - '0'));
                    sb.Append(targetRune.ToString());
                }
                else
                    sb.Append(ch);
            }
            digits = sb.ToString();
        }

        return value < 0 ? "-" + digits : digits;
    }

    private static string ToAlpha(long number, bool lowercase)
    {
        if (number <= 0) return number.ToString(CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        var n = number;
        while (n > 0)
        {
            n--;
            var c = (char)((n % 26) + (lowercase ? 'a' : 'A'));
            sb.Insert(0, c);
            n /= 26;
        }
        return sb.ToString();
    }

    private static string ToRoman(long number, bool lowercase)
    {
        if (number <= 0 || number >= 4000)
            return number.ToString(CultureInfo.InvariantCulture);

        ReadOnlySpan<(int value, string numeral)> romanNumerals =
        [
            (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"),
            (100, "C"), (90, "XC"), (50, "L"), (40, "XL"),
            (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")
        ];

        var sb = new StringBuilder();
        var n = (int)number;
        foreach (var (value, numeral) in romanNumerals)
        {
            while (n >= value) { sb.Append(numeral); n -= value; }
        }
        return lowercase ? sb.ToString().ToLowerInvariant() : sb.ToString();
    }

    private static string FormatWordWithSign(long value, string format, bool ordinal, string lang = "en", string? ordinalSuffix = null)
    {
        var sign = value < 0 ? "-" : "";
        var absValue = Math.Abs(value);

        // Use locale-specific formatting for supported languages
        string words;
        if (lang != "en" && LocalizedWordFormatters.TryGetValue(lang, out var formatter))
        {
            words = ordinal ? formatter.ToOrdinalWords(absValue, ordinalSuffix) : formatter.ToCardinalWords(absValue);
            if (absValue == 0) words = ordinal ? formatter.ZeroOrdinal : formatter.Zero;
        }
        else
        {
            words = ordinal ? NumberToOrdinalWordsPositive(absValue) : NumberToWordsPositive(absValue);
            if (absValue == 0) words = ordinal ? "zeroth" : "zero";
        }

        words = format switch
        {
            "W" => words.ToUpperInvariant(),
            "Ww" => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(words),
            _ => words.ToLowerInvariant()
        };
        return sign + words;
    }

    private static string NumberToWords(long number)
    {
        if (number == 0) return "zero";
        if (number < 0) return "minus " + NumberToWordsPositive(Math.Abs(number));
        return NumberToWordsPositive(number);
    }

    private static string NumberToOrdinalWords(long number)
    {
        if (number == 0) return "zeroth";
        if (number < 0) return "minus " + NumberToOrdinalWordsPositive(Math.Abs(number));
        return NumberToOrdinalWordsPositive(number);
    }

    private static string NumberToOrdinalWordsPositive(long number)
    {
        var cardinal = NumberToWordsPositive(number);
        if (number == 0) cardinal = "zero";
        return MakeOrdinalWord(cardinal);
    }

    /// <summary>Converts a cardinal word string to ordinal by replacing the last word.</summary>
    private static string MakeOrdinalWord(string cardinal)
    {
        // Handle irregular ordinals by replacing the last word
        var irregulars = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["one"] = "first", ["two"] = "second", ["three"] = "third",
            ["five"] = "fifth", ["eight"] = "eighth", ["nine"] = "ninth",
            ["twelve"] = "twelfth", ["zero"] = "zeroth"
        };

        // Find the last word
        var lastSpace = cardinal.LastIndexOf(' ');
        var prefix = lastSpace >= 0 ? cardinal[..(lastSpace + 1)] : "";
        var lastWord = lastSpace >= 0 ? cardinal[(lastSpace + 1)..] : cardinal;

        if (irregulars.TryGetValue(lastWord, out var ordinal))
            return prefix + ordinal;
        if (lastWord.EndsWith('y'))
            return prefix + lastWord[..^1] + "ieth";
        if (lastWord.EndsWith("ve", StringComparison.Ordinal))
            return prefix + lastWord[..^2] + "fth";
        if (lastWord.EndsWith('t'))
            return prefix + lastWord + "h";
        if (lastWord.EndsWith('e'))
            return prefix + lastWord[..^1] + "th";
        return prefix + lastWord + "th";
    }

    private static string NumberToWordsPositive(long number)
    {
        if (number == 0) return "";
        string[] ones = ["", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine",
            "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen"];
        string[] tens = ["", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"];

        if (number < 20) return ones[number];
        if (number < 100)
        {
            var o = number % 10;
            return o > 0 ? $"{tens[number / 10]} {ones[o]}" : tens[number / 10];
        }
        if (number < 1000)
        {
            var rest = number % 100;
            return rest > 0 ? $"{ones[number / 100]} hundred and {NumberToWordsPositive(rest)}" : $"{ones[number / 100]} hundred";
        }

        (string name, long divisor)[] groups = [
            ("quintillion", 1_000_000_000_000_000_000L),
            ("quadrillion", 1_000_000_000_000_000L),
            ("trillion", 1_000_000_000_000L),
            ("billion", 1_000_000_000L),
            ("million", 1_000_000L),
            ("thousand", 1_000L)
        ];

        foreach (var (name, divisor) in groups)
        {
            if (number >= divisor)
            {
                var high = NumberToWordsPositive(number / divisor);
                var rest = number % divisor;
                var connector = rest > 0 && rest < 100 ? " and " : " ";
                return rest > 0 ? $"{high} {name}{connector}{NumberToWordsPositive(rest)}" : $"{high} {name}";
            }
        }

        return number.ToString(CultureInfo.InvariantCulture);
    }

    private static string GetOrdinalSuffix(long number)
    {
        var abs = Math.Abs(number);
        var lastTwo = abs % 100;
        if (lastTwo >= 11 && lastTwo <= 13) return "th";
        return (abs % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
    }

    /// <summary>Checks if the first character/rune in the picture is a decimal digit (Nd category).</summary>
    private static bool IsDecimalDigitRune(string picture)
    {
        if (picture.Length == 0) return false;
        var firstRune = Rune.GetRuneAt(picture, 0);
        return Rune.IsDigit(firstRune);
    }

    /// <summary>Formats a value using a non-ASCII decimal digit family, handling padding and digit conversion.</summary>
    private static string FormatWithDecimalDigitFamily(long value, string basePicture)
    {
        // Determine the zero digit of the target family from the first rune
        var firstRune = Rune.GetRuneAt(basePicture, 0);
        var numVal = (int)Rune.GetNumericValue(firstRune);
        int zeroCodepoint = firstRune.Value - numVal;

        // Count digit positions in the picture (using Rune enumeration for non-BMP)
        int digitCount = 0;
        foreach (var rune in basePicture.EnumerateRunes())
        {
            if (Rune.IsDigit(rune) || rune.Value == '#') digitCount++;
        }
        if (digitCount == 0) digitCount = 1;

        // Format as ASCII first, then convert
        var ascii = Math.Abs(value).ToString($"D{digitCount}", CultureInfo.InvariantCulture);

        // Convert to target digit family
        if (zeroCodepoint == '0')
            return value < 0 ? "-" + ascii : ascii;

        var sb = new StringBuilder(ascii.Length * 2);
        if (value < 0) sb.Append('-');
        foreach (var ch in ascii)
        {
            if (ch >= '0' && ch <= '9')
            {
                var targetRune = new Rune(zeroCodepoint + (ch - '0'));
                sb.Append(targetRune.ToString());
            }
            else
                sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Tries to format a value using a Unicode numbering sequence (non-decimal digit systems).
    /// Supports: circled digits (U+2460), parenthesized digits (U+2474), full-stopped digits (U+2488),
    /// Greek uppercase (U+0391) and lowercase (U+03B1) alphabetic numbering,
    /// and other offset-based sequences where consecutive codepoints represent consecutive values.
    /// Returns null if the picture doesn't match a known sequence.
    /// </summary>
    private static string? TryFormatUnicodeSequence(long value, string basePicture)
    {
        if (basePicture.Length == 0) return null;
        var firstRune = Rune.GetRuneAt(basePicture, 0);

        // Skip if it's a decimal digit (handled elsewhere), '#', or ASCII letter (a/A/i/I/w/W)
        if (Rune.IsDigit(firstRune)) return null;
        if (firstRune.Value == '#') return null;
        if (firstRune.Value < 128) return null; // ASCII letters handled by main switch

        int cp = firstRune.Value;

        // Greek uppercase alphabetic: Α(U+0391)=1, Β(U+0392)=2, ... up to Ω(U+03A9)
        if (cp >= 0x0391 && cp <= 0x03A9)
            return ToGreekAlpha(value, uppercase: true);

        // Greek lowercase alphabetic: α(U+03B1)=1, β(U+03B2)=2, ... up to ω(U+03C9)
        if (cp >= 0x03B1 && cp <= 0x03C9)
            return ToGreekAlpha(value, uppercase: false);

        // CJK ideographic numbering: 一(U+4E00)
        if (cp == 0x4E00)
            return ToCjkNumber(value);

        // Offset-based sequences: the first codepoint represents value 1
        // Circled digits: ①(U+2460)=1 through ⑳(U+2473)=20
        // Parenthesized digits: ⑴(U+2474)=1 through ⒇(U+2487)=20
        // Full-stopped digits: ⒈(U+2488)=1 through ⒛(U+249B)=20
        if (TryGetOffsetSequenceRange(cp, out int seqStart, out int seqMax))
        {
            // For values within range, use the offset sequence
            if (value >= 1 && value <= seqMax)
                return new Rune(seqStart + (int)value - 1).ToString();
            // Fallback for out-of-range values
            return value.ToString(CultureInfo.InvariantCulture);
        }

        return null;
    }

    private static bool TryGetOffsetSequenceRange(int codepoint, out int start, out int maxVal)
    {
        if (codepoint >= 0x2460 && codepoint <= 0x2473)
        { start = 0x2460; maxVal = 20; return true; }
        if (codepoint >= 0x2474 && codepoint <= 0x2487)
        { start = 0x2474; maxVal = 20; return true; }
        if (codepoint >= 0x2488 && codepoint <= 0x249B)
        { start = 0x2488; maxVal = 20; return true; }
        start = 0; maxVal = 0; return false;
    }

    /// <summary>Greek alphabetic numbering (Milesian/Ionic system simplified to sequential).</summary>
    private static string ToGreekAlpha(long value, bool uppercase)
    {
        if (value <= 0) return value.ToString(CultureInfo.InvariantCulture);
        // Simple sequential mapping: 1=Α/α, 2=Β/β, etc.
        int baseChar = uppercase ? 0x0391 : 0x03B1;
        // Greek alphabet has 24 letters (some gaps in Unicode: skip U+03A2/U+03C2 final sigma position)
        // Α(0391)-Ρ(03A1) = 17 letters, then skip 03A2, Σ(03A3)-Ω(03A9) = 7 letters = 24 total
        var sb = new StringBuilder();
        var n = value;
        while (n > 0)
        {
            n--;
            int idx = (int)(n % 24);
            // Map index to codepoint, skipping the gap at position 17 (U+03A2 / U+03C2)
            int cp = baseChar + idx;
            if (idx >= 17) cp++; // skip the gap (U+03A2 for uppercase, U+03C2 for lowercase)
            sb.Insert(0, new Rune(cp).ToString());
            n /= 24;
        }
        return sb.ToString();
    }

    /// <summary>CJK ideographic number formatting (Japanese-style: omit 一 before 十/百 for 10-19 and 100-199).</summary>
    private static string ToCjkNumber(long value)
    {
        if (value == 0) return "\u96F6"; // 零
        if (value < 0) return "-" + ToCjkNumber(-value);

        string[] digits = ["\u96F6", "\u4E00", "\u4E8C", "\u4E09", "\u56DB", "\u4E94", "\u516D", "\u4E03", "\u516B", "\u4E5D"];
        // 零一二三四五六七八九

        if (value < 10) return digits[value];

        var result = new StringBuilder();
        ToCjkNumberRecursive(value, result, digits, true);
        return result.ToString();
    }

    private static void ToCjkNumberRecursive(long value, StringBuilder result, string[] digits, bool topLevel)
    {
        if (value == 0) return;

        (long divisor, string unit)[] ranks =
        [
            (1_0000_0000_0000L, "\u5146"), // 兆
            (1_0000_0000L, "\u4EBF"),       // 亿
            (1_0000L, "\u4E07"),            // 万
            (1000, "\u5343"),               // 千
            (100, "\u767E"),                // 百
            (10, "\u5341"),                 // 十
        ];

        foreach (var (divisor, unit) in ranks)
        {
            if (value >= divisor)
            {
                var high = value / divisor;
                var low = value % divisor;
                // Omit 一 before 十 and 百 when at top-level and high==1
                if (high == 1 && topLevel && (divisor == 10 || divisor == 100))
                    result.Append(unit);
                else
                {
                    ToCjkNumberRecursive(high, result, digits, false);
                    result.Append(unit);
                }
                if (low > 0)
                    ToCjkNumberRecursive(low, result, digits, false);
                return;
            }
        }

        // Single digit (1-9)
        if (value >= 1 && value <= 9)
            result.Append(digits[value]);
    }

    /// <summary>Interface for locale-specific number word formatting.</summary>
    private sealed class LocaleWordFormatter
    {
        public required Func<long, string> ToCardinalWords;
        public required Func<long, string?, string> ToOrdinalWords;
        public required string Zero;
        public required string ZeroOrdinal;
    }

    private static readonly Dictionary<string, LocaleWordFormatter> LocalizedWordFormatters = new(StringComparer.OrdinalIgnoreCase)
    {
        ["de"] = new LocaleWordFormatter
        {
            Zero = "null",
            ZeroOrdinal = "nullter",
            ToCardinalWords = GermanCardinal,
            ToOrdinalWords = (n, suffix) => GermanOrdinal(n, suffix)
        },
        ["fr"] = new LocaleWordFormatter
        {
            Zero = "z\u00e9ro",
            ZeroOrdinal = "z\u00e9roi\u00e8me",
            ToCardinalWords = FrenchCardinal,
            ToOrdinalWords = (n, _) => FrenchOrdinal(n)
        },
        ["it"] = new LocaleWordFormatter
        {
            Zero = "zero",
            ZeroOrdinal = "zeresimo",
            ToCardinalWords = ItalianCardinal,
            ToOrdinalWords = (n, suffix) => ItalianOrdinal(n, suffix)
        }
    };

    private static string GermanCardinal(long n)
    {
        if (n == 0) return "null";
        string[] ones = ["", "eins", "zwei", "drei", "vier", "f\u00fcnf", "sechs", "sieben", "acht", "neun",
            "zehn", "elf", "zw\u00f6lf", "dreizehn", "vierzehn", "f\u00fcnfzehn", "sechzehn", "siebzehn", "achtzehn", "neunzehn"];
        string[] tens = ["", "", "zwanzig", "drei\u00dfig", "vierzig", "f\u00fcnfzig", "sechzig", "siebzig", "achtzig", "neunzig"];

        if (n < 20) return ones[n];
        if (n < 100)
        {
            var o = n % 10;
            return o > 0 ? $"{(o == 1 ? "ein" : ones[o])}und{tens[n / 10]}" : tens[n / 10];
        }
        if (n < 1000)
        {
            var rest = n % 100;
            var h = ones[n / 100] == "eins" ? "ein" : ones[n / 100];
            return rest > 0 ? $"{h}hundert{GermanCardinal(rest)}" : $"{h}hundert";
        }
        if (n < 1_000_000)
        {
            var rest = n % 1000;
            var t = n / 1000;
            var tWord = t == 1 ? "ein" : GermanCardinal(t);
            return rest > 0 ? $"{tWord}tausend{GermanCardinal(rest)}" : $"{tWord}tausend";
        }
        return n.ToString(CultureInfo.InvariantCulture); // fallback
    }

    private static string GermanOrdinal(long n, string? suffix)
    {
        // German ordinals: cardinal + "ter" (or custom suffix like "-er")
        // Special forms: 1=erster, 3=dritter, 7=siebter, 8=achter
        var sfx = suffix ?? "-ter";
        if (sfx.StartsWith('-')) sfx = sfx[1..];

        if (n == 1) return $"erst{sfx}";
        if (n == 3) return $"dritt{sfx}";
        if (n == 7) return $"siebt{sfx}";
        if (n == 8) return $"acht{sfx}";

        var cardinal = GermanCardinal(n);
        // Standard: add "t" for 2-19, "st" for 20+
        if (n < 20) return $"{cardinal}t{sfx}";
        return $"{cardinal}st{sfx}";
    }

    private static string FrenchCardinal(long n)
    {
        if (n == 0) return "z\u00e9ro";
        string[] ones = ["", "un", "deux", "trois", "quatre", "cinq", "six", "sept", "huit", "neuf",
            "dix", "onze", "douze", "treize", "quatorze", "quinze", "seize", "dix-sept", "dix-huit", "dix-neuf"];
        if (n < 20) return ones[n];
        if (n < 100)
        {
            var t = n / 10;
            var o = n % 10;
            var tensWord = t switch
            {
                2 => "vingt",
                3 => "trente",
                4 => "quarante",
                5 => "cinquante",
                6 => "soixante",
                7 => o < 10 ? "soixante" : "soixante",
                8 => "quatre-vingt",
                9 => "quatre-vingt",
                _ => ""
            };
            if (t == 7) return o == 1 ? "soixante et onze" : $"soixante-{ones[10 + (int)o]}";
            if (t == 9) return $"quatre-vingt-{ones[10 + (int)o]}";
            if (o == 0) return t == 8 ? "quatre-vingts" : tensWord;
            if (o == 1 && t <= 6) return $"{tensWord} et un";
            return $"{tensWord}-{ones[o]}";
        }
        if (n < 1000)
        {
            var h = n / 100;
            var rest = n % 100;
            var hWord = h == 1 ? "cent" : $"{FrenchCardinal(h)} cent";
            if (rest == 0 && h > 1) return $"{FrenchCardinal(h)} cents";
            return rest > 0 ? $"{hWord} {FrenchCardinal(rest)}" : hWord;
        }
        if (n < 1_000_000)
        {
            var t = n / 1000;
            var rest = n % 1000;
            var tWord = t == 1 ? "mille" : $"{FrenchCardinal(t)} mille";
            return rest > 0 ? $"{tWord} {FrenchCardinal(rest)}" : tWord;
        }
        return n.ToString(CultureInfo.InvariantCulture);
    }

    private static string FrenchOrdinal(long n)
    {
        if (n == 1) return "premi\u00e8re"; // première (default feminine; spec tests use "deuxième" form)
        // Actually, spec test expects "Deuxième" for format-integer(2, 'Ww;o', 'fr')
        // French ordinals: cardinal + "ième", with adjustments
        if (n == 1) return "premier";
        var cardinal = FrenchCardinal(n);
        // Remove trailing 'e' before adding -ième
        if (cardinal.EndsWith('e')) cardinal = cardinal[..^1];
        // Special: cinq → cinquième, neuf → neuvième
        if (cardinal.EndsWith('q')) cardinal += 'u'; // cinq → cinqu
        if (cardinal.EndsWith('f')) cardinal = cardinal[..^1] + 'v'; // neuf → neuv
        return cardinal + "i\u00e8me";
    }

    private static string ItalianCardinal(long n)
    {
        if (n == 0) return "zero";
        string[] ones = ["", "uno", "due", "tre", "quattro", "cinque", "sei", "sette", "otto", "nove",
            "dieci", "undici", "dodici", "tredici", "quattordici", "quindici", "sedici", "diciassette", "diciotto", "diciannove"];
        string[] tens = ["", "", "venti", "trenta", "quaranta", "cinquanta", "sessanta", "settanta", "ottanta", "novanta"];
        if (n < 20) return ones[n];
        if (n < 100)
        {
            var t = n / 10;
            var o = n % 10;
            if (o == 0) return tens[t];
            var tensW = tens[t];
            // Drop final vowel before 'uno' and 'otto'
            if (o == 1 || o == 8) tensW = tensW[..^1];
            return $"{tensW}{ones[o]}";
        }
        if (n < 1000)
        {
            var h = n / 100;
            var rest = n % 100;
            var hWord = h == 1 ? "cento" : $"{ItalianCardinal(h)}cento";
            return rest > 0 ? $"{hWord}{ItalianCardinal(rest)}" : hWord;
        }
        if (n < 1_000_000)
        {
            var t = n / 1000;
            var rest = n % 1000;
            var tWord = t == 1 ? "mille" : $"{ItalianCardinal(t)}mila";
            return rest > 0 ? $"{tWord}{ItalianCardinal(rest)}" : tWord;
        }
        return n.ToString(CultureInfo.InvariantCulture);
    }

    private static string ItalianOrdinal(long n, string? suffix)
    {
        // Italian ordinals: special forms for 1-10, then cardinal stem + "-esimo/-esima"
        var sfx = suffix ?? "-o"; // default masculine
        if (sfx.StartsWith('-')) sfx = sfx[1..];
        var isFeminine = sfx.EndsWith('a');

        string[] ordinals = ["", "prim", "second", "terz", "quart", "quint", "sest", "settim", "ottav", "non", "decim"];
        if (n >= 1 && n <= 10) return ordinals[n] + sfx;

        // For numbers > 10: cardinal stem + "esimo/esima"
        var cardinal = ItalianCardinal(n);
        // Remove trailing vowel
        if (cardinal.Length > 0 && "aeiou".Contains(cardinal[^1]))
            cardinal = cardinal[..^1];
        return cardinal + (isFeminine ? "esima" : "esimo");
    }
}

/// <summary>
/// fn:format-integer($value, $picture, $lang) as xs:string (3-argument version)
/// </summary>
public sealed class FormatIntegerFunction3 : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "format-integer");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "value"), Type = new XdmSequenceType { ItemType = ItemType.Integer, Occurrence = Occurrence.ZeroOrOne } },
        new() { Name = new QName(NamespaceId.None, "picture"), Type = XdmSequenceType.String },
        new() { Name = new QName(NamespaceId.None, "lang"), Type = new XdmSequenceType { ItemType = ItemType.String, Occurrence = Occurrence.ZeroOrOne } }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        if (arguments[0] == null)
            return ValueTask.FromResult<object?>("");

        var value = Convert.ToInt64(Execution.QueryExecutionContext.Atomize(arguments[0]), CultureInfo.InvariantCulture);
        var picture = arguments[1]?.ToString() ?? "1";
        var lang = arguments[2]?.ToString();

        var result = FormatIntegerFunction.FormatIntegerStatic(value, picture, lang);
        return ValueTask.FromResult<object?>(result);
    }
}

/// <summary>
/// fn:format-number($value, $picture) as xs:string
/// </summary>
public sealed class FormatNumberFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "format-number");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "value"), Type = new XdmSequenceType { ItemType = ItemType.Double, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "picture"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        // The picture parameter must be xs:string (XPTY0004 for non-string atomics)
        var pictureArg = Execution.QueryExecutionContext.Atomize(arguments[1]);
        if (pictureArg is not null and not string and not Xdm.XsUntypedAtomic)
            throw new XQueryRuntimeException("XPTY0004",
                "fn:format-number picture argument must be a string");
        var df = GetDecimalFormat(context, null);
        var result = FormatNumberImpl(arguments[0], pictureArg?.ToString() ?? "", df);
        return ValueTask.FromResult<object?>(result);
    }

    internal static Analysis.DecimalFormatProperties GetDecimalFormat(Ast.ExecutionContext context, string? name)
    {
        var key = name ?? "";
        if (context.DecimalFormats != null && context.DecimalFormats.TryGetValue(key, out var df))
            return df;
        return Analysis.DecimalFormatProperties.Default;
    }

    internal static string FormatNumberImpl(object? rawValue, string picture, Analysis.DecimalFormatProperties df)
    {
        var atomized = Execution.QueryExecutionContext.Atomize(rawValue);
        double value;
        // Preserve original decimal value for full precision formatting
        decimal? originalDecimal = null;
        try
        {
            switch (atomized)
            {
                case null:
                    value = double.NaN;
                    break;
                case double d:
                    value = d;
                    break;
                case float f:
                    value = (double)f;
                    break;
                case decimal m:
                    originalDecimal = m;
                    value = (double)m;
                    break;
                case long l:
                    originalDecimal = (decimal)l;
                    value = (double)l;
                    break;
                case int i:
                    originalDecimal = (decimal)i;
                    value = (double)i;
                    break;
                case BigInteger bi:
                    // BigInteger may exceed double range; try conversion
                    try { value = (double)bi; }
                    catch (OverflowException) { value = double.PositiveInfinity; }
                    // Try decimal for precision if it fits
                    try { originalDecimal = (decimal)bi; }
                    catch (OverflowException)
                    {
                        // Value exceeds decimal precision — raise FOAR0002 (numeric overflow)
                        // as permitted by the spec for implementation-defined integer limits
                        throw new XQueryRuntimeException("FOAR0002",
                            $"Integer value too large for format-number: {bi}");
                    }
                    break;
                case string s:
                    if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var dv))
                        value = dv;
                    else
                        value = double.NaN;
                    break;
                default:
                    value = Convert.ToDouble(atomized, CultureInfo.InvariantCulture);
                    break;
            }
        }
        catch (FormatException) { value = double.NaN; }
        catch (InvalidCastException) { value = double.NaN; }

        // Handle non-BMP characters in the decimal format:
        // - Non-BMP zero-digit (e.g., Osmanya U+10480)
        // - Non-BMP decimal/grouping separators
        // Strategy: normalize picture to BMP chars, process with BMP, denormalize result.
        bool nonBmpDigits = df.ZeroDigitCodePoint > 0xFFFF;
        int nonBmpZeroCodePoint = nonBmpDigits ? df.ZeroDigitCodePoint : 0;
        string? nonBmpDecimalSep = df.DecimalSeparatorFull;
        string? nonBmpGroupingSep = df.GroupingSeparatorFull;
        bool hasNonBmpSeparators = nonBmpDecimalSep != null || nonBmpGroupingSep != null;

        if (nonBmpDigits)
        {
            picture = NormalizePictureNonBmp(picture, df.ZeroDigitCodePoint);
        }

        // Normalize non-BMP separators in the picture to BMP placeholders
        if (hasNonBmpSeparators)
        {
            if (nonBmpDecimalSep != null)
                picture = picture.Replace(nonBmpDecimalSep, ".");
            if (nonBmpGroupingSep != null)
                picture = picture.Replace(nonBmpGroupingSep, ",");
        }

        if (nonBmpDigits || hasNonBmpSeparators)
        {
            // Create a modified df that uses BMP characters for all processing
            df = new Analysis.DecimalFormatProperties
            {
                DecimalSeparator = nonBmpDecimalSep != null ? '.' : df.DecimalSeparator,
                GroupingSeparator = nonBmpGroupingSep != null ? ',' : df.GroupingSeparator,
                Infinity = df.Infinity,
                MinusSign = df.MinusSign,
                NaN = df.NaN,
                Percent = df.Percent,
                PerMille = df.PerMille,
                ZeroDigit = nonBmpDigits ? '0' : df.ZeroDigit,
                ZeroDigitCodePoint = nonBmpDigits ? '0' : df.ZeroDigitCodePoint,
                Digit = df.Digit,
                PatternSeparator = df.PatternSeparator,
                ExponentSeparator = df.ExponentSeparator
            };
        }

        // Split into sub-pictures using the pattern-separator
        var subPictures = SplitSubPictures(picture, df.PatternSeparator);
        if (subPictures.Length == 0 || subPictures.Length > 2)
            throw new XQueryRuntimeException("FODF1310", $"Invalid picture string: {picture}");

        // Each sub-picture's mantissa must contain at least one digit character (zero-digit or
        // optional-digit). Digits in the exponent part don't count.
        foreach (var sp in subPictures)
        {
            var bodyStr = GetBody(sp, df);
            // Find where exponent part starts in the body
            int expStart = -1;
            for (int i = 0; i < bodyStr.Length; i++)
            {
                if (bodyStr[i] == df.ExponentSeparator && i + 1 < bodyStr.Length
                    && IsZeroDigit(bodyStr[i + 1], df))
                { expStart = i; break; }
            }
            var mantissa = expStart >= 0 ? bodyStr[..expStart] : bodyStr;
            bool hasDigit = false;
            foreach (var c in mantissa)
            {
                if (c == df.Digit || IsZeroDigit(c, df)) { hasDigit = true; break; }
            }
            if (!hasDigit)
                throw new XQueryRuntimeException("FODF1310",
                    $"Invalid picture: sub-picture '{sp}' contains no digit character in mantissa");
        }
        // Validate the fractional part of each sub-picture: after decimal-separator the
        // mandatory-digits (zero-digit) must precede optional-digits.
        // e.g. "000.##0" and "000.$$0" (with digit='$') are invalid.
        foreach (var sp in subPictures)
        {
            var dsp = sp.IndexOf(df.DecimalSeparator);
            if (dsp < 0) continue;
            var frac = sp[(dsp + 1)..];
            // Strip exponent part (e.g., "e99" in "#.#e99") before fractional validation
            int expIdx = frac.IndexOf(df.ExponentSeparator);
            if (expIdx >= 0)
                frac = frac[..expIdx];
            // Strip trailing non-digit (suffix) characters.
            int end = frac.Length;
            while (end > 0 && frac[end - 1] != df.Digit && !IsZeroDigit(frac[end - 1], df))
                end--;
            var fracDigits = frac[..end];
            // In fractional part: zero-digits must precede optional-digits.
            bool seenOptional = false;
            foreach (var c in fracDigits)
            {
                if (c == df.Digit) seenOptional = true;
                else if (IsZeroDigit(c, df) && seenOptional)
                    throw new XQueryRuntimeException("FODF1310",
                        $"Invalid picture: mandatory digit after optional digit in fractional part of '{sp}'");
            }
        }

        // Validate grouping separator positions in each sub-picture
        foreach (var sp in subPictures)
        {
            var bodyStr = GetBody(sp, df);
            for (int i = 0; i < bodyStr.Length; i++)
            {
                if (bodyStr[i] == df.GroupingSeparator)
                {
                    // Adjacent grouping separators
                    if (i + 1 < bodyStr.Length && bodyStr[i + 1] == df.GroupingSeparator)
                        throw new XQueryRuntimeException("FODF1310",
                            $"Invalid picture: adjacent grouping separators in '{sp}'");
                    // Grouping separator adjacent to decimal separator
                    if (i + 1 < bodyStr.Length && bodyStr[i + 1] == df.DecimalSeparator)
                        throw new XQueryRuntimeException("FODF1310",
                            $"Invalid picture: grouping separator adjacent to decimal separator in '{sp}'");
                    if (i > 0 && bodyStr[i - 1] == df.DecimalSeparator)
                        throw new XQueryRuntimeException("FODF1310",
                            $"Invalid picture: grouping separator adjacent to decimal separator in '{sp}'");
                    // Grouping separator at end of integer part (before decimal or end of body)
                    if (i == bodyStr.Length - 1 || (i + 1 < bodyStr.Length && bodyStr[i + 1] == df.DecimalSeparator))
                    {
                        // Check if there's a decimal separator after — if so, this is end of integer part
                        // If it's the last char and no decimal, it's at the end of the body
                        bool atEndOfInt = i == bodyStr.Length - 1;
                        if (!atEndOfInt)
                        {
                            // Check if next non-separator char is decimal
                            atEndOfInt = bodyStr[i + 1] == df.DecimalSeparator;
                        }
                        // Actually already handled by decimal adjacency check above
                    }
                }
            }
            // Grouping separator at the very end of the integer part (no digits after it before decimal)
            var decPos = bodyStr.IndexOf(df.DecimalSeparator);
            var intBody = decPos >= 0 ? bodyStr[..decPos] : bodyStr;
            if (intBody.Length > 0 && intBody[^1] == df.GroupingSeparator)
                throw new XQueryRuntimeException("FODF1310",
                    $"Invalid picture: grouping separator at end of integer part in '{sp}'");
            // Note: leading grouping separator (e.g., ",##0") is valid per spec — it gets ignored
        }

        // Validate that the body contains only active characters and at most one exponent part.
        // Also: percent/per-mille and exponent separator are mutually exclusive.
        foreach (var sp in subPictures)
        {
            var bodyStr = GetBody(sp, df);
            var prefix = GetPrefix(sp, df);
            var suffix = GetSuffix(sp, df);
            bool hasPercent = sp.Contains(df.Percent) || sp.Contains(df.PerMille);
            int exponentCount = 0;
            for (int i = 0; i < bodyStr.Length; i++)
            {
                char c = bodyStr[i];
                if (IsZeroDigit(c, df) || c == df.Digit || c == df.DecimalSeparator
                    || c == df.GroupingSeparator)
                    continue;
                if (c == df.ExponentSeparator && i + 1 < bodyStr.Length && IsZeroDigit(bodyStr[i + 1], df))
                {
                    exponentCount++;
                    if (exponentCount > 1)
                        throw new XQueryRuntimeException("FODF1310",
                            $"Invalid picture: multiple exponent separators in sub-picture '{sp}'");
                    if (hasPercent)
                        throw new XQueryRuntimeException("FODF1310",
                            $"Invalid picture: exponent separator with percent/per-mille in sub-picture '{sp}'");
                    continue;
                }
                throw new XQueryRuntimeException("FODF1310",
                    $"Invalid picture: passive character '{c}' found in body of sub-picture '{sp}'");
            }
        }

        // Handle NaN
        if (double.IsNaN(value))
            return df.NaN;

        // Handle Infinity
        if (double.IsPositiveInfinity(value))
        {
            var prefix = GetPrefix(subPictures[0], df);
            var suffix = GetSuffix(subPictures[0], df);
            return prefix + df.Infinity + suffix;
        }
        if (double.IsNegativeInfinity(value))
        {
            var subPic = subPictures.Length > 1 ? subPictures[1] : subPictures[0];
            var prefix = GetPrefix(subPic, df);
            var suffix = GetSuffix(subPic, df);
            if (subPictures.Length > 1)
                return prefix + df.Infinity + suffix;
            return df.MinusSign + prefix + df.Infinity + suffix;
        }

        // Select sub-picture
        bool isNegative = value < 0 || (value == 0.0 && double.IsNegativeInfinity(1.0 / value));
        string activePicture;
        if (isNegative && subPictures.Length > 1)
        {
            activePicture = subPictures[1];
            value = Math.Abs(value);
            if (originalDecimal.HasValue) originalDecimal = Math.Abs(originalDecimal.Value);
        }
        else if (isNegative)
        {
            activePicture = subPictures[0];
            value = Math.Abs(value);
            if (originalDecimal.HasValue) originalDecimal = Math.Abs(originalDecimal.Value);
        }
        else
        {
            activePicture = subPictures[0];
        }

        var prefix2 = GetPrefix(activePicture, df);
        var suffix2 = GetSuffix(activePicture, df);
        var body = GetBody(activePicture, df);

        // Check for percent/per-mille in the prefix or suffix
        // Track BigInteger for overflow cases (e.g., decimal.MaxValue * 100)
        BigInteger? overflowBigInt = null;
        if (prefix2.Contains(df.Percent) || suffix2.Contains(df.Percent))
        {
            value *= 100;
            if (originalDecimal.HasValue)
            {
                try { originalDecimal = originalDecimal.Value * 100m; }
                catch (OverflowException)
                {
                    // Decimal overflowed — use BigInteger for full precision
                    overflowBigInt = (BigInteger)originalDecimal.Value * 100;
                    originalDecimal = null;
                }
            }
        }
        else if (prefix2.Contains(df.PerMille) || suffix2.Contains(df.PerMille))
        {
            value *= 1000;
            if (originalDecimal.HasValue)
            {
                try { originalDecimal = originalDecimal.Value * 1000m; }
                catch (OverflowException)
                {
                    overflowBigInt = (BigInteger)originalDecimal.Value * 1000;
                    originalDecimal = null;
                }
            }
        }

        // Check for exponent separator in body — try each occurrence from left to right
        // to find a valid exponent part (all zero-digits after separator)
        for (int expSearch = 0; expSearch < body.Length; expSearch++)
        {
            int expSepPos = body.IndexOf(df.ExponentSeparator, expSearch);
            if (expSepPos < 0) break;

            var mantissaPart = body[..expSepPos];
            var exponentPart = body[(expSepPos + 1)..];
            // Validate exponent part (must be zero-digits only and non-empty)
            bool validExponent = exponentPart.Length > 0;
            foreach (var c in exponentPart)
                if (!IsZeroDigit(c, df)) { validExponent = false; break; }

            if (validExponent)
            {
                var result2 = FormatExponent(value, mantissaPart, exponentPart, df, originalDecimal);
                var finalResult = prefix2 + result2 + suffix2;
                if (isNegative && subPictures.Length == 1)
                    finalResult = df.MinusSign + finalResult;
                return DenormalizeNonBmpResult(finalResult, nonBmpDigits, nonBmpZeroCodePoint, nonBmpDecimalSep, nonBmpGroupingSep);
            }
            expSearch = expSepPos; // continue searching after this position
        }

        // Parse integer and fractional parts of the body
        var decSepPos = body.IndexOf(df.DecimalSeparator);
        string intPart, fracPart;
        if (decSepPos >= 0)
        {
            intPart = body[..decSepPos];
            fracPart = body[(decSepPos + 1)..];
        }
        else
        {
            intPart = body;
            fracPart = "";
        }

        // Analyze integer part: min/max digits and grouping positions
        int intMinDigits = 0, intMaxDigits = 0;
        var intGroupPositions = new List<int>(); // positions (digit count from right) where separators appear
        int intPos = 0;
        for (int i = intPart.Length - 1; i >= 0; i--)
        {
            char c = intPart[i];
            if (IsZeroDigit(c, df)) { intMinDigits++; intMaxDigits++; intPos++; }
            else if (c == df.Digit) { intMaxDigits++; intPos++; }
            else if (c == df.GroupingSeparator) { intGroupPositions.Add(intPos); }
        }

        // Analyze fractional part (including grouping separators)
        int fracMinDigits = 0, fracMaxDigits = 0;
        var fracGroupPositions = new List<int>();
        int fracPos = 0;
        foreach (var c in fracPart)
        {
            if (IsZeroDigit(c, df)) { fracMinDigits++; fracMaxDigits++; fracPos++; }
            else if (c == df.Digit) { fracMaxDigits++; fracPos++; }
            else if (c == df.GroupingSeparator) { fracGroupPositions.Add(fracPos); }
        }

        // Format the number using decimal arithmetic for precision
        // When we have a BigInteger from percent/per-mille overflow, use it directly
        string formatted;
        if (overflowBigInt.HasValue)
        {
            formatted = FormatBigInteger(overflowBigInt.Value, intMinDigits, fracMinDigits, fracMaxDigits,
                intGroupPositions, df, intMaxDigits, fracGroupPositions);
        }
        else
        {
            formatted = FormatDecimal(value, intMinDigits, fracMinDigits, fracMaxDigits,
                intGroupPositions, df, intMaxDigits, fracGroupPositions,
                originalDecimal: originalDecimal);
        }

        var result = prefix2 + formatted + suffix2;
        if (isNegative && subPictures.Length == 1)
            result = df.MinusSign + result;
        return DenormalizeNonBmpResult(result, nonBmpDigits, nonBmpZeroCodePoint, nonBmpDecimalSep, nonBmpGroupingSep);
    }

    private static string FormatExponent(double value, string mantissaPart, string exponentPart,
        Analysis.DecimalFormatProperties df, decimal? originalDecimal = null)
    {
        // Parse mantissa pattern
        var decSepPos = mantissaPart.IndexOf(df.DecimalSeparator);
        string mantIntPart, mantFracPart;
        if (decSepPos >= 0)
        {
            mantIntPart = mantissaPart[..decSepPos];
            mantFracPart = mantissaPart[(decSepPos + 1)..];
        }
        else
        {
            mantIntPart = mantissaPart;
            mantFracPart = "";
        }

        // Count integer digits in mantissa pattern
        int mantIntMinDigits = 0, mantIntMaxDigits = 0;
        foreach (var c in mantIntPart)
        {
            if (IsZeroDigit(c, df)) { mantIntMinDigits++; mantIntMaxDigits++; }
            else if (c == df.Digit) { mantIntMaxDigits++; }
        }

        int mantFracMinDigits = 0, mantFracMaxDigits = 0;
        foreach (var c in mantFracPart)
        {
            if (IsZeroDigit(c, df)) { mantFracMinDigits++; mantFracMaxDigits++; }
            else if (c == df.Digit) { mantFracMaxDigits++; }
        }

        int expMinDigits = exponentPart.Length;

        // Calculate exponent to normalize the mantissa.
        // Per spec §4.7.3: the scaling factor is the minimum-integer-part-size.
        int scalingFactor = mantIntMinDigits;

        // When scaling factor is 0 and the pattern has no explicit fractional digits,
        // integer digit positions shift to become fractional (mantissa is in [0, 1)).
        // When there ARE explicit fractional digits, they govern the fractional display.
        int effFracMin = mantFracMinDigits;
        int effFracMax = mantFracMaxDigits;
        int effIntMin = mantIntMinDigits;
        if (scalingFactor == 0 && mantIntMaxDigits > 0 && value != 0
            && mantFracMaxDigits == 0)
        {
            effFracMax += mantIntMaxDigits;
        }

        int exponent = 0;
        if (value != 0)
        {
            exponent = (int)Math.Floor(Math.Log10(Math.Abs(value)));
            exponent -= (scalingFactor - 1);
        }

        // Use decimal precision for mantissa when available
        decimal? decMantissa = null;
        double mantissa = value / Math.Pow(10, exponent);
        if (originalDecimal.HasValue && originalDecimal.Value != 0m)
        {
            try
            {
                // Compute mantissa in decimal: divide by 10^exponent
                decimal decDivisor = DecimalPow10(exponent);
                decMantissa = originalDecimal.Value / decDivisor;
            }
            catch { /* fall back to double */ }
        }

        // Round mantissa to required fractional digits
        if (decMantissa.HasValue)
        {
            if (effFracMax >= 0 && effFracMax <= 28)
                decMantissa = Math.Round(decMantissa.Value, effFracMax, MidpointRounding.AwayFromZero);
        }
        else if (effFracMax < 20)
        {
            var mult = Math.Pow(10, effFracMax);
            mantissa = Math.Round(mantissa * mult, MidpointRounding.AwayFromZero) / mult;
        }

        // Per spec: do NOT adjust exponent after rounding causes mantissa overflow.
        // E.g., format-number(0.99999999, '0.0e0') → 10.0e-1 (mantissa overflows to 10.0).

        // Format mantissa — show "0" in integer part only if pattern has integer digit positions
        int displayIntMin = mantIntMaxDigits > 0 ? Math.Max(effIntMin, 1) : effIntMin;
        // When mantissa overflows and pattern has decimal with frac digits and
        // intMax < actual integer digits, ensure at least 1 fractional digit is shown.
        int displayFracMin = effFracMin;
        double mantissaForCheck = decMantissa.HasValue ? (double)decMantissa.Value : mantissa;
        if (value != 0 && mantFracMaxDigits > 0)
        {
            int actualIntDigits = mantissaForCheck == 0 ? 1 : (int)Math.Floor(Math.Log10(Math.Abs(mantissaForCheck))) + 1;
            if (actualIntDigits > mantIntMaxDigits && displayFracMin < 1)
                displayFracMin = 1;
        }
        var formatted = FormatDecimal(decMantissa.HasValue ? (double)decMantissa.Value : mantissa,
            displayIntMin, displayFracMin, effFracMax, [], df,
            isExponentMantissa: mantIntMaxDigits > 0, originalDecimal: decMantissa);

        // Format exponent
        var expStr = Math.Abs(exponent).ToString(CultureInfo.InvariantCulture);
        while (expStr.Length < expMinDigits)
            expStr = "0" + expStr;

        // Replace digits with zero-digit family if needed
        if (df.ZeroDigitCodePoint != '0')
            expStr = ReplaceDigits(expStr, df);

        var expSign = exponent < 0 ? df.MinusSign.ToString() : "";
        return formatted + df.ExponentSeparator + expSign + expStr;
    }

    /// <summary>
    /// Compute 10^exponent as a decimal value. Supports negative exponents.
    /// </summary>
    private static decimal DecimalPow10(int exponent)
    {
        if (exponent == 0) return 1m;
        decimal result = 1m;
        if (exponent > 0)
        {
            for (int i = 0; i < exponent; i++)
                result *= 10m;
        }
        else
        {
            for (int i = 0; i < -exponent; i++)
                result /= 10m;
        }
        return result;
    }

    private static string[] SplitSubPictures(string picture, char separator)
    {
        var parts = new List<string>();
        int start = 0;
        for (int i = 0; i < picture.Length; i++)
        {
            if (picture[i] == separator)
            {
                parts.Add(picture[start..i]);
                start = i + 1;
            }
        }
        parts.Add(picture[start..]);
        return parts.ToArray();
    }

    private static bool IsZeroDigit(char c, Analysis.DecimalFormatProperties df)
    {
        return c >= df.ZeroDigit && c < (char)(df.ZeroDigit + 10);
    }

    private static string GetPrefix(string subPicture, Analysis.DecimalFormatProperties df)
    {
        for (int i = 0; i < subPicture.Length; i++)
        {
            char c = subPicture[i];
            if (IsZeroDigit(c, df) || c == df.Digit || c == df.DecimalSeparator
                || c == df.GroupingSeparator)
                return subPicture[..i];
        }
        return subPicture;
    }

    private static string GetSuffix(string subPicture, Analysis.DecimalFormatProperties df)
    {
        for (int i = subPicture.Length - 1; i >= 0; i--)
        {
            char c = subPicture[i];
            if (IsZeroDigit(c, df) || c == df.Digit || c == df.DecimalSeparator
                || c == df.GroupingSeparator)
                return subPicture[(i + 1)..];
        }
        return "";
    }

    private static string GetBody(string subPicture, Analysis.DecimalFormatProperties df)
    {
        // The body extends from the first active character to the last active character.
        // Active characters are: zero-digit, optional-digit, decimal-separator,
        // grouping-separator, and exponent-separator (only if followed by zero-digits).
        int start = -1, end = -1;
        for (int i = 0; i < subPicture.Length; i++)
        {
            char c = subPicture[i];
            bool isActive = IsZeroDigit(c, df) || c == df.Digit || c == df.DecimalSeparator
                || c == df.GroupingSeparator;
            // Exponent separator is active only if followed by at least one zero-digit
            if (!isActive && c == df.ExponentSeparator && i + 1 < subPicture.Length
                && IsZeroDigit(subPicture[i + 1], df))
                isActive = true;
            if (isActive)
            {
                if (start < 0) start = i;
                end = i;
            }
        }
        if (start < 0) return "";
        return subPicture[start..(end + 1)];
    }

    private static string FormatDecimal(double value, int intMinDigits, int fracMinDigits,
        int fracMaxDigits, List<int> intGroupPositions, Analysis.DecimalFormatProperties df,
        int intPatternDigitCount = 0, List<int>? fracGroupPositions = null,
        bool isExponentMantissa = false, decimal? originalDecimal = null)
    {
        // Use decimal for better precision when possible; fall back to double for large values
        bool useDecimal = true;
        decimal decValue;
        if (originalDecimal.HasValue)
        {
            // Use the original decimal directly — preserves full precision
            decValue = originalDecimal.Value;
        }
        else
        {
            try { decValue = (decimal)value; }
            catch { decValue = 0m; useDecimal = false; }
        }

        bool allowEmptyInteger = (intMinDigits == 0);

        string intStr, fracStr;
        if (useDecimal)
        {
            // Round to fracMaxDigits
            if (fracMaxDigits >= 0 && fracMaxDigits <= 28)
                decValue = Math.Round(decValue, fracMaxDigits, MidpointRounding.AwayFromZero);

            // Convert to string with fixed point
            if (fracMaxDigits > 0)
            {
                var formatted = decValue.ToString($"F{fracMaxDigits}", CultureInfo.InvariantCulture);
                var dotPos = formatted.IndexOf('.');
                if (dotPos >= 0)
                {
                    intStr = formatted[..dotPos];
                    fracStr = formatted[(dotPos + 1)..];
                }
                else
                {
                    intStr = formatted;
                    fracStr = "";
                }
            }
            else
            {
                intStr = Math.Round(decValue, MidpointRounding.AwayFromZero).ToString("F0", CultureInfo.InvariantCulture);
                fracStr = "";
            }
        }
        else
        {
            // Large values that don't fit in decimal — use double formatting
            // Format with enough precision, then split
            if (fracMaxDigits > 0)
            {
                var rounded = Math.Round(value, fracMaxDigits, MidpointRounding.AwayFromZero);
                var formatted = rounded.ToString($"F{fracMaxDigits}", CultureInfo.InvariantCulture);
                var dotPos = formatted.IndexOf('.');
                if (dotPos >= 0)
                {
                    intStr = formatted[..dotPos];
                    fracStr = formatted[(dotPos + 1)..];
                }
                else
                {
                    intStr = formatted;
                    fracStr = "";
                }
            }
            else
            {
                // For very large numbers, use "R" (round-trip) to get all significant digits,
                // then pad with zeros if needed
                var s = value.ToString("R", CultureInfo.InvariantCulture);
                // Handle scientific notation (e.g., "1E+30")
                if (s.Contains('E') || s.Contains('e'))
                {
                    // Parse the scientific notation and expand to full integer
                    var parts = s.Split(['E', 'e']);
                    var mantissa = parts[0].Replace(".", "");
                    var exp = int.Parse(parts[1]);
                    var dotInMantissa = parts[0].IndexOf('.');
                    var significandDigits = dotInMantissa >= 0 ? mantissa.Length : mantissa.Length;
                    var intDigits = dotInMantissa >= 0 ? dotInMantissa : mantissa.Length;
                    var totalIntDigits = intDigits + exp;
                    if (totalIntDigits > significandDigits)
                        intStr = mantissa + new string('0', totalIntDigits - significandDigits);
                    else
                        intStr = mantissa[..totalIntDigits];
                }
                else
                {
                    intStr = s.Contains('.') ? s[..s.IndexOf('.')] : s;
                }
                fracStr = "";
            }
        }

        if (intStr.StartsWith('-'))
            intStr = intStr[1..];

        bool isZeroValue = (value == 0.0);

        // Pad/trim integer part
        // In non-exponent mode: suppress "0" integer when intMinDigits=0 for any value
        // (e.g., "#.#" with 0.2 → ".2", "#.#" with 0 → ".0")
        // In exponent mode: keep "0" for non-zero mantissa (e.g., "#.#e0" with 0.2 → "0.2e0")
        // but suppress for zero value (e.g., "#.#e0" with 0 → "0e0" via normal path)
        if (allowEmptyInteger && intStr == "0" && (!isExponentMantissa || isZeroValue))
            intStr = "";
        else
            while (intStr.Length < intMinDigits)
                intStr = "0" + intStr;

        // Trim trailing zeros in fractional part
        // Special case: when value is zero and fracMaxDigits > 0, the spec says
        // "the fractional part will contain a single instance of the zero-digit character"
        // so keep at least 1 fractional digit for zero values
        int effectiveFracMin = isZeroValue && fracMaxDigits > 0 && !isExponentMantissa
            ? Math.Max(fracMinDigits, 1) : fracMinDigits;
        while (fracStr.Length > effectiveFracMin && fracStr.EndsWith('0'))
            fracStr = fracStr[..^1];
        while (fracStr.Length < effectiveFracMin)
            fracStr += "0";

        // Apply grouping separators
        if (intGroupPositions.Count > 0 && intStr.Length > 0)
        {
            var sb = new StringBuilder();
            int digitCount = 0;
            for (int i = intStr.Length - 1; i >= 0; i--)
            {
                if (digitCount > 0 && ShouldInsertGroupSeparator(digitCount, intGroupPositions, intPatternDigitCount))
                    sb.Insert(0, df.GroupingSeparator);
                sb.Insert(0, intStr[i]);
                digitCount++;
            }
            intStr = sb.ToString();
        }

        // Apply fractional grouping separators
        if (fracGroupPositions is { Count: > 0 } && fracStr.Length > 0)
        {
            var sb = new StringBuilder();
            int digitCount = 0;
            for (int i = 0; i < fracStr.Length; i++)
            {
                if (digitCount > 0 && fracGroupPositions.Contains(digitCount))
                    sb.Append(df.GroupingSeparator);
                sb.Append(fracStr[i]);
                digitCount++;
            }
            fracStr = sb.ToString();
        }

        // Replace digits with zero-digit family if non-default
        if (df.ZeroDigitCodePoint != '0')
        {
            intStr = ReplaceDigits(intStr, df);
            if (fracStr.Length > 0)
                fracStr = ReplaceDigits(fracStr, df);
        }

        if (fracStr.Length > 0)
            return intStr + df.DecimalSeparator + fracStr;
        // When integer part has content, return it
        if (intStr.Length > 0)
            return intStr;
        // No integer digits to show — but if no fractional part either, must show "0"
        return "0";
    }

    /// <summary>
    /// Format a BigInteger value (used when decimal overflows, e.g., decimal.MaxValue * 100 for percent).
    /// </summary>
    private static string FormatBigInteger(BigInteger bigValue, int intMinDigits, int fracMinDigits,
        int fracMaxDigits, List<int> intGroupPositions, Analysis.DecimalFormatProperties df,
        int intPatternDigitCount = 0, List<int>? fracGroupPositions = null)
    {
        var intStr = BigInteger.Abs(bigValue).ToString(CultureInfo.InvariantCulture);

        // Pad integer part to minimum digits
        while (intStr.Length < intMinDigits)
            intStr = "0" + intStr;

        // Apply grouping separators
        if (intGroupPositions.Count > 0 && intStr.Length > 0)
        {
            var sb = new StringBuilder();
            int digitCount = 0;
            for (int i = intStr.Length - 1; i >= 0; i--)
            {
                if (digitCount > 0 && ShouldInsertGroupSeparator(digitCount, intGroupPositions, intPatternDigitCount))
                    sb.Insert(0, df.GroupingSeparator);
                sb.Insert(0, intStr[i]);
                digitCount++;
            }
            intStr = sb.ToString();
        }

        // Replace digits with zero-digit family if non-default
        if (df.ZeroDigitCodePoint != '0')
            intStr = ReplaceDigits(intStr, df);

        // Handle fractional part (always empty for BigInteger, but pad if required)
        string fracStr = "";
        while (fracStr.Length < fracMinDigits)
            fracStr += "0";

        if (fracStr.Length > 0)
            return intStr + df.DecimalSeparator + fracStr;
        return intStr;
    }

    private static bool ShouldInsertGroupSeparator(int digitCount, List<int> groupPositions,
        int patternDigitCount)
    {
        // groupPositions are absolute positions from the right where separators appear
        // patternDigitCount is the total number of digit positions in the integer pattern
        if (groupPositions.Count == 0) return false;

        // Check explicit separator positions first
        for (int i = 0; i < groupPositions.Count; i++)
        {
            if (digitCount == groupPositions[i]) return true;
        }

        // Per XPath 4.0 spec: grouping repeats beyond explicit positions only if:
        // 1. All separator positions form a regular sequence s, 2s, 3s, ... for some s > 0
        // 2. The number of digit symbols to the LEFT of the leftmost separator is LESS THAN s
        int primaryGroup = groupPositions[0]; // smallest position = rightmost separator
        if (primaryGroup <= 0) return false;

        // Check regularity: all positions must be at multiples of primaryGroup
        bool isRegular = true;
        for (int i = 0; i < groupPositions.Count; i++)
        {
            if (groupPositions[i] != primaryGroup * (i + 1))
            {
                isRegular = false;
                break;
            }
        }
        if (!isRegular) return false;

        // Check condition 2: digits to the left of leftmost separator must be < primaryGroup
        int leftmostSepPos = groupPositions[^1];
        int digitsLeftOfSep = patternDigitCount - leftmostSepPos;
        if (digitsLeftOfSep > primaryGroup) return false;

        // Regular repeating grouping
        return digitCount > 0 && digitCount % primaryGroup == 0;
    }

    private static string ReplaceDigits(string s, Analysis.DecimalFormatProperties df)
    {
        int zeroCodePoint = df.ZeroDigitCodePoint;
        if (zeroCodePoint <= 0xFFFF)
        {
            // BMP digit replacement (original fast path)
            var offset = (char)zeroCodePoint - '0';
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                if (c >= '0' && c <= '9')
                    sb.Append((char)(c + offset));
                else
                    sb.Append(c);
            }
            return sb.ToString();
        }
        else
        {
            // Non-BMP digit replacement — output surrogate pairs
            var sb = new StringBuilder(s.Length * 2);
            foreach (var c in s)
            {
                if (c >= '0' && c <= '9')
                {
                    int cp = zeroCodePoint + (c - '0');
                    sb.Append(char.ConvertFromUtf32(cp));
                }
                else
                    sb.Append(c);
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Normalize a picture string by replacing non-BMP zero-digit family (10 digits starting
    /// at <paramref name="zeroCodePoint"/>) with BMP '0'-'9' for processing.
    /// </summary>
    private static string NormalizePictureNonBmp(string picture, int zeroCodePoint)
    {
        var sb = new StringBuilder(picture.Length);
        var enumerator = System.Globalization.StringInfo.GetTextElementEnumerator(picture);
        while (enumerator.MoveNext())
        {
            string element = enumerator.GetTextElement();
            if (element.Length >= 2 && char.IsHighSurrogate(element[0]))
            {
                int cp = char.ConvertToUtf32(element[0], element[1]);
                int offset = cp - zeroCodePoint;
                if (offset >= 0 && offset <= 9)
                {
                    sb.Append((char)('0' + offset));
                    continue;
                }
            }
            sb.Append(element);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Denormalize a result string by replacing BMP '0'-'9' with non-BMP digits starting at
    /// <paramref name="zeroCodePoint"/>.
    /// </summary>
    private static string DenormalizeResultNonBmp(string result, int zeroCodePoint)
    {
        var sb = new StringBuilder(result.Length * 2);
        foreach (var c in result)
        {
            if (c >= '0' && c <= '9')
                sb.Append(char.ConvertFromUtf32(zeroCodePoint + (c - '0')));
            else
                sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Denormalize a result by replacing BMP placeholders back with non-BMP characters
    /// (digits, decimal separator, grouping separator).
    /// </summary>
    private static string DenormalizeNonBmpResult(string result, bool nonBmpDigits, int nonBmpZeroCodePoint,
        string? nonBmpDecimalSep, string? nonBmpGroupingSep)
    {
        if (!nonBmpDigits && nonBmpDecimalSep == null && nonBmpGroupingSep == null)
            return result;

        if (nonBmpDigits)
            result = DenormalizeResultNonBmp(result, nonBmpZeroCodePoint);

        // Replace BMP separator placeholders with non-BMP originals
        if (nonBmpDecimalSep != null)
            result = result.Replace(".", nonBmpDecimalSep);
        if (nonBmpGroupingSep != null)
            result = result.Replace(",", nonBmpGroupingSep);

        return result;
    }
}

/// <summary>
/// fn:format-number($value, $picture, $decimal-format-name) as xs:string
/// </summary>
public sealed class FormatNumber3Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "format-number");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "value"), Type = new XdmSequenceType { ItemType = ItemType.Double, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "picture"), Type = XdmSequenceType.String },
        new() { Name = new QName(NamespaceId.None, "decimal-format-name"), Type = XdmSequenceType.OptionalString }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        // Handle empty sequence (null, empty array, empty list) as default format
        var rawArg = arguments.Count > 2 ? arguments[2] : null;
        if (rawArg is object?[] { Length: 0 } or List<object?> { Count: 0 }) rawArg = null;
        var formatName = rawArg?.ToString()?.Trim();
        // Resolve the format name: accept plain NCName, Q{uri}local, or prefixed "ex:name"
        // where ex is a statically known prefix.
        string? resolvedName = formatName;
        if (!string.IsNullOrEmpty(formatName) && context.DecimalFormats != null)
        {
            // Try exact match first
            if (!context.DecimalFormats.ContainsKey(formatName))
            {
                // Try prefix resolution: "prefix:local" → "Q{uri}local"
                var colonIdx = formatName.IndexOf(':');
                if (colonIdx > 0 && colonIdx < formatName.Length - 1 && !formatName.StartsWith("Q{", StringComparison.Ordinal))
                {
                    var prefix = formatName[..colonIdx];
                    var local = formatName[(colonIdx + 1)..];
                    if (context is Execution.QueryExecutionContext qec
                        && qec.PrefixNamespaceBindings != null
                        && qec.PrefixNamespaceBindings.TryGetValue(prefix, out var uri))
                    {
                        var expanded = $"Q{{{uri}}}{local}";
                        if (context.DecimalFormats.ContainsKey(expanded))
                            resolvedName = expanded;
                    }
                }
            }
        }
        // FODF1280: if a non-null format name is supplied but no matching format exists,
        // raise an error. (A missing third arg or empty string uses the default format.)
        if (!string.IsNullOrEmpty(resolvedName))
        {
            if (context.DecimalFormats == null || !context.DecimalFormats.ContainsKey(resolvedName))
                throw new XQueryRuntimeException("FODF1280",
                    $"Decimal format '{formatName}' is not defined in the static context");
        }
        var df = FormatNumberFunction.GetDecimalFormat(context, resolvedName);
        var result = FormatNumberFunction.FormatNumberImpl(arguments[0], arguments[1]?.ToString() ?? "", df);
        return ValueTask.FromResult<object?>(result);
    }
}

/// <summary>
/// Shared formatting logic for format-date, format-dateTime, and format-time.
/// Parses XSLT picture strings like "[Y0001]-[M01]-[D01]" and formats date/time components.
/// </summary>
internal static class DateTimeFormatter
{
    /// <summary>
    /// Atomizes a function argument to an optional string, correctly handling empty sequences
    /// (which Atomize returns as object[]) and single-item sequences.
    /// </summary>
    internal static string? AtomizeToOptionalString(object? argument)
    {
        var atomized = Execution.QueryExecutionContext.Atomize(argument);
        if (atomized is null) return null;
        if (atomized is object[] arr)
            return arr.Length == 0 ? null : arr[0]?.ToString();
        if (atomized is Array genArr)
            return genArr.Length == 0 ? null : genArr.GetValue(0)?.ToString();
        return atomized.ToString();
    }

    private static readonly string[] MonthNames =
        ["January", "February", "March", "April", "May", "June",
         "July", "August", "September", "October", "November", "December"];

    private static readonly string[] DayNames =
        ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

    private static readonly Dictionary<string, string[]> LocalizedMonthNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["de"] = ["Januar", "Februar", "März", "April", "Mai", "Juni",
                  "Juli", "August", "September", "Oktober", "November", "Dezember"],
        ["fr"] = ["janvier", "février", "mars", "avril", "mai", "juin",
                  "juillet", "août", "septembre", "octobre", "novembre", "décembre"],
    };

    private static readonly Dictionary<string, string[]> LocalizedDayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["de"] = ["Montag", "Dienstag", "Mittwoch", "Donnerstag", "Freitag", "Samstag", "Sonntag"],
        ["fr"] = ["lundi", "mardi", "mercredi", "jeudi", "vendredi", "samedi", "dimanche"],
    };

    public static string Format(DateTimeOffset dt, string picture, bool hasDate, bool hasTime, string? language = null, string? calendar = null, long? extendedYear = null, bool hasTimezone = true, string? place = null)
    {
        // If place is a timezone ID, convert the dateTime to that timezone
        TimeZoneInfo? resolvedTz = null;
        if (place != null)
        {
            try
            {
                resolvedTz = TimeZoneInfo.FindSystemTimeZoneById(place);
                if (hasTimezone)
                    dt = TimeZoneInfo.ConvertTime(dt, resolvedTz);
            }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        // Validate calendar parameter
        if (calendar != null && calendar.Length > 0)
        {
            var cal = calendar.Trim();
            // Reject unknown calendars: only allow standard calendars
            // Per spec, unknown calendar in no namespace → FOFD1340
            // Known calendars: AD, AH, AM, AO, AP, AS, BE, CB, CE, CL, CS, EE, FE, ISO, JE, KE, KY, ME, MS, NS, OS, RE, SE, SH, SS, TE, VE, VS
            var knownCalendars = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "AD", "AH", "AM", "AO", "AP", "AS", "BE", "CB", "CE", "CL", "CS",
                "EE", "FE", "ISO", "JE", "KE", "KY", "ME", "MS", "NS", "OS", "RE",
                "SE", "SH", "SS", "TE", "VE", "VS"
            };

            // Parse EQName format: Q{uri}local or just local
            string localName;
            string? namespaceUri = null;
            if (cal.StartsWith("Q{", StringComparison.Ordinal))
            {
                var closeBrace = cal.IndexOf('}', 2);
                if (closeBrace < 0)
                    throw new XQueryException("FOFD1340", $"Invalid calendar name: {calendar}");
                namespaceUri = cal[2..closeBrace];
                localName = cal[(closeBrace + 1)..];
            }
            else
            {
                localName = cal;
            }

            // Handle prefixed QName format (e.g. "cal:CB" where cal is a namespace prefix)
            // We can't resolve the prefix here, but the presence of a prefix implies
            // it's in a namespace, so treat it as a namespaced calendar (implementation-defined, fallback to Gregorian)
            if (namespaceUri == null && localName.Contains(':'))
            {
                var colonIdx = localName.IndexOf(':');
                var prefix = localName[..colonIdx];
                localName = localName[(colonIdx + 1)..];
                // Empty prefix (e.g. ":w") or empty local name is invalid
                if (prefix.Length == 0 || localName.Length == 0)
                    throw new XQueryException("FOFD1340", $"Invalid calendar name: {calendar}");
                // Treat as namespaced (non-standard) calendar — fall through to Gregorian fallback
                namespaceUri = $"urn:calendar-prefix:{prefix}"; // synthetic namespace to indicate it's prefixed
            }

            // Validate local name is a valid NCName (basic check)
            if (localName.Length == 0 || char.IsDigit(localName[0]))
                throw new XQueryException("FOFD1340", $"Invalid calendar name: {calendar}");

            // Unknown calendar in no namespace (or empty namespace) → FOFD1340
            if ((namespaceUri == null || namespaceUri.Length == 0) && !knownCalendars.Contains(localName))
                throw new XQueryException("FOFD1340", $"Unknown calendar: {calendar}");
        }

        // Determine effective language
        string effectiveLanguage = "en";
        string? languageMarker = null;
        if (language != null)
        {
            var baseLang = language.Contains('-') ? language[..language.IndexOf('-')] : language;
            if (baseLang == "en" || LocalizedMonthNames.ContainsKey(baseLang))
            {
                effectiveLanguage = baseLang;
            }
            else
            {
                // Unsupported language: fall back to English with marker
                languageMarker = "[Language: en]";
            }
        }

        var sb = new System.Text.StringBuilder();
        if (languageMarker != null) sb.Append(languageMarker);
        var i = 0;
        while (i < picture.Length)
        {
            if (picture[i] == '[' && i + 1 < picture.Length && picture[i + 1] == '[')
            {
                // Escaped literal [
                sb.Append('[');
                i += 2;
            }
            else if (picture[i] == '[')
            {
                var end = picture.IndexOf(']', i + 1);
                if (end < 0) { sb.Append(picture[i..]); break; }
                var spec = picture[(i + 1)..end].Trim();
                sb.Append(FormatComponent(dt, spec, hasDate, hasTime, extendedYear, hasTimezone, effectiveLanguage, resolvedTz));
                i = end + 1;
            }
            else if (picture[i] == ']' && i + 1 < picture.Length && picture[i + 1] == ']')
            {
                sb.Append(']');
                i += 2;
            }
            else
            {
                sb.Append(picture[i]);
                i++;
            }
        }
        return sb.ToString();
    }

    private static readonly HashSet<char> DateComponents = ['Y', 'M', 'D', 'd', 'F', 'W', 'w', 'E'];
    private static readonly HashSet<char> TimeComponents = ['H', 'h', 'm', 's', 'f', 'P'];
    private static readonly HashSet<char> AllComponents = ['Y', 'M', 'D', 'd', 'F', 'W', 'w', 'H', 'h', 'm', 's', 'f', 'P', 'Z', 'z', 'E', 'C'];

    /// <summary>Checks if a string is a valid width specifier: digits, '-', and '*' only.</summary>
    private static bool IsValidWidthSpec(string s)
    {
        if (s.Length == 0) return false;
        // Valid patterns: "N", "N-M", "N-*", "*-N", "*"
        foreach (var c in s)
        {
            if (c != '-' && c != '*' && !char.IsAsciiDigit(c)) return false;
        }
        return true;
    }

    private static string FormatComponent(DateTimeOffset dt, string spec, bool hasDate, bool hasTime, long? extendedYear = null, bool hasTimezone = true, string effectiveLanguage = "en", TimeZoneInfo? resolvedTz = null)
    {
        // Per XSLT spec §9.8.4.1: whitespace within the variable marker is removed
        spec = System.Text.RegularExpressions.Regex.Replace(spec, @"\s+", "");
        if (spec.Length == 0) return "";

        var component = spec[0];
        var presentation = spec.Length > 1 ? spec[1..] : "";

        // Apply default presentation modifiers per spec when none specified
        // Default is "1" for most numeric components. Spec recommends min-width 2 for m and s.
        if (presentation.Length == 0 || (presentation.Length > 0 && presentation[0] == ','))
        {
            var defaultPres = component switch
            {
                'F' => "Nn",  // day-of-week defaults to name
                'm' => "01",  // minute defaults to 2-digit (spec: "should be 2")
                's' => "01",  // second defaults to 2-digit (spec: "should be 2")
                _ => ""
            };
            if (defaultPres.Length > 0)
            {
                if (presentation.Length > 0 && presentation[0] == ',')
                    presentation = defaultPres + presentation;
                else
                    presentation = defaultPres;
            }
        }

        // XTDE1340: Invalid component letter
        if (!AllComponents.Contains(component))
            throw new XQueryException("XTDE1340", $"Invalid component '{component}' in date/time picture string");

        // XTDE1350: Component not available in value type
        if (DateComponents.Contains(component) && !hasDate)
            throw new XQueryException("XTDE1350", $"Date component '{component}' is not available in a time value");
        if (TimeComponents.Contains(component) && !hasTime)
            throw new XQueryException("XTDE1350", $"Time component '{component}' is not available in a date value");

        // Parse optional width constraint ,min-max
        // Width modifier follows the LAST comma where the part after it is a valid width pattern
        // (digits, '-', '*'). This allows commas to be used as grouping separators in the presentation.
        int? minWidth = null;
        int? maxWidth = null;
        var widthIdx = -1;
        for (var ci = presentation.Length - 1; ci >= 0; ci--)
        {
            if (presentation[ci] == ',')
            {
                var candidate = presentation[(ci + 1)..];
                if (IsValidWidthSpec(candidate))
                {
                    widthIdx = ci;
                    break;
                }
            }
        }
        if (widthIdx >= 0)
        {
            var widthSpec = presentation[(widthIdx + 1)..];
            presentation = presentation[..widthIdx];
            var dashIdx = widthSpec.IndexOf('-', StringComparison.Ordinal);
            if (dashIdx >= 0)
            {
                if (dashIdx > 0 && int.TryParse(widthSpec[..dashIdx], NumberStyles.Integer, CultureInfo.InvariantCulture, out var mn))
                    minWidth = mn;
                var afterDash = widthSpec[(dashIdx + 1)..];
                if (afterDash.Length > 0 && afterDash != "*" && int.TryParse(afterDash, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mx))
                    maxWidth = mx;
            }
            else if (widthSpec == "*")
            {
                // Just "*" means unbounded max, no min constraint
            }
            else if (int.TryParse(widthSpec, NumberStyles.Integer, CultureInfo.InvariantCulture, out var w))
            {
                minWidth = w;
                maxWidth = w;
            }
        }

        // FOFD1340: min-width > max-width is invalid
        if (minWidth.HasValue && maxWidth.HasValue && minWidth.Value > maxWidth.Value)
            throw new XQueryException("FOFD1340", $"Invalid width specifier: minimum width ({minWidth.Value}) exceeds maximum width ({maxWidth.Value})");

        // Validate presentation for numeric components: optional digit (#) must not follow mandatory digit
        // This applies to date/time component presentations (not format-integer which has its own validation)
        if (component is 'Y' or 'M' or 'D' or 'd' or 'H' or 'h' or 'm' or 's' or 'W' or 'w' or 'f')
        {
            ValidatePresentationDigits(presentation, component);
        }

        return component switch
        {
            'Y' => FormatYear(extendedYear.HasValue ? (int)Math.Abs(extendedYear.Value) : dt.Year, presentation, minWidth, maxWidth),
            'M' => FormatMonth(dt.Month, presentation, minWidth, maxWidth, effectiveLanguage),
            'D' => FormatNumber(dt.Day, presentation, minWidth, maxWidth),
            'd' => FormatNumber(dt.DayOfYear, presentation, minWidth, maxWidth),
            'F' => FormatDayOfWeek(dt.DayOfWeek, presentation, minWidth, maxWidth, effectiveLanguage),
            'W' => FormatNumber(ISOWeekOfYear(dt), presentation, minWidth, maxWidth),
            'w' => FormatNumber(GetWeekOfMonth(dt), presentation, minWidth, maxWidth),
            'H' => FormatNumber(dt.Hour, presentation, minWidth, maxWidth),
            'h' => FormatNumber(dt.Hour == 0 ? 12 : dt.Hour > 12 ? dt.Hour - 12 : dt.Hour, presentation, minWidth, maxWidth),
            'm' => FormatNumber(dt.Minute, presentation, minWidth, maxWidth),
            's' => FormatNumber(dt.Second, presentation, minWidth, maxWidth),
            'f' => FormatFractionalSeconds(dt, presentation, minWidth, maxWidth),
            'P' => FormatAmPm(dt.Hour, presentation, minWidth, maxWidth),
            'Z' or 'z' => FormatTimezone(dt, presentation, component, minWidth, maxWidth, hasTimezone, resolvedTz),
            'E' => FormatEra(extendedYear.HasValue ? (int)extendedYear.Value : dt.Year, presentation),
            'C' => "ISO", // calendar
            _ => $"[{spec}]"
        };
    }

    /// <summary>Validates decimal-digit presentation patterns and digit family consistency.
    /// For regular components: optional-digit-sign* mandatory-digit-sign+ (# before digits).
    /// For fractional seconds (f): mandatory-digit-sign+ optional-digit-sign* (0 before 9/#).
    /// Also validates no mixed digit families and at least one mandatory digit for f.</summary>
    private static void ValidatePresentationDigits(string presentation, char component)
    {
        if (presentation.Length == 0) return;
        // Skip non-digit presentations (N, n, W, w, I, i, a, A etc.)
        // Also handle non-BMP digits by checking via Rune
        if (presentation.Length > 0)
        {
            var firstRune = Rune.GetRuneAt(presentation, 0);
            if (!Rune.IsDigit(firstRune) && presentation[0] != '#') return;
        }

        // Check for mixed digit families (using Rune enumeration for non-BMP digit support)
        int? firstZeroCodepoint = null;
        bool seenMandatory = false;
        bool seenOptional = false;
        bool seenHash = false;
        bool orderingViolation = false;
        int mandatoryCount = 0;

        // Collect runes for lookahead
        var runes = new List<Rune>();
        foreach (var r in presentation.EnumerateRunes()) runes.Add(r);

        for (int ri = 0; ri < runes.Count; ri++)
        {
            var rune = runes[ri];
            bool isOptional;
            bool isMandatory;

            if (rune.Value == '#')
            {
                isOptional = true;
                isMandatory = false;
            }
            else if (Rune.IsDigit(rune))
            {
                var numVal = (int)Rune.GetNumericValue(rune);
                var zeroChar = rune.Value - numVal;
                if (firstZeroCodepoint == null)
                    firstZeroCodepoint = zeroChar;
                else if (zeroChar != firstZeroCodepoint.Value)
                    throw new XQueryException("FOFD1340", $"Mixed digit families in presentation: '{presentation}'");

                if (component == 'f')
                {
                    // In fractional seconds: 9 is optional, all other digits (0-8) are mandatory
                    isOptional = numVal == 9;
                    isMandatory = numVal != 9;
                }
                else
                {
                    // In regular components: all digits are mandatory
                    isOptional = false;
                    isMandatory = true;
                }
            }
            else
            {
                // Non-digit, non-# character: could be grouping separator, skip
                // But stop if it's not between digits
                bool moreDigits = false;
                for (int j = ri + 1; j < runes.Count; j++)
                {
                    if (Rune.IsDigit(runes[j]) || runes[j].Value == '#') { moreDigits = true; break; }
                }
                if (!moreDigits) break; // trailing non-digit, stop validation
                continue;
            }

            if (isMandatory)
            {
                mandatoryCount++;
                if (component == 'f')
                {
                    // For fractional seconds: mandatory after optional is invalid
                    if (seenOptional) orderingViolation = true;
                }
                seenMandatory = true;
            }
            if (isOptional)
            {
                if (component == 'f')
                {
                    // For fractional seconds: # after 9 is OK, but 9 after # is invalid
                    // Valid ordering: mandatory (0-8)... then 9... then #...
                    if (rune.Value != '#' && seenHash) orderingViolation = true; // 9 after #
                    if (rune.Value == '#') seenHash = true;
                }
                else
                {
                    // For regular components: optional after mandatory is invalid
                    if (seenMandatory) orderingViolation = true;
                }
                seenOptional = true;
            }
        }

        if (orderingViolation)
        {
            if (component == 'f')
                throw new XQueryException("FOFD1340", $"Invalid presentation: mandatory digit after optional digit in '{presentation}'");
            else
                throw new XQueryException("FOFD1340", $"Invalid presentation: optional digit (#) after mandatory digit in '{presentation}'");
        }

        // For fractional seconds: patterns with only 9 and/or # digits are valid
        // (9 = show digit position but trim trailing zeros, # = omit if not significant)
    }

    private static string FormatYear(int year, string presentation, int? minWidth, int? maxWidth)
    {
        // Parse ordinal flag and width from presentation
        var ordinal = false;
        var pres = presentation;
        if (pres.EndsWith('o'))
        {
            ordinal = true;
            pres = pres[..^1];
        }

        // Roman numeral formatting — width modifiers apply at the numeric level (truncate year digits)
        if (pres is "I" or "i")
        {
            var yearVal = Math.Abs(year);
            // maxWidth truncates the year to at most N digits before converting to roman
            if (maxWidth.HasValue)
            {
                var yearStr = yearVal.ToString(CultureInfo.InvariantCulture);
                if (yearStr.Length > maxWidth.Value)
                {
                    yearStr = yearStr[^maxWidth.Value..];
                    yearVal = int.Parse(yearStr, CultureInfo.InvariantCulture);
                }
            }
            var roman = pres == "I" ? ToRoman(yearVal) : ToRoman(yearVal).ToLowerInvariant();
            // Pad to minWidth with trailing spaces
            if (minWidth.HasValue && roman.Length < minWidth.Value)
                roman = roman.PadRight(minWidth.Value);
            return roman;
        }

        // Word formatting
        if (pres.Length > 0 && (pres[0] == 'W' || pres[0] == 'w'))
            return FormatWord(Math.Abs(year), pres, ordinal);

        // Detect non-ASCII zero digit (supports non-BMP digits via Rune)
        int zeroDigitRune = '0';
        bool isNonBmpDigit = false;
        if (pres.Length > 0)
        {
            var firstRune = Rune.GetRuneAt(pres, 0);
            if (firstRune.Value != '#' && Rune.IsDigit(firstRune) && firstRune.Value > 127)
            {
                var numVal = (int)Rune.GetNumericValue(firstRune);
                zeroDigitRune = firstRune.Value - numVal;
                isNonBmpDigit = !firstRune.IsBmp;
            }
        }

        // Parse grouping separators and count digit characters (including '#' as optional digit)
        // E.g. "9;999" = digits [9,9,9,9] with separators [';'@pos1], "#.0" = 2 digit positions with '.' separator
        // Use Rune enumeration to correctly handle non-BMP (surrogate pair) digits
        var padDigits = 0;
        var mandatoryDigits = 0;
        var totalPatternDigits = 0;
        var groupSeparators = new List<(int digitPos, char sep)>(); // separator positions relative to digit count from left
        {
            // Collect all runes for lookahead capability
            var presRunes = new List<Rune>();
            foreach (var r in pres.EnumerateRunes()) presRunes.Add(r);
            for (var ri = 0; ri < presRunes.Count; ri++)
            {
                var rune = presRunes[ri];
                var c = rune.IsBmp ? (char)rune.Value : '\0';
                if (Rune.IsDigit(rune) || c == '#')
                {
                    padDigits++;
                    totalPatternDigits++;
                    if (c != '#' && !(isNonBmpDigit && !rune.IsBmp && rune.Value == zeroDigitRune)) mandatoryDigits++;
                }
                else if (padDigits > 0 && ri < presRunes.Count - 1) // non-digit between digits = grouping separator
                {
                    // Check if more digits/# follow
                    bool moreDigits = false;
                    for (int j = ri + 1; j < presRunes.Count; j++)
                        if (Rune.IsDigit(presRunes[j]) || (presRunes[j].IsBmp && (char)presRunes[j].Value == '#')) { moreDigits = true; break; }
                    if (moreDigits)
                        groupSeparators.Add((padDigits, c)); // position = digits seen so far
                    else
                        break;
                }
                else
                    break;
            }
        }
        bool padFromPresentation = padDigits > 0;
        if (padDigits == 0) padDigits = minWidth ?? 1; // default presentation is "1" (minimum 1 digit)

        // Use the larger of mandatory digits (from presentation) and minWidth (from width specifier)
        var effectivePad = mandatoryDigits > 0 ? mandatoryDigits : (minWidth ?? 1);
        if (minWidth.HasValue) effectivePad = Math.Max(effectivePad, minWidth.Value);
        var result = Math.Abs(year).ToString(CultureInfo.InvariantCulture);
        // Truncate to maxWidth from the right (keep last N digits)
        if (maxWidth.HasValue && result.Length > maxWidth.Value)
            result = result[^maxWidth.Value..];
        // Per F&O spec: for year, multi-digit presentations (e.g. "01", "#.0", "001", "#0 00 0")
        // truncate the year to that many digits. Single-digit "1" means "at least 1 digit" (no truncation).
        else if (padFromPresentation && totalPatternDigits >= 2 && !maxWidth.HasValue && result.Length > totalPatternDigits)
            result = result[^totalPatternDigits..];

        result = result.PadLeft(effectivePad, '0');

        // Insert grouping separators (convert positions from left-to-right in pattern to right-to-left in result)
        if (groupSeparators.Count > 0)
            result = InsertGroupingSeparators(result, groupSeparators, totalPatternDigits);

        // Replace ASCII digits with target digit family (supports non-BMP via Rune)
        if (zeroDigitRune != '0')
        {
            var sb = new StringBuilder(result.Length * 2);
            foreach (var ch in result)
            {
                if (ch >= '0' && ch <= '9')
                {
                    var targetRune = new Rune(zeroDigitRune + (ch - '0'));
                    sb.Append(targetRune.ToString());
                }
                else
                    sb.Append(ch);
            }
            result = sb.ToString();
        }

        if (ordinal) result += GetOrdinalSuffix(year);
        return result;
    }

    private static string FormatMonth(int month, string presentation, int? minWidth, int? maxWidth, string language = "en")
    {
        // N/n = name, default = number
        if (presentation.Length > 0 && (presentation[0] == 'N' || presentation[0] == 'n'))
        {
            string name;
            if (language != "en" && LocalizedMonthNames.TryGetValue(language, out var localizedNames))
                name = localizedNames[month - 1];
            else
                name = MonthNames[month - 1];
            if (maxWidth.HasValue && maxWidth.Value < name.Length)
                name = name[..maxWidth.Value];
            return ApplyCase(name, presentation);
        }
        // Roman numeral
        if (presentation == "I") return ToRoman(month);
        if (presentation == "i") return ToRoman(month).ToLowerInvariant();
        // Word formatting
        if (presentation.Length > 0 && (presentation[0] == 'W' || presentation[0] == 'w'))
        {
            var ordinal = presentation.EndsWith('o');
            return FormatWord(month, presentation, ordinal);
        }
        return FormatNumber(month, presentation, minWidth, maxWidth);
    }

    private static string FormatDayOfWeek(DayOfWeek dow, string presentation, int? minWidth, int? maxWidth, string language = "en")
    {
        // Map .NET DayOfWeek (Sunday=0) to ISO (Monday=0)
        var isoIdx = dow == DayOfWeek.Sunday ? 6 : (int)dow - 1;
        if (presentation.Length > 0 && (presentation[0] == 'N' || presentation[0] == 'n'))
        {
            string name;
            if (language != "en" && LocalizedDayNames.TryGetValue(language, out var localizedNames))
                name = localizedNames[isoIdx];
            else
                name = DayNames[isoIdx];
            // When both min and max width specified, truncate to minWidth (conventional abbreviation)
            if (minWidth.HasValue && maxWidth.HasValue && name.Length > minWidth.Value)
                name = name[..minWidth.Value];
            else if (maxWidth.HasValue && name.Length > maxWidth.Value)
                name = name[..maxWidth.Value];
            return ApplyCase(name, presentation);
        }
        return FormatNumber(isoIdx + 1, presentation); // 1-based
    }

    private static string FormatNumber(int value, string presentation, int? minWidth = null, int? maxWidth = null)
    {
        if (presentation.Length == 0 && !minWidth.HasValue && !maxWidth.HasValue)
            return value.ToString(CultureInfo.InvariantCulture);
        if (presentation.Length == 0) presentation = "1"; // default numeric presentation

        // Word formatting (W/w/Ww with optional ordinal 'o' suffix)
        if (presentation.Length > 0 && (presentation[0] == 'W' || presentation[0] == 'w'))
        {
            var isOrd = presentation.EndsWith('o');
            var wordPres = isOrd ? presentation[..^1] : presentation;
            return FormatWord(value, wordPres, isOrd);
        }
        // Roman numeral formatting
        if (presentation == "I") return ToRoman(value);
        if (presentation == "i") return ToRoman(value).ToLowerInvariant();
        // Alphabetic formatting
        if (presentation == "a" || presentation == "ao") return ToAlpha(value, upper: false);
        if (presentation == "A" || presentation == "Ao") return ToAlpha(value, upper: true);

        // Check for ordinal suffix
        var ordinal = false;
        var pres = presentation;
        if (pres.EndsWith('o'))
        {
            ordinal = true;
            pres = pres[..^1];
        }

        // Roman numeral
        if (pres == "I") return ToRoman(value);
        if (pres == "i") return ToRoman(value).ToLowerInvariant();

        // Word formatting
        if (pres.Length > 0 && (pres[0] == 'W' || pres[0] == 'w'))
            return FormatWord(value, pres, ordinal);

        // Alphabetic numbering: A=1,B=2,...Z=26,AA=27...
        if (pres == "A" || pres == "a")
        {
            var upper = pres == "A";
            return ToAlpha(value, upper);
        }

        // Detect non-ASCII zero digit (supports non-BMP digits via Rune)
        int zeroDigitRune = '0';
        if (pres.Length > 0)
        {
            var firstRune = Rune.GetRuneAt(pres, 0);
            if (Rune.IsDigit(firstRune) && firstRune.Value > 127)
            {
                var numVal = (int)Rune.GetNumericValue(firstRune);
                zeroDigitRune = firstRune.Value - numVal;
            }
        }

        // Count padding digits (using Rune enumeration for non-BMP support)
        var padDigits = 0;
        foreach (var rune in pres.EnumerateRunes())
        {
            if (Rune.IsDigit(rune)) padDigits++;
            else break;
        }
        if (padDigits == 0) padDigits = 1;

        // Use the larger of padDigits (from presentation) and minWidth (from width specifier)
        var effectivePad = minWidth.HasValue ? Math.Max(padDigits, minWidth.Value) : padDigits;
        var result = value.ToString(CultureInfo.InvariantCulture).PadLeft(effectivePad, '0');

        // Truncate to maxWidth from the right (keep last N digits)
        if (maxWidth.HasValue && result.Length > maxWidth.Value)
            result = result[^maxWidth.Value..];

        // Replace ASCII digits with the target digit family (supports non-BMP via Rune)
        if (zeroDigitRune != '0')
        {
            var sb = new StringBuilder(result.Length * 2);
            foreach (var ch in result)
            {
                if (ch >= '0' && ch <= '9')
                {
                    var targetRune = new Rune(zeroDigitRune + (ch - '0'));
                    sb.Append(targetRune.ToString());
                }
                else
                    sb.Append(ch);
            }
            result = sb.ToString();
        }

        if (ordinal)
            result += GetOrdinalSuffix(value);

        return result;
    }

    private static string ToAlpha(int value, bool upper)
    {
        if (value <= 0) return value.ToString(CultureInfo.InvariantCulture);
        var sb = new System.Text.StringBuilder();
        var v = value;
        while (v > 0)
        {
            v--; // make 0-based
            sb.Insert(0, (char)((upper ? 'A' : 'a') + v % 26));
            v /= 26;
        }
        return sb.ToString();
    }

    private static string GetOrdinalSuffix(int value)
    {
        var abs = Math.Abs(value);
        var lastTwo = abs % 100;
        if (lastTwo >= 11 && lastTwo <= 13) return "th";
        return (abs % 10) switch
        {
            1 => "st",
            2 => "nd",
            3 => "rd",
            _ => "th"
        };
    }

    private static string FormatFractionalSeconds(DateTimeOffset dt, string presentation, int? minWidth = null, int? maxWidth = null)
    {
        // Build fractional second string from ticks (7 decimal places of precision)
        var ticks = dt.Ticks % TimeSpan.TicksPerSecond; // 0-9999999
        var fracStr = ticks.ToString("D7", CultureInfo.InvariantCulture); // always 7 digits

        // Parse presentation to determine digit counts and grouping separators:
        // 0 or any non-9/non-# digit = mandatory (determines min digits)
        // 9 or # = optional (adds to max digits)
        // [f] or [f1] with no explicit counts → show all significant digits
        // Non-digit characters between digits are grouping separators
        int mandatoryDigits = 0;
        int optionalDigits = 0;
        char zeroDigit = '0';
        var groupingSeps = new List<(int digitPos, char sep)>(); // grouping separator positions
        int digitCount = 0;

        for (int pi = 0; pi < presentation.Length; pi++)
        {
            var c = presentation[pi];
            if (c == '#')
            {
                optionalDigits++;
                digitCount++;
            }
            else if (char.IsDigit(c))
            {
                var numVal = (int)char.GetNumericValue(c);
                var thisZero = (char)(c - numVal);
                if (digitCount == 0 && thisZero != '0')
                    zeroDigit = thisZero;

                if (numVal == 9)
                    optionalDigits++;
                else
                    mandatoryDigits++;
                digitCount++;
            }
            else
            {
                // Non-digit: check if more digits follow (grouping separator) or end
                bool moreDigits = false;
                for (int j = pi + 1; j < presentation.Length; j++)
                    if (char.IsDigit(presentation[j]) || presentation[j] == '#') { moreDigits = true; break; }
                if (moreDigits)
                    groupingSeps.Add((digitCount, c));
                else
                    break;
            }
        }
        var totalPresDigits = mandatoryDigits + optionalDigits;

        // Determine min/max output digits
        // Per XPath spec (bug 29788): for [f], the presentation picture takes precedence
        // over the width modifier when a multi-digit picture is specified
        int min, max;
        if (minWidth.HasValue || maxWidth.HasValue)
        {
            // FOFD1340: explicit minWidth=0 is invalid for fractional seconds
            if (minWidth.HasValue && minWidth.Value <= 0)
                throw new XQueryRuntimeException("FOFD1340", "Minimum width for fractional seconds must be greater than 0");
            // FOFD1340: explicit maxWidth=0 is invalid for fractional seconds
            if (maxWidth.HasValue && maxWidth.Value <= 0)
                throw new XQueryRuntimeException("FOFD1340", "Maximum width for fractional seconds must be greater than 0");
            // Per XPath spec (bug 29788): for [f], the picture's mandatory digits
            // set the minimum floor that width modifier cannot reduce below
            var picMin = mandatoryDigits > 0 ? mandatoryDigits : 1;
            var picMax = totalPresDigits > 0 ? totalPresDigits : 7;
            min = minWidth.HasValue ? Math.Max(minWidth.Value, picMin) : picMin;
            max = maxWidth.HasValue ? Math.Max(maxWidth.Value, picMin) : picMax;
        }
        else if (totalPresDigits == 0 || (totalPresDigits == 1 && mandatoryDigits <= 1 && optionalDigits == 0))
        {
            // Default: [f] or [f1] — show all significant digits
            min = 1;
            max = 7;
        }
        else
        {
            // Presentation determines digit range
            min = mandatoryDigits > 0 ? mandatoryDigits : 0;
            max = totalPresDigits;
        }
        if (min < 1) min = 1; // always show at least 1 digit

        // Truncate to max digits
        if (max > 7) fracStr = fracStr.PadRight(max, '0');
        else if (max < 7) fracStr = fracStr[..max];

        // Remove trailing zeros but keep at least min digits
        while (fracStr.Length > min && fracStr[^1] == '0')
            fracStr = fracStr[..^1];

        // Insert grouping separators (positions are from the LEFT for fractional seconds)
        if (groupingSeps.Count > 0)
        {
            var sb = new System.Text.StringBuilder();
            int dIdx = 0;
            for (int ci = 0; ci < fracStr.Length; ci++)
            {
                sb.Append(fracStr[ci]);
                dIdx++;
                foreach (var (pos, sep) in groupingSeps)
                {
                    if (pos == dIdx && ci < fracStr.Length - 1)
                    {
                        sb.Append(sep);
                        break;
                    }
                }
            }
            fracStr = sb.ToString();
        }

        // Replace ASCII digits with target digit family
        if (zeroDigit != '0')
        {
            var chars = fracStr.ToCharArray();
            for (var ci = 0; ci < chars.Length; ci++)
            {
                if (chars[ci] >= '0' && chars[ci] <= '9')
                    chars[ci] = (char)(zeroDigit + (chars[ci] - '0'));
            }
            fracStr = new string(chars);
        }

        return fracStr;
    }

    private static string FormatEra(int year, string presentation)
    {
        var era = year > 0 ? "AD" : "BC";
        if (presentation.Length > 0 && (presentation[0] == 'N' || presentation[0] == 'n'))
            return ApplyCase(era, presentation);
        return era;
    }

    private static string FormatAmPm(int hour, string presentation, int? minWidth, int? maxWidth)
    {
        var amPm = hour < 12 ? "am" : "pm";

        // Apply case from presentation modifier
        if (presentation == "N")
            amPm = amPm.ToUpperInvariant(); // UPPERCASE
        else if (presentation == "Nn")
            amPm = char.ToUpperInvariant(amPm[0]) + amPm[1..]; // Title case
        // else: "n" or empty → lowercase (already lowercase)

        // Apply width constraint: truncate to maxWidth if specified
        if (maxWidth.HasValue && maxWidth.Value < amPm.Length)
            amPm = amPm[..maxWidth.Value];

        return amPm;
    }

    private static string FormatTimezone(DateTimeOffset dt, string presentation, char component, int? minWidth = null, int? maxWidth = null, bool hasTimezone = true, TimeZoneInfo? resolvedTz = null)
    {
        // ZZ = military timezone letter codes
        if (component == 'Z' && presentation == "Z")
            return FormatMilitaryTimezone(dt, hasTimezone);

        // ZN or Zn = timezone name (e.g. "EST", "GMT-05:00")
        // Try to resolve a well-known timezone abbreviation; fall back to ±HH:MM format
        if (component == 'Z' && presentation.Length > 0 && (presentation[0] == 'N' || presentation[0] == 'n'))
        {
            if (!hasTimezone)
                return "";
            var off = dt.Offset;
            if (off == TimeSpan.Zero)
                return ApplyCase("GMT", presentation);
            // If we have a resolved timezone (from place parameter), use its names directly
            if (resolvedTz != null)
            {
                var isDst = resolvedTz.IsDaylightSavingTime(dt);
                var tzName = isDst ? resolvedTz.DaylightName : resolvedTz.StandardName;
                // On Linux, IANA tz names may return the full ID; use our abbreviation table instead
                if (tzName.Length <= 5 && !tzName.Contains('/'))
                    return ApplyCase(tzName, presentation);
                // Fall through to abbreviation lookup
            }
            var abbrev = GetTimezoneAbbreviation(dt, off);
            if (abbrev != null)
                return ApplyCase(abbrev, presentation);
            // Fallback: plain offset without GMT prefix (accepted by W3C tests)
            var absOff = off < TimeSpan.Zero ? -off : off;
            var signOff = off < TimeSpan.Zero ? "-" : "+";
            return ApplyCase($"{signOff}{absOff.Hours:D2}:{absOff.Minutes:D2}", presentation);
        }

        var offset = dt.Offset;
        var abs = offset < TimeSpan.Zero ? -offset : offset;
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var hours = abs.Hours;
        var minutes = abs.Minutes;

        // Component 'z': GMT-prefix notation
        if (component == 'z')
        {
            // Parse the presentation pattern to determine hour/minute formatting
            // z0 = minimal hours, drop :00 minutes
            // z00 = two-digit hours, drop :00 minutes
            // z00:00 or z (default) = full format GMT+HH:MM
            // Custom separator replaces ':'
            var pres = presentation;
            char? separator = null;
            int hourDigits = 2; // default: zero-padded hours
            bool alwaysShowMinutes = true; // default: always show minutes

            if (pres.Length == 0)
            {
                // Default: GMT+HH:MM
                hourDigits = 2;
                alwaysShowMinutes = true;
                separator = ':';
            }
            else
            {
                // Count leading digit placeholders for hours
                var digitCount = 0;
                var i = 0;
                while (i < pres.Length && (pres[i] == '0' || pres[i] == '9' || char.IsDigit(pres[i])))
                {
                    digitCount++;
                    i++;
                }
                hourDigits = digitCount > 0 ? digitCount : 1;

                // Check for separator and minute part
                if (i < pres.Length && !char.IsDigit(pres[i]) && pres[i] != '0' && pres[i] != '9')
                {
                    separator = pres[i];
                    i++;
                    // Count minute digits
                    var minDigits = 0;
                    while (i < pres.Length && (pres[i] == '0' || pres[i] == '9' || char.IsDigit(pres[i])))
                    {
                        minDigits++;
                        i++;
                    }
                    alwaysShowMinutes = minDigits > 0;
                }
                else if (i >= pres.Length)
                {
                    // No separator, no minutes part: omit minutes when zero
                    alwaysShowMinutes = false;
                    separator = ':'; // fallback separator for non-zero minutes
                }
            }

            var hStr = hourDigits >= 2 ? hours.ToString("D2", CultureInfo.InvariantCulture) : hours.ToString(CultureInfo.InvariantCulture);
            var sep = separator ?? ':';
            if (alwaysShowMinutes || minutes != 0)
                return $"GMT{sign}{hStr}{sep}{minutes:D2}";
            return $"GMT{sign}{hStr}";
        }

        // Z component: numeric format
        // Check for 't' suffix which means use Z for UTC
        var useTforZero = presentation.EndsWith('t');
        var zpres = useTforZero ? presentation[..^1] : presentation;

        if (offset == TimeSpan.Zero && useTforZero)
            return "Z";

        // Parse the numeric timezone picture pattern
        // The pattern uses 0 (mandatory digit) and 9 (optional digit) with optional separator
        // Default [Z] = +HH:MM
        if (zpres.Length == 0)
        {
            // Default: +HH:MM
            return $"{sign}{hours:D2}:{minutes:D2}";
        }

        // Find separator character (non-digit, non-0, non-9)
        char? zSep = null;
        int zSepIdx = -1;
        var hourMandatory = 0;
        var hourOptional = 0;
        var minMandatory = 0;
        var minOptional = 0;
        // Also detect the zero-digit family (may be supplementary codepoint)
        int zeroDigitCp = '0';

        var phase = 0; // 0=hours, 1=separator found→minutes
        for (int i = 0; i < zpres.Length; )
        {
            int cp;
            int charCount;
            if (char.IsHighSurrogate(zpres[i]) && i + 1 < zpres.Length && char.IsLowSurrogate(zpres[i + 1]))
            {
                cp = char.ConvertToUtf32(zpres[i], zpres[i + 1]);
                charCount = 2;
            }
            else
            {
                cp = zpres[i];
                charCount = 1;
            }

            double numericValue;
            if (cp >= 0x10000)
                numericValue = char.GetNumericValue(new string(new[] { zpres[i], zpres[i + 1] }), 0);
            else
                numericValue = char.GetNumericValue((char)cp);
            bool isDigit = numericValue >= 0 && numericValue <= 9;

            if (cp == '0' || (isDigit && numericValue == 0))
            {
                if (cp != '0') zeroDigitCp = cp;
                if (phase == 0) hourMandatory++;
                else minMandatory++;
            }
            else if (cp == '9' || (isDigit && numericValue == 9))
            {
                if (phase == 0) hourOptional++;
                else minOptional++;
            }
            else if (isDigit)
            {
                // Other digit — treat as mandatory, and infer zero digit
                int digitVal = (int)numericValue;
                if (zeroDigitCp == '0') zeroDigitCp = cp - digitVal;
                if (phase == 0) hourMandatory++;
                else minMandatory++;
            }
            else
            {
                // Separator character
                zSep = zpres[i];
                zSepIdx = i;
                phase = 1;
            }
            i += charCount;
        }

        // Determine formatting
        var totalHourDigits = hourMandatory + hourOptional;
        var totalMinDigits = minMandatory + minOptional;
        var hasExplicitSeparator = phase == 1;
        var hasMinutePart = hasExplicitSeparator || totalMinDigits > 0;

        string result;

        if (!hasExplicitSeparator && totalHourDigits >= 3)
        {
            // No separator, 3+ digits: concatenated HHMM format
            // E.g. [Z999] → +030, +1000, +000
            // E.g. [Z9999] → +0530, +1000, +0000
            var concatenated = hours * 100 + minutes;
            var concStr = concatenated.ToString(CultureInfo.InvariantCulture);
            // Pad to total digit count (both mandatory and optional specify the field width for timezone)
            var minDigits = Math.Max(totalHourDigits, 1);
            if (concStr.Length < minDigits)
                concStr = concStr.PadLeft(minDigits, '0');
            result = $"{sign}{concStr}";
        }
        else if (hasExplicitSeparator)
        {
            // Has explicit separator between hours and minutes
            var hResult = hours.ToString(CultureInfo.InvariantCulture);
            // For timezone, total digit positions (mandatory + optional) determine field width
            if (totalHourDigits >= 2 || (hourMandatory == 0 && hourOptional == 0))
                hResult = hours.ToString("D2", CultureInfo.InvariantCulture);
            else if (totalHourDigits == 1)
                hResult = hours.ToString(CultureInfo.InvariantCulture);
            var mResult = minutes.ToString("D2", CultureInfo.InvariantCulture);
            var sepChar = zSep ?? ':';
            result = $"{sign}{hResult}{sepChar}{mResult}";
        }
        else
        {
            // No separator, 1-2 hour digits: hours only, minutes shown with ':' when non-zero
            var hResult = hours.ToString(CultureInfo.InvariantCulture);
            // For timezone, total digit positions determine field width
            if (totalHourDigits >= 2 || (hourMandatory == 0 && hourOptional == 0))
                hResult = hours.ToString("D2", CultureInfo.InvariantCulture);
            else if (totalHourDigits == 1)
                hResult = hours.ToString(CultureInfo.InvariantCulture);
            var mResult = minutes.ToString("D2", CultureInfo.InvariantCulture);

            if (minutes != 0)
                result = $"{sign}{hResult}:{mResult}";
            else
                result = $"{sign}{hResult}";
        }

        // Replace ASCII digits with target digit family (may be supplementary codepoints)
        if (zeroDigitCp != '0')
        {
            var sb = new System.Text.StringBuilder(result.Length);
            foreach (var c in result)
            {
                if (c >= '0' && c <= '9')
                {
                    int targetCp = zeroDigitCp + (c - '0');
                    if (targetCp > 0xFFFF)
                        sb.Append(char.ConvertFromUtf32(targetCp));
                    else
                        sb.Append((char)targetCp);
                }
                else
                    sb.Append(c);
            }
            result = sb.ToString();
        }

        return result;
    }

    /// <summary>Military timezone letters: A-M (skip J) for UTC+1 to +12, N-Y for UTC-1 to -12, Z for UTC, J for local (no timezone).</summary>
    private static string FormatMilitaryTimezone(DateTimeOffset dt, bool hasTimezone = true)
    {
        // J = no timezone information available
        if (!hasTimezone)
            return "J";

        var offset = dt.Offset;
        var totalMinutes = (int)offset.TotalMinutes;

        // Z = UTC (offset 0)
        if (totalMinutes == 0)
            return "Z";

        // Only whole-hour offsets in range get military letters
        if (totalMinutes % 60 != 0)
        {
            // Non-integral hour: fall back to numeric ±HH:MM
            var abs = offset < TimeSpan.Zero ? -offset : offset;
            var sign = offset < TimeSpan.Zero ? "-" : "+";
            return $"{sign}{abs.Hours:D2}:{abs.Minutes:D2}";
        }

        var totalHours = totalMinutes / 60;

        // Out of range -12..+12: fall back to numeric
        if (totalHours < -12 || totalHours > 12)
        {
            var abs = offset < TimeSpan.Zero ? -offset : offset;
            var sign = offset < TimeSpan.Zero ? "-" : "+";
            return $"{sign}{abs.Hours:D2}:{abs.Minutes:D2}";
        }

        if (totalHours > 0)
        {
            // A=+1, B=+2, ..., I=+9, K=+10, L=+11, M=+12 (skip J)
            var letter = totalHours <= 9 ? (char)('A' + totalHours - 1) : (char)('A' + totalHours); // skip J
            return letter.ToString();
        }
        else
        {
            // N=-1, O=-2, ..., Y=-12
            var letter = (char)('N' + (-totalHours) - 1);
            return letter.ToString();
        }
    }

    private static int ISOWeekOfYear(DateTimeOffset dt)
    {
        // ISO 8601 week number: week 1 contains the first Thursday of the year
        var day = dt.DateTime;
        var dayOfYear = day.DayOfYear;
        var dayOfWeek = (int)day.DayOfWeek; // Sunday=0
        // Convert to ISO: Monday=1, Sunday=7
        var isoDow = dayOfWeek == 0 ? 7 : dayOfWeek;
        // Find the Thursday of this week
        var thursday = day.AddDays(4 - isoDow);
        var jan1 = new DateTime(thursday.Year, 1, 1);
        return (thursday.DayOfYear - 1) / 7 + 1;
    }

    private static int GetWeekOfMonth(DateTimeOffset dt)
    {
        // ISO week-of-month: week 1 contains the first Thursday of the month
        // If the date falls before week 1 of its month, it belongs to the last week of the previous month
        var week = GetWeekOfMonthRaw(dt.Year, dt.Month, dt.Day, dt.DayOfWeek);
        if (week < 1)
        {
            // Date is before week 1 of its month — belongs to last week of previous month
            var prevMonth = dt.DateTime.AddDays(-dt.Day); // last day of previous month
            return GetWeekOfMonthRaw(prevMonth.Year, prevMonth.Month, prevMonth.Day, prevMonth.DayOfWeek);
        }
        return week;
    }

    private static int GetWeekOfMonthRaw(int year, int month, int day, DayOfWeek dayOfWeek)
    {
        var first = new DateTime(year, month, 1);
        var firstDow = first.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)first.DayOfWeek;
        // Days from first to first Thursday (ISO day 4)
        var daysToThursday = (4 - firstDow + 7) % 7;
        var firstThursday = first.AddDays(daysToThursday);
        // Monday of that week = start of week 1
        var week1Start = firstThursday.AddDays(-3);
        // Monday of the date's week
        var dateDow = dayOfWeek == DayOfWeek.Sunday ? 7 : (int)dayOfWeek;
        var dateMonday = new DateTime(year, month, day).AddDays(1 - dateDow);
        // Week difference
        return (int)Math.Round((dateMonday - week1Start).TotalDays / 7.0) + 1;
    }

    // Well-known timezone abbreviations keyed by UTC offset in minutes.
    // Standard and daylight abbreviations for the most common timezone at each offset.
    private static readonly Dictionary<int, (string Standard, string Daylight)> WellKnownTimezones = new()
    {
        [-720] = ("BIT", "BIT"),     // Baker Island
        [-660] = ("SST", "SST"),     // Samoa Standard Time
        [-600] = ("HST", "HST"),     // Hawaii (no DST)
        [-540] = ("AKST", "AKDT"),   // Alaska
        [-480] = ("PST", "PDT"),     // Pacific
        [-420] = ("MST", "MDT"),     // Mountain
        [-360] = ("CST", "CDT"),     // Central
        [-300] = ("EST", "EDT"),     // Eastern
        [-240] = ("AST", "ADT"),     // Atlantic
        [-210] = ("NST", "NDT"),     // Newfoundland
        [-180] = ("BRT", "BRST"),    // Brasilia
        [-120] = ("GST", "GST"),     // South Georgia
        [-60]  = ("CVT", "CVT"),     // Cape Verde
        [60]   = ("CET", "CEST"),    // Central European
        [120]  = ("EET", "EEST"),    // Eastern European
        [180]  = ("MSK", "MSD"),     // Moscow
        [210]  = ("IRST", "IRDT"),   // Iran
        [240]  = ("GST", "GST"),     // Gulf
        [270]  = ("AFT", "AFT"),     // Afghanistan
        [300]  = ("PKT", "PKST"),    // Pakistan
        [330]  = ("IST", "IST"),     // India
        [345]  = ("NPT", "NPT"),     // Nepal
        [360]  = ("BST", "BST"),     // Bangladesh
        [390]  = ("MMT", "MMT"),     // Myanmar
        [420]  = ("ICT", "ICT"),     // Indochina
        [480]  = ("CST", "CDT"),     // China
        [540]  = ("JST", "JST"),     // Japan (no DST)
        [570]  = ("ACST", "ACDT"),   // Australian Central
        [600]  = ("AEST", "AEDT"),   // Australian Eastern
        [660]  = ("SBT", "SBT"),     // Solomon Islands
        [720]  = ("NZST", "NZDT"),   // New Zealand
    };

    /// <summary>
    /// Tries to find a timezone abbreviation for the given offset using a curated mapping.
    /// Determines standard vs daylight by checking if a major timezone at this base offset
    /// is currently in DST (which means someone at this fixed offset is actually in the
    /// DST of a timezone one hour behind).
    /// </summary>
    private static string? GetTimezoneAbbreviation(DateTimeOffset dt, TimeSpan offset)
    {
        var offsetMinutes = (int)offset.TotalMinutes;
        if (!WellKnownTimezones.TryGetValue(offsetMinutes, out var names))
            return null;

        // If this timezone doesn't observe DST (same abbreviation), return immediately
        if (names.Standard == names.Daylight)
            return names.Standard;

        // Check if a system timezone whose BASE (standard) offset matches is currently in DST.
        // If it is, then its actual offset is shifted (e.g. EST→EDT = -05:00→-04:00),
        // meaning someone still AT -05:00 is likely in the DST of the zone one hour behind
        // (e.g. CDT = CST+1 = -06:00+1 = -05:00).
        foreach (var tz in TimeZoneInfo.GetSystemTimeZones())
        {
            if (tz.BaseUtcOffset == offset && tz.SupportsDaylightSavingTime && tz.IsDaylightSavingTime(dt))
            {
                // The standard timezone at this offset is in DST (shifted away),
                // so this offset is the DST of the zone one hour behind
                if (WellKnownTimezones.TryGetValue(offsetMinutes - 60, out var behindNames) &&
                    behindNames.Standard != behindNames.Daylight)
                    return behindNames.Daylight;
                break;
            }
        }

        return names.Standard;
    }

    private static string ApplyCase(string name, string presentation)
    {
        if (presentation.Length == 0) return name;
        if (char.IsUpper(presentation[0]))
        {
            // Nn = title case (first upper, rest lower)
            if (presentation.Length >= 2 && char.IsLower(presentation[1]))
                return char.ToUpperInvariant(name[0]) + name[1..].ToLowerInvariant();
            // N or NN = UPPERCASE
            return name.ToUpperInvariant();
        }
        // n or nn = lowercase
        return name.ToLowerInvariant();
    }

    /// <summary>Inserts grouping separators into a digit string based on a pattern.</summary>
    /// <param name="digits">The plain digit string (e.g. "2012")</param>
    /// <param name="seps">Separator positions from the pattern (digitPos from left, sep char)</param>
    /// <param name="patternDigits">Total digit positions in the pattern</param>
    private static string InsertGroupingSeparators(string digits, List<(int digitPos, char sep)> seps, int patternDigits)
    {
        // Convert separator positions to positions from the right
        // Pattern "9;999" has 4 digits, separator at digit position 1 (from left) → 3 from right
        // Pattern "9,99-9" has 4 digits, separators at positions 1 (,) and 3 (-) → from right: 3 (,) and 1 (-)
        var sb = new System.Text.StringBuilder();
        // Build a list of separator chars indexed by position-from-right in the pattern
        var sepFromRight = new List<(int posFromRight, char sep)>();
        foreach (var (digitPos, sep) in seps)
            sepFromRight.Add((patternDigits - digitPos, sep));

        // Walk the digits from right to left, inserting separators
        var digitIdx = digits.Length - 1;
        var posFromRight = 0;
        while (digitIdx >= 0)
        {
            sb.Insert(0, digits[digitIdx]);
            digitIdx--;
            posFromRight++;
            // Check if there's a separator at this position
            foreach (var (pos, sep) in sepFromRight)
            {
                if (pos == posFromRight && digitIdx >= 0)
                {
                    sb.Insert(0, sep);
                    break;
                }
            }
        }
        return sb.ToString();
    }

    private static string ToRoman(int value)
    {
        if (value <= 0) return value.ToString(CultureInfo.InvariantCulture);
        var sb = new System.Text.StringBuilder();
        ReadOnlySpan<(int val, string rom)> table =
        [
            (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"),
            (100, "C"), (90, "XC"), (50, "L"), (40, "XL"),
            (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")
        ];
        var remaining = value;
        foreach (var (val, rom) in table)
        {
            while (remaining >= val)
            {
                sb.Append(rom);
                remaining -= val;
            }
        }
        return sb.ToString();
    }

    private static readonly string[] Ones =
        ["", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine",
         "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen",
         "seventeen", "eighteen", "nineteen"];
    private static readonly string[] Tens =
        ["", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"];

    private static readonly string[] OrdinalOnes =
        ["", "first", "second", "third", "fourth", "fifth", "sixth", "seventh", "eighth", "ninth",
         "tenth", "eleventh", "twelfth", "thirteenth", "fourteenth", "fifteenth", "sixteenth",
         "seventeenth", "eighteenth", "nineteenth"];
    private static readonly string[] OrdinalTens =
        ["", "", "twentieth", "thirtieth", "fortieth", "fiftieth", "sixtieth", "seventieth", "eightieth", "ninetieth"];

    private static string NumberToWords(int value, bool ordinal)
    {
        if (value == 0) return ordinal ? "zeroth" : "zero";
        if (value < 0) return "minus " + NumberToWords(-value, ordinal);

        var result = "";
        if (value >= 1000000)
        {
            result += NumberToWords(value / 1000000, false) + " million";
            value %= 1000000;
            if (value == 0) return ordinal ? result + "th" : result;
            result += value < 100 ? " and " : " ";
        }
        if (value >= 1000)
        {
            result += NumberToWords(value / 1000, false) + " thousand";
            value %= 1000;
            if (value == 0) return ordinal ? result + "th" : result;
            result += value < 100 ? " and " : " ";
        }
        if (value >= 100)
        {
            result += Ones[value / 100] + " hundred";
            value %= 100;
            if (value == 0) return ordinal ? result + "th" : result;
            result += " and ";
        }

        if (value >= 20)
        {
            var t = value / 10;
            var o = value % 10;
            if (o == 0)
                result += ordinal ? OrdinalTens[t] : Tens[t];
            else
                result += Tens[t] + " " + (ordinal ? OrdinalOnes[o] : Ones[o]);
        }
        else if (value > 0)
        {
            result += ordinal ? OrdinalOnes[value] : Ones[value];
        }

        return result;
    }

    private static string FormatWord(int value, string presentation, bool ordinal)
    {
        var words = NumberToWords(value, ordinal);
        // Apply case: W = UPPER, w = lower, Ww = Title
        if (presentation.Length >= 2 && char.IsUpper(presentation[0]) && char.IsLower(presentation[1]))
        {
            // Title case: capitalize first letter of each word except "and"
            var wordParts = words.Split(' ');
            for (var j = 0; j < wordParts.Length; j++)
            {
                if (wordParts[j].Length > 0 && wordParts[j] != "and")
                    wordParts[j] = char.ToUpperInvariant(wordParts[j][0]) + wordParts[j][1..];
            }
            return string.Join(' ', wordParts);
        }
        if (presentation.Length > 0 && char.IsUpper(presentation[0]))
            return words.ToUpperInvariant();
        return words; // lowercase by default
    }

    /// <summary>Creates a DateTimeOffset for a time value, using a safe base date that won't overflow with large timezone offsets.</summary>
    internal static DateTimeOffset SafeTimeOffset(TimeOnly time, TimeSpan offset)
    {
        // Use a date in the middle of the range to avoid overflow with large offsets (e.g. +13:00, -14:00)
        var safeDate = new DateOnly(2000, 1, 1);
        return new DateTimeOffset(safeDate, time, offset);
    }

    internal static DateTimeOffset ExtractDateTimeOffset(Xdm.XsDate xd, out long? extendedYear)
    {
        extendedYear = xd.ExtendedYear;
        return new DateTimeOffset(xd.Date, TimeOnly.MinValue, xd.Timezone ?? TimeSpan.Zero);
    }

    internal static DateTimeOffset ExtractDateTimeOffset(Xdm.XsDateTime xdt, out long? extendedYear)
    {
        extendedYear = xdt.ExtendedYear;
        return xdt.Value;
    }
}

/// <summary>
/// fn:format-date($value, $picture) as xs:string
/// </summary>
public sealed class FormatDateFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "format-date");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "value"), Type = new XdmSequenceType { ItemType = ItemType.Date, Occurrence = Occurrence.ZeroOrOne } },
        new() { Name = new QName(NamespaceId.None, "picture"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = Execution.QueryExecutionContext.Atomize(arguments[0]);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        var picture = arguments[1]?.ToString() ?? "";
        long? extendedYear = null;
        var dt = arg switch
        {
            Xdm.XsDate xd => DateTimeFormatter.ExtractDateTimeOffset(xd, out extendedYear),
            DateOnly d => new DateTimeOffset(d, TimeOnly.MinValue, TimeSpan.Zero),
            Xdm.XsDateTime xdt => DateTimeFormatter.ExtractDateTimeOffset(xdt, out extendedYear),
            DateTimeOffset dto => dto,
            string s => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture),
            _ => throw new XQueryException("XPTY0004", $"Expected xs:date, got {arg.GetType().Name}")
        };
        var hasTimezone = arg is Xdm.XsDate xd2 ? xd2.Timezone.HasValue : true;
        return ValueTask.FromResult<object?>(DateTimeFormatter.Format(dt, picture, hasDate: true, hasTime: false, extendedYear: extendedYear, hasTimezone: hasTimezone));
    }
}

/// <summary>
/// fn:format-date($value, $picture, $language, $calendar, $place) as xs:string
/// </summary>
public sealed class FormatDate5Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "format-date");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "value"), Type = new XdmSequenceType { ItemType = ItemType.Date, Occurrence = Occurrence.ZeroOrOne } },
        new() { Name = new QName(NamespaceId.None, "picture"), Type = XdmSequenceType.String },
        new() { Name = new QName(NamespaceId.None, "language"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "calendar"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "place"), Type = XdmSequenceType.OptionalString }
    ];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = Execution.QueryExecutionContext.Atomize(arguments[0]);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        var picture = arguments[1]?.ToString() ?? "";
        var language = DateTimeFormatter.AtomizeToOptionalString(arguments[2]);
        var calendar = DateTimeFormatter.AtomizeToOptionalString(arguments[3]);
        long? extendedYear = null;
        var dt = arg switch
        {
            Xdm.XsDate xd => DateTimeFormatter.ExtractDateTimeOffset(xd, out extendedYear),
            DateOnly d => new DateTimeOffset(d, TimeOnly.MinValue, TimeSpan.Zero),
            Xdm.XsDateTime xdt => DateTimeFormatter.ExtractDateTimeOffset(xdt, out extendedYear),
            DateTimeOffset dto => dto,
            string s => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture),
            _ => throw new XQueryException("XPTY0004", $"Expected xs:date, got {arg.GetType().Name}")
        };
        var hasTimezone = arg is Xdm.XsDate xd2 ? xd2.Timezone.HasValue : true;
        var place = DateTimeFormatter.AtomizeToOptionalString(arguments[4]);
        return ValueTask.FromResult<object?>(DateTimeFormatter.Format(dt, picture, hasDate: true, hasTime: false, language: language, calendar: calendar, extendedYear: extendedYear, hasTimezone: hasTimezone, place: place));
    }
}

/// <summary>
/// fn:format-dateTime($value, $picture) as xs:string
/// </summary>
public sealed class FormatDateTimeFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "format-dateTime");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "value"), Type = new XdmSequenceType { ItemType = ItemType.DateTime, Occurrence = Occurrence.ZeroOrOne } },
        new() { Name = new QName(NamespaceId.None, "picture"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = Execution.QueryExecutionContext.Atomize(arguments[0]);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        var picture = arguments[1]?.ToString() ?? "";
        long? extendedYear = null;
        var dt = arg switch
        {
            Xdm.XsDateTime xdt => DateTimeFormatter.ExtractDateTimeOffset(xdt, out extendedYear),
            DateTimeOffset dto => dto,
            string s => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture),
            _ => throw new XQueryException("XPTY0004", $"Expected xs:dateTime, got {arg.GetType().Name}")
        };
        var hasTimezone = arg is Xdm.XsDateTime xdt2 ? xdt2.HasTimezone : true;
        return ValueTask.FromResult<object?>(DateTimeFormatter.Format(dt, picture, hasDate: true, hasTime: true, extendedYear: extendedYear, hasTimezone: hasTimezone));
    }
}

/// <summary>
/// fn:format-dateTime($value, $picture, $language, $calendar, $place) as xs:string
/// </summary>
public sealed class FormatDateTime5Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "format-dateTime");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "value"), Type = new XdmSequenceType { ItemType = ItemType.DateTime, Occurrence = Occurrence.ZeroOrOne } },
        new() { Name = new QName(NamespaceId.None, "picture"), Type = XdmSequenceType.String },
        new() { Name = new QName(NamespaceId.None, "language"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "calendar"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "place"), Type = XdmSequenceType.OptionalString }
    ];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = Execution.QueryExecutionContext.Atomize(arguments[0]);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        var picture = arguments[1]?.ToString() ?? "";
        var language = DateTimeFormatter.AtomizeToOptionalString(arguments[2]);
        var calendar = DateTimeFormatter.AtomizeToOptionalString(arguments[3]);
        long? extendedYear = null;
        var dt = arg switch
        {
            Xdm.XsDateTime xdt => DateTimeFormatter.ExtractDateTimeOffset(xdt, out extendedYear),
            DateTimeOffset dto => dto,
            string s => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture),
            _ => throw new XQueryException("XPTY0004", $"Expected xs:dateTime, got {arg.GetType().Name}")
        };
        var hasTimezone = arg is Xdm.XsDateTime xdt3 ? xdt3.HasTimezone : true;
        var place = DateTimeFormatter.AtomizeToOptionalString(arguments[4]);
        return ValueTask.FromResult<object?>(DateTimeFormatter.Format(dt, picture, hasDate: true, hasTime: true, language: language, calendar: calendar, extendedYear: extendedYear, hasTimezone: hasTimezone, place: place));
    }
}

/// <summary>
/// fn:format-time($value, $picture) as xs:string
/// </summary>
public sealed class FormatTimeFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "format-time");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "value"), Type = new XdmSequenceType { ItemType = ItemType.Time, Occurrence = Occurrence.ZeroOrOne } },
        new() { Name = new QName(NamespaceId.None, "picture"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = Execution.QueryExecutionContext.Atomize(arguments[0]);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        var picture = arguments[1]?.ToString() ?? "";
        var dt = arg switch
        {
            Xdm.XsTime xt => DateTimeFormatter.SafeTimeOffset(xt.Time, xt.Timezone ?? TimeSpan.Zero),
            TimeOnly t => new DateTimeOffset(DateOnly.MinValue, t, TimeSpan.Zero),
            TimeSpan ts => new DateTimeOffset(DateTime.MinValue.Add(ts)),
            Xdm.XsDateTime xdt => xdt.Value,
            DateTimeOffset dto => dto,
            string s => new DateTimeOffset(DateTime.MinValue.Add(TimeOnly.Parse(s, CultureInfo.InvariantCulture).ToTimeSpan())),
            _ => throw new XQueryException("XPTY0004", $"Expected xs:time, got {arg.GetType().Name}")
        };
        var hasTimezone = arg is Xdm.XsTime xt2 ? xt2.Timezone.HasValue : true;
        return ValueTask.FromResult<object?>(DateTimeFormatter.Format(dt, picture, hasDate: false, hasTime: true, hasTimezone: hasTimezone));
    }
}

/// <summary>
/// fn:format-time($value, $picture, $language, $calendar, $place) as xs:string
/// </summary>
public sealed class FormatTime5Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "format-time");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "value"), Type = new XdmSequenceType { ItemType = ItemType.Time, Occurrence = Occurrence.ZeroOrOne } },
        new() { Name = new QName(NamespaceId.None, "picture"), Type = XdmSequenceType.String },
        new() { Name = new QName(NamespaceId.None, "language"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "calendar"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "place"), Type = XdmSequenceType.OptionalString }
    ];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = Execution.QueryExecutionContext.Atomize(arguments[0]);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        var picture = arguments[1]?.ToString() ?? "";
        var language = DateTimeFormatter.AtomizeToOptionalString(arguments[2]);
        var calendar = DateTimeFormatter.AtomizeToOptionalString(arguments[3]);
        var dt = arg switch
        {
            Xdm.XsTime xt => DateTimeFormatter.SafeTimeOffset(xt.Time, xt.Timezone ?? TimeSpan.Zero),
            TimeOnly t => new DateTimeOffset(DateOnly.MinValue, t, TimeSpan.Zero),
            TimeSpan ts => new DateTimeOffset(DateTime.MinValue.Add(ts)),
            Xdm.XsDateTime xdt => xdt.Value,
            DateTimeOffset dto => dto,
            string s => new DateTimeOffset(DateTime.MinValue.Add(TimeOnly.Parse(s, CultureInfo.InvariantCulture).ToTimeSpan())),
            _ => throw new XQueryException("XPTY0004", $"Expected xs:time, got {arg.GetType().Name}")
        };
        var hasTimezone = arg is Xdm.XsTime xt2 ? xt2.Timezone.HasValue : true;
        var place = DateTimeFormatter.AtomizeToOptionalString(arguments[4]);
        return ValueTask.FromResult<object?>(DateTimeFormatter.Format(dt, picture, hasDate: false, hasTime: true, language: language, calendar: calendar, hasTimezone: hasTimezone, place: place));
    }
}

/// <summary>
/// fn:serialize($arg) as xs:string
/// </summary>
public sealed class SerializeFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "serialize");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "arg"), Type = new XdmSequenceType { ItemType = ItemType.Item, Occurrence = Occurrence.ZeroOrMore } }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg == null)
            return ValueTask.FromResult<object?>("");

        // SENR0001: top-level attribute/namespace/function item cannot be serialized
        CheckSerr0001(arg);
        if (arg is IEnumerable<object?> seq && arg is not string)
            foreach (var it in seq) CheckSerr0001(it);

        var nodeProvider = (context as QueryExecutionContext)?.NodeProvider;
        var result = SerializeItem(arg, nodeProvider);
        return ValueTask.FromResult<object?>(result);
    }

    internal static void CheckSerr0001(object? item)
    {
        if (item is Xdm.Nodes.XdmAttribute || item is Xdm.Nodes.XdmNamespace || item is XQueryFunction)
            throw new XQueryRuntimeException("SENR0001",
                "Attribute, namespace, or function item cannot be serialized at the top level");
    }

    internal static string SerializeItem(object? item, INodeProvider? nodeProvider)
    {
        if (item == null) return "";
        return item switch
        {
            string s => s,
            bool b => b ? "true" : "false",
            Xdm.Nodes.XdmNode node => SerializeNodeToXml(node, nodeProvider),
            IDictionary<object, object?> map => SerializeMapAsJson(map),
            List<object?> array => SerializeArrayAsJson(array),
            object?[] arr => string.Join(" ", arr.Where(x => x != null).Select(x => SerializeItem(x, nodeProvider))),
            _ => item.ToString() ?? ""
        };
    }

    internal static string SerializeNodeToXml(Xdm.Nodes.XdmNode node, INodeProvider? provider)
    {
        var sb = new StringBuilder();
        SerializeNodeToXml(node, provider, sb);
        return sb.ToString();
    }

    private static void SerializeNodeToXml(Xdm.Nodes.XdmNode node, INodeProvider? provider, StringBuilder sb)
    {
        switch (node)
        {
            case Xdm.Nodes.XdmDocument doc:
                foreach (var childId in doc.Children)
                    if (provider?.GetNode(childId) is Xdm.Nodes.XdmNode childNode)
                        SerializeNodeToXml(childNode, provider, sb);
                break;
            case Xdm.Nodes.XdmElement elem:
                var prefix = elem.Prefix;
                var localName = elem.LocalName;
                var qname = !string.IsNullOrEmpty(prefix) ? $"{prefix}:{localName}" : localName;
                sb.Append('<').Append(qname);
                // Namespace declarations
                foreach (var nsDecl in elem.NamespaceDeclarations)
                {
                    // Resolve namespace URI via the provider if possible
                    var nsUri = "";
                    if (provider is XdmDocumentStore store)
                        nsUri = store.ResolveNamespaceUri(nsDecl.Namespace)?.ToString() ?? "";
                    if (string.IsNullOrEmpty(nsDecl.Prefix))
                        sb.Append(" xmlns=\"").Append(nsUri).Append('"');
                    else
                        sb.Append(" xmlns:").Append(nsDecl.Prefix).Append("=\"").Append(nsUri).Append('"');
                }
                // Attributes
                foreach (var attrId in elem.Attributes)
                    if (provider?.GetNode(attrId) is Xdm.Nodes.XdmAttribute attr)
                    {
                        var attrName = !string.IsNullOrEmpty(attr.Prefix) ? $"{attr.Prefix}:{attr.LocalName}" : attr.LocalName;
                        sb.Append(' ').Append(attrName).Append("=\"").Append(EscapeAttr(attr.Value)).Append('"');
                    }
                // Children
                var hasChildren = false;
                foreach (var childId in elem.Children)
                {
                    if (!hasChildren) { sb.Append('>'); hasChildren = true; }
                    if (provider?.GetNode(childId) is Xdm.Nodes.XdmNode child)
                        SerializeNodeToXml(child, provider, sb);
                }
                if (!hasChildren)
                    sb.Append("/>");
                else
                    sb.Append("</").Append(qname).Append('>');
                break;
            case Xdm.Nodes.XdmText text:
                sb.Append(System.Security.SecurityElement.Escape(text.Value));
                break;
            case Xdm.Nodes.XdmComment comment:
                sb.Append("<!--").Append(comment.Value).Append("-->");
                break;
            case Xdm.Nodes.XdmProcessingInstruction pi:
                sb.Append("<?").Append(pi.Target);
                if (!string.IsNullOrEmpty(pi.Value))
                    sb.Append(' ').Append(pi.Value);
                sb.Append("?>");
                break;
            default:
                sb.Append(node.StringValue);
                break;
        }
    }

    private static string EscapeAttr(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace("\"", "&quot;");

    private static string SerializeMapAsJson(IDictionary<object, object?> map)
    {
        var sb = new StringBuilder();
        sb.Append('{');
        bool first = true;
        foreach (var (key, value) in map)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append('"').Append(JsonEscape(key.ToString() ?? "")).Append("\":");
            SerializeJsonValue(value, sb);
        }
        sb.Append('}');
        return sb.ToString();
    }

    private static string SerializeArrayAsJson(List<object?> array)
    {
        var sb = new StringBuilder();
        sb.Append('[');
        for (int i = 0; i < array.Count; i++)
        {
            if (i > 0) sb.Append(',');
            SerializeJsonValue(array[i], sb);
        }
        sb.Append(']');
        return sb.ToString();
    }

    private static void SerializeJsonValue(object? value, StringBuilder sb)
    {
        switch (value)
        {
            case null: sb.Append("null"); break;
            case bool b: sb.Append(b ? "true" : "false"); break;
            case int or long or double or float or decimal:
                sb.Append(string.Format(CultureInfo.InvariantCulture, "{0}", value)); break;
            case string s: sb.Append('"').Append(JsonEscape(s)).Append('"'); break;
            case IDictionary<object, object?> m: sb.Append(SerializeMapAsJson(m)); break;
            case List<object?> a: sb.Append(SerializeArrayAsJson(a)); break;
            case object?[] arr:
                sb.Append('[');
                for (int i = 0; i < arr.Length; i++) { if (i > 0) sb.Append(','); SerializeJsonValue(arr[i], sb); }
                sb.Append(']'); break;
            default: sb.Append('"').Append(JsonEscape(value.ToString() ?? "")).Append('"'); break;
        }
    }

    private static string JsonEscape(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("/", "\\/").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
}

/// <summary>
/// fn:serialize($arg, $params) as xs:string
/// </summary>
public sealed class Serialize2Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "serialize");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "arg"), Type = new XdmSequenceType { ItemType = ItemType.Item, Occurrence = Occurrence.ZeroOrMore } },
        new() { Name = new QName(NamespaceId.None, "params"), Type = new XdmSequenceType { ItemType = ItemType.Item, Occurrence = Occurrence.ZeroOrOne } }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0];

        var paramsArg = arguments.Count > 1 ? arguments[1] : null;
        if (paramsArg is object?[] emptyParamsArr && emptyParamsArr.Length == 0) paramsArg = null;
        var paramsMap = paramsArg as IDictionary<object, object?>;
        bool paramsFromMap = paramsMap != null;

        if (paramsMap == null && paramsArg is Xdm.Nodes.XdmElement paramsElem)
        {
            var nodeProv = (context as QueryExecutionContext)?.NodeProvider;
            string? nsUri = null;
            if (nodeProv is XdmDocumentStore paramsStore)
                nsUri = paramsStore.ResolveNamespaceUri(paramsElem.Namespace)?.ToString();
            if (nsUri != "http://www.w3.org/2010/xslt-xquery-serialization"
                || paramsElem.LocalName != "serialization-parameters")
                throw new XQueryRuntimeException("XPTY0004",
                    "serialization parameters element must be <output:serialization-parameters>");
            // Delegate to the comprehensive validation in XQueryResultSerializer
            paramsMap = XQueryResultSerializer.ParseSerializationParamsElement(paramsElem, nodeProv);
        }

        // Delegate to the comprehensive option parsing in XQueryResultSerializer
        var options = XQueryResultSerializer.ParseSerializationOptions(paramsMap, paramsFromMap);
        var method = options.Method;

        // In adaptive mode, attributes ARE allowed at top level
        if (method != OutputMethod.Adaptive)
        {
            SerializeFunction.CheckSerr0001(arg);
            if (arg is IEnumerable<object?> argSeq && arg is not string)
                foreach (var it in argSeq) SerializeFunction.CheckSerr0001(it);
        }

        // JSON: empty sequence => "null"; SERE0023 for multi-item sequences
        if (method == OutputMethod.Json)
        {
            if (arg == null || (arg is object?[] nullArr && nullArr.Length == 0))
                return ValueTask.FromResult<object?>("null");
            if (arg is object?[] jsonArr && jsonArr.Length > 1)
                throw new XQueryRuntimeException("SERE0023",
                    "JSON output method cannot serialize a sequence of more than one item");
            if (arg is IEnumerable<object?> jsonSeq && arg is not string
                && arg is not IDictionary<object, object?> && arg is not List<object?>)
            {
                var count = 0;
                foreach (var _ in jsonSeq) { count++; if (count > 1) break; }
                if (count > 1)
                    throw new XQueryRuntimeException("SERE0023",
                        "JSON output method cannot serialize a sequence of more than one item");
            }
        }

        if (arg == null)
        {
            if (method == OutputMethod.Json)
                return ValueTask.FromResult<object?>("null");
            return ValueTask.FromResult<object?>("");
        }

        if (context is Execution.QueryExecutionContext qec && qec.NodeProvider is XdmDocumentStore store)
        {
            var serializer = new XQueryResultSerializer(store, options);
            return ValueTask.FromResult<object?>(serializer.Serialize(arg));
        }

        var nodeProvider = (context as QueryExecutionContext)?.NodeProvider;
        return ValueTask.FromResult<object?>(SerializeFunction.SerializeItem(arg, nodeProvider));
    }
}

/// <summary>
/// fn:resolve-uri($relative, $base) as xs:anyURI?
/// </summary>
public sealed class ResolveUriFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "resolve-uri");
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalAnyUri;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "relative"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "base"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var relative = arguments[0]?.ToString();
        if (relative == null)
            return ValueTask.FromResult<object?>(null);

        var baseUri = arguments[1]?.ToString() ?? "";

        // If the relative URI is already absolute (has a scheme component), return it directly.
        // Note: .NET's Uri.TryCreate with UriKind.Absolute also accepts path-absolute forms like "/foo/bar"
        // which are NOT RFC 3986 absolute URIs (they have no scheme). We must check for a scheme explicitly.
        if (relative.Contains(':') && Uri.TryCreate(relative, UriKind.Absolute, out var absUri)
            && absUri.Scheme.Length > 0)
            return ValueTask.FromResult<object?>(new Xdm.XsAnyUri(absUri.OriginalString));

        // FORG0002: base URI must be a valid absolute URI (must contain a scheme with ':')
        if (baseUri.Contains("##"))
            throw new XQueryRuntimeException("FORG0002", $"The base URI '{baseUri}' is not a valid URI");
        if (!baseUri.Contains(':') || !Uri.TryCreate(baseUri, UriKind.Absolute, out var baseUriObj))
            throw new XQueryRuntimeException("FORG0002", $"The base URI '{baseUri}' is not a valid absolute URI");

        // FORG0002: base URI must not contain a fragment (RFC 3986 §5.2)
        if (baseUri.Contains('#'))
            throw new XQueryRuntimeException("FORG0002", $"The base URI '{baseUri}' must not contain a fragment identifier");

        // FORG0002: relative URI must be a valid URI reference
        if (relative.Contains("##"))
            throw new XQueryRuntimeException("FORG0002", $"The relative URI '{relative}' is not a valid URI reference");
        // A colon in the first path segment (before any slash) makes the relative URI invalid
        // (RFC 3986 §3.3: it would be ambiguous with a scheme)
        {
            var firstSlash = relative.IndexOf('/');
            var firstColon = relative.IndexOf(':');
            if (firstColon >= 0 && (firstSlash < 0 || firstColon < firstSlash))
                throw new XQueryRuntimeException("FORG0002", $"The relative URI '{relative}' is not a valid URI reference (colon in first path segment)");
        }

        try
        {
            if (Uri.TryCreate(baseUriObj, relative, out var resolved))
            {
                // Use OriginalString instead of AbsoluteUri: .NET normalizes "http://g" → "http://g/"
                // (adds trailing slash for empty path), but OriginalString preserves the correct form.
                return ValueTask.FromResult<object?>(new PhoenixmlDb.Xdm.XsAnyUri(resolved.OriginalString));
            }
            throw new XQueryRuntimeException("FORG0002", $"Cannot resolve URI '{relative}' against base '{baseUri}'");
        }
        catch (XQueryRuntimeException) { throw; }
        catch (UriFormatException)
        {
            throw new XQueryRuntimeException("FORG0002", $"Cannot resolve URI '{relative}' against base '{baseUri}'");
        }
    }
}

/// <summary>
/// fn:resolve-uri($relative) as xs:anyURI?
/// </summary>
public sealed class ResolveUri1Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "resolve-uri");
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalAnyUri;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "relative"), Type = XdmSequenceType.OptionalString }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var relative = arguments[0]?.ToString();
        if (relative == null)
            return ValueTask.FromResult<object?>(null);

        var baseUri = context.StaticBaseUri ?? "";

        // FORG0002: reject malformed percent-escapes (e.g. "%gg")
        static bool HasBadPercentEscape(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '%')
                {
                    if (i + 2 >= s.Length) return true;
                    if (!Uri.IsHexDigit(s[i + 1]) || !Uri.IsHexDigit(s[i + 2])) return true;
                    i += 2;
                }
            }
            return false;
        }
        if (HasBadPercentEscape(relative))
            throw new XQueryRuntimeException("FORG0002", $"The relative URI '{relative}' contains invalid percent-encoding");

        try
        {
            if (Uri.TryCreate(baseUri, UriKind.Absolute, out var baseUriObj) &&
                Uri.TryCreate(baseUriObj, relative, out var resolved))
            {
                return ValueTask.FromResult<object?>(new PhoenixmlDb.Xdm.XsAnyUri(resolved.OriginalString));
            }
            if (Uri.TryCreate(relative, UriKind.Absolute, out _))
                return ValueTask.FromResult<object?>(new PhoenixmlDb.Xdm.XsAnyUri(relative));
            return ValueTask.FromResult<object?>(new PhoenixmlDb.Xdm.XsAnyUri(relative));
        }
        catch (UriFormatException)
        {
            // URI resolution failed — return the relative URI unchanged per spec
            return ValueTask.FromResult<object?>(new PhoenixmlDb.Xdm.XsAnyUri(relative));
        }
    }
}

/// <summary>
/// fn:QName($paramURI, $paramQName) as xs:QName
/// </summary>
public sealed class QNameFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "QName");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.QName, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "paramURI"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "paramQName"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        // XPTY0004: arguments must be strings (or untypedAtomic), not numeric etc.
        var rawUri = Execution.QueryExecutionContext.Atomize(arguments[0]);
        var rawQName = Execution.QueryExecutionContext.Atomize(arguments[1]);
        if (rawQName is not null and not string and not Xdm.XsUntypedAtomic)
            throw new XQueryRuntimeException("XPTY0004",
                $"fn:QName second argument must be a string, got {rawQName.GetType().Name}");
        if (rawUri is not null and not string and not Xdm.XsUntypedAtomic)
            throw new XQueryRuntimeException("XPTY0004",
                $"fn:QName first argument must be a string, got {rawUri.GetType().Name}");
        var nsUri = rawUri?.ToString() ?? "";
        var qname = rawQName?.ToString() ?? "";

        var colonIdx = qname.IndexOf(':', StringComparison.Ordinal);
        string? prefix = null;
        string localName;
        if (colonIdx > 0)
        {
            prefix = qname[..colonIdx];
            localName = qname[(colonIdx + 1)..];
        }
        else
        {
            localName = qname;
        }

        // FOCA0002: validate lexical form of QName
        static bool IsNCName(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            try { System.Xml.XmlConvert.VerifyNCName(s); return true; }
            catch { return false; }
        }
        if (prefix != null && !IsNCName(prefix))
            throw new XQueryRuntimeException("FOCA0002", $"Invalid QName prefix: '{prefix}'");
        if (!IsNCName(localName))
            throw new XQueryRuntimeException("FOCA0002", $"Invalid QName local name: '{localName}'");
        // FOCA0002: if a prefix is present, the namespace URI must not be empty
        if (prefix != null && string.IsNullOrEmpty(nsUri))
            throw new XQueryRuntimeException("FOCA0002", "Prefix supplied but namespace URI is empty");

        var nsId = string.IsNullOrEmpty(nsUri) ? NamespaceId.None : new NamespaceId((uint)Math.Abs(nsUri.GetHashCode()));
        // When URI is explicitly empty (""), set RuntimeNamespace to "" so that
        // fn:function-lookup can distinguish "explicitly no namespace" from "namespace unknown".
        var result = new QName(nsId, localName, prefix) { RuntimeNamespace = nsUri.Length == 0 ? "" : nsUri };
        return ValueTask.FromResult<object?>(result);
    }
}

/// <summary>
/// fn:environment-variable($name) as xs:string?
/// </summary>
public sealed class EnvironmentVariableFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "environment-variable");
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalString;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "name"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg == null || (arg is object?[] arr && arr.Length == 0))
            throw new XQueryException("XPTY0004", "fn:environment-variable() requires xs:string, got empty sequence");
        if (arg is not string && arg is not PhoenixmlDb.Xdm.XsUntypedAtomic)
            throw new XQueryException("XPTY0004", $"fn:environment-variable() requires xs:string, got {arg.GetType().Name}");
        // For security, return empty sequence (no environment variable access)
        return ValueTask.FromResult<object?>(null);
    }
}

/// <summary>
/// fn:available-environment-variables() as xs:string*
/// </summary>
public sealed class AvailableEnvironmentVariablesFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "available-environment-variables");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.String, Occurrence = Occurrence.ZeroOrMore };
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        // For security, return empty sequence
        return ValueTask.FromResult<object?>(Array.Empty<string>());
    }
}
