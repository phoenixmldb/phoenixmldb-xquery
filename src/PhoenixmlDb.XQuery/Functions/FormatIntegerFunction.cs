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

        var result = FormatIntegerStatic(value, picture, null, context);
        return ValueTask.FromResult<object?>(result);
    }

    internal static string FormatIntegerStatic(long value, string picture, string? lang = null, Ast.ExecutionContext? context = null)
    {
        // Empty picture is invalid
        if (string.IsNullOrEmpty(picture))
            throw context.Error("FODF1310", "Empty picture string for format-integer");

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
                    throw context.Error("FODF1310", "Unmatched parenthesis in format modifier");
                ordinalSuffix = modifier[(modIdx + 1)..closeIdx];
                modIdx = closeIdx + 1;
            }
            // Nothing should follow
            if (modIdx < modifier.Length)
                throw context.Error("FODF1310", $"Invalid format modifier: unexpected '{modifier[modIdx]}' after format modifier");
        }

        if (string.IsNullOrEmpty(basePicture))
            throw context.Error("FODF1310", "Empty primary format token for format-integer");

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
                throw context.Error("FODF1310", "Invalid picture string for format-integer: mandatory digit cannot precede optional digit '#'");
        }

        // Validate grouping separator placement in decimal-digit pictures
        if (basePicture.Contains(',', StringComparison.Ordinal))
        {
            // Trailing grouping separator is invalid
            if (basePicture.EndsWith(','))
                throw context.Error("FODF1310", "Invalid picture: trailing grouping separator");
            // Leading grouping separator is invalid
            if (basePicture.StartsWith(','))
                throw context.Error("FODF1310", "Invalid picture: leading grouping separator");
            // Adjacent grouping separators are invalid
            if (basePicture.Contains(",,", StringComparison.Ordinal))
                throw context.Error("FODF1310", "Invalid picture: adjacent grouping separators");
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
                throw context.Error("FODF1310",
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
                        throw context.Error("FODF1310",
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
    private static bool HasGroupingSeparator(string picture, Ast.ExecutionContext? context = null)
    {
        var seenDigit = false;
        foreach (var rune in picture.EnumerateRunes())
        {
            if (rune.Value == '#' || Rune.IsDigit(rune)) { seenDigit = true; }
            else if (seenDigit) return true; // non-digit after a digit = separator
        }
        return false;
    }

    private static string FormatWithGrouping(long value, string picture, Ast.ExecutionContext? context = null)
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

    private static string ToAlpha(long number, bool lowercase, Ast.ExecutionContext? context = null)
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

    private static string ToRoman(long number, bool lowercase, Ast.ExecutionContext? context = null)
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

    private static string NumberToWords(long number, Ast.ExecutionContext? context = null)
    {
        if (number == 0) return "zero";
        if (number < 0) return "minus " + NumberToWordsPositive(Math.Abs(number));
        return NumberToWordsPositive(number);
    }

    private static string NumberToOrdinalWords(long number, Ast.ExecutionContext? context = null)
    {
        if (number == 0) return "zeroth";
        if (number < 0) return "minus " + NumberToOrdinalWordsPositive(Math.Abs(number));
        return NumberToOrdinalWordsPositive(number);
    }

    private static string NumberToOrdinalWordsPositive(long number, Ast.ExecutionContext? context = null)
    {
        var cardinal = NumberToWordsPositive(number);
        if (number == 0) cardinal = "zero";
        return MakeOrdinalWord(cardinal);
    }

    /// <summary>Converts a cardinal word string to ordinal by replacing the last word.</summary>
    private static string MakeOrdinalWord(string cardinal, Ast.ExecutionContext? context = null)
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

    private static string NumberToWordsPositive(long number, Ast.ExecutionContext? context = null)
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

    private static string GetOrdinalSuffix(long number, Ast.ExecutionContext? context = null)
    {
        var abs = Math.Abs(number);
        var lastTwo = abs % 100;
        if (lastTwo >= 11 && lastTwo <= 13) return "th";
        return (abs % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
    }

    /// <summary>Checks if the first character/rune in the picture is a decimal digit (Nd category).</summary>
    private static bool IsDecimalDigitRune(string picture, Ast.ExecutionContext? context = null)
    {
        if (picture.Length == 0) return false;
        var firstRune = Rune.GetRuneAt(picture, 0);
        return Rune.IsDigit(firstRune);
    }

    /// <summary>Formats a value using a non-ASCII decimal digit family, handling padding and digit conversion.</summary>
    private static string FormatWithDecimalDigitFamily(long value, string basePicture, Ast.ExecutionContext? context = null)
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
    private static string? TryFormatUnicodeSequence(long value, string basePicture, Ast.ExecutionContext? context = null)
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

    private static bool TryGetOffsetSequenceRange(int codepoint, out int start, out int maxVal, Ast.ExecutionContext? context = null)
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
    private static string ToGreekAlpha(long value, bool uppercase, Ast.ExecutionContext? context = null)
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
    private static string ToCjkNumber(long value, Ast.ExecutionContext? context = null)
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

    private static void ToCjkNumberRecursive(long value, StringBuilder result, string[] digits, bool topLevel, Ast.ExecutionContext? context = null)
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

    private static string GermanOrdinal(long n, string? suffix, Ast.ExecutionContext? context = null)
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

    private static string FrenchOrdinal(long n, Ast.ExecutionContext? context = null)
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

    private static string ItalianOrdinal(long n, string? suffix, Ast.ExecutionContext? context = null)
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
