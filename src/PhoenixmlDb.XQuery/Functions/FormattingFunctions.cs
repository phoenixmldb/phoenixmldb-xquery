using System.Globalization;
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

        var result = FormatIntegerStatic(value, picture);
        return ValueTask.FromResult<object?>(result);
    }

    internal static string FormatIntegerStatic(long value, string picture)
    {
        // Check for ordinal modifier (e.g. "1;o")
        var ordinal = false;
        var basePicture = picture;
        var semiIdx = picture.IndexOf(';', StringComparison.Ordinal);
        if (semiIdx >= 0)
        {
            var modifier = picture[(semiIdx + 1)..].Trim();
            basePicture = picture[..semiIdx];
            ordinal = modifier.Contains('o', StringComparison.OrdinalIgnoreCase);
        }

        // Validate: in decimal-digit pictures, '#' (optional) must precede '0' (mandatory)
        if (basePicture.Contains('#', StringComparison.Ordinal) && basePicture.Contains('0', StringComparison.Ordinal))
        {
            var lastHash = basePicture.LastIndexOf('#');
            var firstZero = basePicture.IndexOf('0');
            if (firstZero < lastHash)
                throw new XQueryException("FODF1310", "Invalid picture string for format-integer: mandatory digit '0' cannot precede optional digit '#'");
        }

        var formatted = basePicture switch
        {
            "1" => value.ToString(CultureInfo.InvariantCulture),
            "01" => value.ToString("D2", CultureInfo.InvariantCulture),
            "001" => value.ToString("D3", CultureInfo.InvariantCulture),
            "a" => ToAlpha(value, lowercase: true),
            "A" => ToAlpha(value, lowercase: false),
            "i" => ToRoman(value, lowercase: true),
            "I" => ToRoman(value, lowercase: false),
            "w" => NumberToWords(value).ToLowerInvariant(),
            "W" => NumberToWords(value).ToUpperInvariant(),
            "Ww" => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(NumberToWords(value)),
            _ when basePicture.Contains(',', StringComparison.Ordinal) => FormatWithGrouping(value, basePicture),
            _ when basePicture.Length > 0 && char.IsDigit(basePicture[0]) =>
                value.ToString($"D{basePicture.Length}", CultureInfo.InvariantCulture),
            _ => value.ToString(CultureInfo.InvariantCulture)
        };

        if (ordinal && (basePicture == "1" || (basePicture.Length > 0 && char.IsDigit(basePicture[0]))))
        {
            formatted += GetOrdinalSuffix(value);
        }

        return formatted;
    }

    private static string FormatWithGrouping(long value, string picture)
    {
        // Parse picture like "#,000" or "#,##0" to determine grouping size and min digits
        // Remove '#' and ',' to count mandatory digits
        var stripped = picture.Replace(",", "", StringComparison.Ordinal).Replace("#", "", StringComparison.Ordinal);
        var minDigits = stripped.Length; // number of '0' digits

        // Determine grouping size from the position of the last ',' relative to the end
        var lastComma = picture.LastIndexOf(',');
        var groupSize = picture.Length - lastComma - 1; // chars after last comma

        var absValue = Math.Abs(value);
        var digits = absValue.ToString(CultureInfo.InvariantCulture);

        // Pad with leading zeros if needed
        if (digits.Length < minDigits)
            digits = digits.PadLeft(minDigits, '0');

        // Insert grouping separators from right to left
        if (groupSize > 0)
        {
            var sb = new StringBuilder();
            for (var i = digits.Length - 1; i >= 0; i--)
            {
                sb.Insert(0, digits[i]);
                var posFromRight = digits.Length - 1 - i;
                if (posFromRight > 0 && posFromRight % groupSize == groupSize - 1 && i > 0)
                    sb.Insert(0, ',');
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

    private static string NumberToWords(long number)
    {
        if (number == 0) return "zero";
        if (number < 0) return "minus " + NumberToWordsPositive(Math.Abs(number));
        return NumberToWordsPositive(number);
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
        // lang parameter currently ignored (always uses English formatting)

        var result = FormatIntegerFunction.FormatIntegerStatic(value, picture);
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
        var rawValue = Execution.QueryExecutionContext.Atomize(arguments[0]);
        var value = rawValue switch
        {
            null => double.NaN, // empty sequence → NaN
            double d => d,
            decimal m => (double)m,
            long l => (double)l,
            int i => (double)i,
            string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
            _ => Convert.ToDouble(rawValue, CultureInfo.InvariantCulture)
        };
        var picture = arguments[1]?.ToString() ?? "#,##0.##";

        // Parse XSLT picture format into .NET format
        var result = FormatWithPicture(value, picture);
        return ValueTask.FromResult<object?>(result);
    }

    private static string FormatWithPicture(double value, string picture)
    {
        // Handle sub-pictures (positive;negative)
        var parts = picture.Split(';');
        if (value < 0 && parts.Length > 1)
        {
            value = Math.Abs(value);
            return FormatSubPicture(value, parts[1]);
        }
        return FormatSubPicture(value, parts[0]);
    }

    private static string FormatSubPicture(double value, string picture)
    {
        // Convert XSLT picture chars to .NET: # → #, 0 → 0, , → ,(group), . → .(decimal)
        try
        {
            return value.ToString(picture, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            // Invalid picture string — fall back to default formatting
            return value.ToString(CultureInfo.InvariantCulture);
        }
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
        // Per XPath spec §4.7.2, the 3rd arg is a decimal-format-name (EQName).
        // We delegate to the 2-arg version since the default decimal format is always used.
        // Named decimal formats require declare decimal-format in the prolog, which is
        // handled at the query compilation level — the runtime format-number always uses
        // the effective decimal format for the expression's static context.
        var twoArgFunc = new FormatNumberFunction();
        return twoArgFunc.InvokeAsync(arguments, context);
    }
}

/// <summary>
/// Shared formatting logic for format-date, format-dateTime, and format-time.
/// Parses XSLT picture strings like "[Y0001]-[M01]-[D01]" and formats date/time components.
/// </summary>
internal static class DateTimeFormatter
{
    private static readonly string[] MonthNames =
        ["January", "February", "March", "April", "May", "June",
         "July", "August", "September", "October", "November", "December"];

    private static readonly string[] DayNames =
        ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

    public static string Format(DateTimeOffset dt, string picture, bool hasDate, bool hasTime, string? language = null, long? extendedYear = null)
    {
        // Check if language is supported; if not, fall back to English and add marker
        string? languageMarker = null;
        if (language != null && language != "en" && !language.StartsWith("en-", StringComparison.OrdinalIgnoreCase))
        {
            languageMarker = "[Language: en]";
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
                sb.Append(FormatComponent(dt, spec, hasDate, hasTime, extendedYear));
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

    private static string FormatComponent(DateTimeOffset dt, string spec, bool hasDate, bool hasTime, long? extendedYear = null)
    {
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
        var widthIdx = presentation.IndexOf(',', StringComparison.Ordinal);
        int? minWidth = null;
        int? maxWidth = null;
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
            else if (int.TryParse(widthSpec, NumberStyles.Integer, CultureInfo.InvariantCulture, out var w))
            {
                minWidth = w;
                maxWidth = w;
            }
        }

        return component switch
        {
            'Y' => FormatYear(extendedYear.HasValue ? (int)Math.Abs(extendedYear.Value) : dt.Year, presentation, minWidth),
            'M' => FormatMonth(dt.Month, presentation, maxWidth),
            'D' => FormatNumber(dt.Day, presentation, minWidth, maxWidth),
            'd' => FormatNumber(dt.DayOfYear, presentation, minWidth, maxWidth),
            'F' => FormatDayOfWeek(dt.DayOfWeek, presentation, minWidth, maxWidth),
            'W' => FormatNumber(ISOWeekOfYear(dt), presentation, minWidth, maxWidth),
            'w' => FormatNumber(GetWeekOfMonth(dt), presentation, minWidth, maxWidth),
            'H' => FormatNumber(dt.Hour, presentation, minWidth, maxWidth),
            'h' => FormatNumber(dt.Hour == 0 ? 12 : dt.Hour > 12 ? dt.Hour - 12 : dt.Hour, presentation, minWidth, maxWidth),
            'm' => FormatNumber(dt.Minute, presentation, minWidth, maxWidth),
            's' => FormatNumber(dt.Second, presentation, minWidth, maxWidth),
            'f' => FormatFractionalSeconds(dt, presentation),
            'P' => FormatAmPm(dt.Hour, presentation, minWidth, maxWidth),
            'Z' or 'z' => FormatTimezone(dt, presentation, component, maxWidth),
            'E' => FormatEra(extendedYear.HasValue ? (int)extendedYear.Value : dt.Year, presentation),
            'C' => "ISO", // calendar
            _ => $"[{spec}]"
        };
    }

    private static string FormatYear(int year, string presentation, int? minWidth)
    {
        // Parse ordinal flag and width from presentation
        var ordinal = false;
        var pres = presentation;
        if (pres.EndsWith('o'))
        {
            ordinal = true;
            pres = pres[..^1];
        }

        // Roman numeral formatting
        if (pres == "I")
        {
            var roman = ToRoman(Math.Abs(year));
            if (minWidth.HasValue && roman.Length < minWidth.Value)
                roman = roman.PadRight(minWidth.Value);
            return roman;
        }
        if (pres == "i")
        {
            var roman = ToRoman(Math.Abs(year)).ToLowerInvariant();
            if (minWidth.HasValue && roman.Length < minWidth.Value)
                roman = roman.PadRight(minWidth.Value);
            return roman;
        }

        // Word formatting
        if (pres.Length > 0 && (pres[0] == 'W' || pres[0] == 'w'))
            return FormatWord(Math.Abs(year), pres, ordinal);

        // Detect non-ASCII zero digit
        char zeroDigit = '0';
        if (pres.Length > 0 && !char.IsAscii(pres[0]) && char.IsDigit(pres[0]))
            zeroDigit = (char)(pres[0] - char.GetNumericValue(pres[0]));

        // Count zero-padding digits: "0001" = 4, "01" = 2, "1" = 1 (no padding)
        var padDigits = 0;
        foreach (var c in pres)
        {
            if (char.IsDigit(c) && char.GetNumericValue(c) == 0) padDigits++;
            else if (char.IsDigit(c)) { padDigits++; break; } // last digit
            else break;
        }
        if (padDigits == 0) padDigits = minWidth ?? 1; // default presentation is "1" (minimum 1 digit)

        var result = Math.Abs(year).ToString(CultureInfo.InvariantCulture);
        // Only truncate to 2 digits for "01" pattern (2 zero-fill digits)
        if (padDigits == 2 && result.Length > 2)
            result = result[^2..]; // last 2 digits

        result = result.PadLeft(padDigits, '0');

        // Replace ASCII digits with target digit family
        if (zeroDigit != '0')
        {
            var chars = result.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                if (chars[i] >= '0' && chars[i] <= '9')
                    chars[i] = (char)(zeroDigit + (chars[i] - '0'));
            }
            result = new string(chars);
        }

        if (ordinal) result += GetOrdinalSuffix(year);
        return result;
    }

    private static string FormatMonth(int month, string presentation, int? maxWidth)
    {
        // N/n = name, default = number
        if (presentation.Length > 0 && (presentation[0] == 'N' || presentation[0] == 'n'))
        {
            var name = MonthNames[month - 1];
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
        return FormatNumber(month, presentation);
    }

    private static string FormatDayOfWeek(DayOfWeek dow, string presentation, int? minWidth, int? maxWidth)
    {
        // Map .NET DayOfWeek (Sunday=0) to ISO (Monday=0)
        var isoIdx = dow == DayOfWeek.Sunday ? 6 : (int)dow - 1;
        if (presentation.Length > 0 && (presentation[0] == 'N' || presentation[0] == 'n'))
        {
            var name = DayNames[isoIdx];
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
        if (presentation.Length == 0) return value.ToString(CultureInfo.InvariantCulture);

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

        // Detect non-ASCII zero digit
        char zeroDigit = '0';
        if (pres.Length > 0 && !char.IsAscii(pres[0]) && char.IsDigit(pres[0]))
        {
            // Find the zero of this digit family
            zeroDigit = (char)(pres[0] - char.GetNumericValue(pres[0]));
        }

        // Count padding digits
        var padDigits = 0;
        foreach (var c in pres)
        {
            if (char.IsDigit(c)) padDigits++;
            else break;
        }
        if (padDigits == 0) padDigits = 1;

        var result = value.ToString(CultureInfo.InvariantCulture).PadLeft(padDigits, '0');

        // Replace ASCII digits with the target digit family
        if (zeroDigit != '0')
        {
            var chars = result.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                if (chars[i] >= '0' && chars[i] <= '9')
                    chars[i] = (char)(zeroDigit + (chars[i] - '0'));
            }
            result = new string(chars);
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

    private static string FormatFractionalSeconds(DateTimeOffset dt, string presentation)
    {
        var ms = dt.Millisecond;
        var digits = presentation.Length > 0 ? presentation.Length : 3;
        var frac = ms.ToString(CultureInfo.InvariantCulture).PadLeft(3, '0');
        if (digits > 3) frac = frac.PadRight(digits, '0');
        else if (digits < 3) frac = frac[..digits];
        return frac;
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

    private static string FormatTimezone(DateTimeOffset dt, string presentation, char component, int? maxWidth = null)
    {
        var offset = dt.Offset;
        var abs = offset < TimeSpan.Zero ? -offset : offset;
        var sign = offset < TimeSpan.Zero ? "-" : "+";

        // Component 'z': named/GMT-offset timezone
        if (component == 'z')
        {
            // Minimal format: omit :00 minutes when presentation is "0" or max width is small
            // Minimal format: omit :00 minutes
            if (presentation == "0")
            {
                // Presentation "0" means single-digit minimum
                if (abs.Minutes == 0)
                    return $"GMT{sign}{abs.Hours}";
                return $"GMT{sign}{abs.Hours}:{abs.Minutes:D2}";
            }
            var minimal = maxWidth.HasValue && maxWidth.Value < 5;
            if (minimal)
            {
                // Width-constrained minimal: zero-padded hours, omit :00 minutes
                if (abs.Minutes == 0)
                    return $"GMT{sign}{abs.Hours:D2}";
                return $"GMT{sign}{abs.Hours:D2}:{abs.Minutes:D2}";
            }
            // Full format: GMT+HH:MM
            return $"GMT{sign}{abs.Hours:D2}:{abs.Minutes:D2}";
        }

        // Z component: numeric format
        // Check for 't' suffix which means use Z for UTC
        var useTforZero = presentation.EndsWith('t');
        var pres = useTforZero ? presentation[..^1] : presentation;

        if (pres.Length == 0 || pres == "01:01")
        {
            // Default: +HH:MM format, +00:00 for UTC (unless 't' suffix)
            if (offset == TimeSpan.Zero)
                return useTforZero ? "Z" : $"{sign}{abs.Hours:D2}:{abs.Minutes:D2}";
            return $"{sign}{abs.Hours:D2}:{abs.Minutes:D2}";
        }
        if (pres == "0101" || pres == "0000")
        {
            if (offset == TimeSpan.Zero)
                return useTforZero ? "Z" : $"+{abs.Hours:D2}{abs.Minutes:D2}";
            return $"{sign}{abs.Hours:D2}{abs.Minutes:D2}";
        }
        if (pres == "1:01")
        {
            if (offset == TimeSpan.Zero)
                return useTforZero ? "Z" : $"{sign}{abs.Hours}:{abs.Minutes:D2}";
            var h = abs.Hours.ToString(CultureInfo.InvariantCulture);
            return $"{sign}{h}:{abs.Minutes:D2}";
        }

        // Default: +HH:MM
        if (offset == TimeSpan.Zero)
            return useTforZero ? "Z" : $"{sign}{abs.Hours:D2}:{abs.Minutes:D2}";
        return $"{sign}{abs.Hours:D2}:{abs.Minutes:D2}";
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
        var first = new DateTime(dt.Year, dt.Month, 1);
        var firstDow = first.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)first.DayOfWeek;
        // Days from first to first Thursday (ISO day 4)
        var daysToThursday = (4 - firstDow + 7) % 7;
        var firstThursday = first.AddDays(daysToThursday);
        // Monday of that week = start of week 1
        var week1Start = firstThursday.AddDays(-3);
        // Monday of the date's week
        var dateDow = dt.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)dt.DayOfWeek;
        var dateMonday = dt.DateTime.AddDays(1 - dateDow);
        // Week difference
        var weekDiff = (int)Math.Round((dateMonday - week1Start).TotalDays / 7.0) + 1;
        return weekDiff < 1 ? 1 : weekDiff;
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
        return ValueTask.FromResult<object?>(DateTimeFormatter.Format(dt, picture, hasDate: true, hasTime: false, extendedYear: extendedYear));
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
        var language = arguments[2]?.ToString();
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
        return ValueTask.FromResult<object?>(DateTimeFormatter.Format(dt, picture, hasDate: true, hasTime: false, language: language, extendedYear: extendedYear));
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
        return ValueTask.FromResult<object?>(DateTimeFormatter.Format(dt, picture, hasDate: true, hasTime: true, extendedYear: extendedYear));
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
        var language = arguments[2]?.ToString();
        long? extendedYear = null;
        var dt = arg switch
        {
            Xdm.XsDateTime xdt => DateTimeFormatter.ExtractDateTimeOffset(xdt, out extendedYear),
            DateTimeOffset dto => dto,
            string s => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture),
            _ => throw new XQueryException("XPTY0004", $"Expected xs:dateTime, got {arg.GetType().Name}")
        };
        return ValueTask.FromResult<object?>(DateTimeFormatter.Format(dt, picture, hasDate: true, hasTime: true, language: language, extendedYear: extendedYear));
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
            Xdm.XsTime xt => new DateTimeOffset(DateOnly.MinValue, xt.Time, xt.Timezone ?? TimeSpan.Zero),
            TimeOnly t => new DateTimeOffset(DateOnly.MinValue, t, TimeSpan.Zero),
            TimeSpan ts => new DateTimeOffset(DateTime.MinValue.Add(ts)),
            Xdm.XsDateTime xdt => xdt.Value,
            DateTimeOffset dto => dto,
            string s => new DateTimeOffset(DateTime.MinValue.Add(TimeOnly.Parse(s, CultureInfo.InvariantCulture).ToTimeSpan())),
            _ => throw new XQueryException("XPTY0004", $"Expected xs:time, got {arg.GetType().Name}")
        };
        return ValueTask.FromResult<object?>(DateTimeFormatter.Format(dt, picture, hasDate: false, hasTime: true));
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
        var language = arguments[2]?.ToString();
        var dt = arg switch
        {
            Xdm.XsTime xt => new DateTimeOffset(DateOnly.MinValue, xt.Time, xt.Timezone ?? TimeSpan.Zero),
            TimeOnly t => new DateTimeOffset(DateOnly.MinValue, t, TimeSpan.Zero),
            TimeSpan ts => new DateTimeOffset(DateTime.MinValue.Add(ts)),
            Xdm.XsDateTime xdt => xdt.Value,
            DateTimeOffset dto => dto,
            string s => new DateTimeOffset(DateTime.MinValue.Add(TimeOnly.Parse(s, CultureInfo.InvariantCulture).ToTimeSpan())),
            _ => throw new XQueryException("XPTY0004", $"Expected xs:time, got {arg.GetType().Name}")
        };
        return ValueTask.FromResult<object?>(DateTimeFormatter.Format(dt, picture, hasDate: false, hasTime: true, language: language));
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

        var nodeProvider = (context as QueryExecutionContext)?.NodeProvider;
        var result = SerializeItem(arg, nodeProvider);
        return ValueTask.FromResult<object?>(result);
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
        s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
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
        if (arg == null)
            return ValueTask.FromResult<object?>("");

        var paramsArg = arguments.Count > 1 ? arguments[1] : null;
        var paramsMap = paramsArg as IDictionary<object, object?>;

        // Build serialization options from the params map
        var method = OutputMethod.Adaptive;
        var indent = false;
        var omitXmlDeclaration = false;
        string? encoding = null;
        string? standalone = null;

        if (paramsMap != null)
        {
            if (paramsMap.TryGetValue("method", out var m) && m is string ms)
                method = ms.ToLowerInvariant() switch
                {
                    "json" => OutputMethod.Json,
                    "xml" => OutputMethod.Xml,
                    "text" => OutputMethod.Text,
                    _ => OutputMethod.Adaptive
                };

            if (paramsMap.TryGetValue("indent", out var ind))
                indent = ind is true || (ind is string si && si.Equals("yes", StringComparison.OrdinalIgnoreCase));

            if (paramsMap.TryGetValue("omit-xml-declaration", out var omit))
                omitXmlDeclaration = omit is true || (omit is string so && so.Equals("yes", StringComparison.OrdinalIgnoreCase));

            if (paramsMap.TryGetValue("encoding", out var enc) && enc is string es)
                encoding = es;

            if (paramsMap.TryGetValue("standalone", out var sa) && sa is string ss)
                standalone = ss.ToLowerInvariant() switch
                {
                    "yes" or "no" or "omit" => ss.ToLowerInvariant(),
                    _ => null
                };
        }

        var options = new SerializationOptions
        {
            Method = method,
            Indent = indent,
            OmitXmlDeclaration = omitXmlDeclaration,
            Encoding = encoding,
            Standalone = standalone
        };

        // Try to get the document store from the execution context for proper XML serialization
        if (context is Execution.QueryExecutionContext qec && qec.NodeProvider is XdmDocumentStore store)
        {
            var serializer = new XQueryResultSerializer(store, options);
            return ValueTask.FromResult<object?>(serializer.Serialize(arg));
        }

        // Fallback: serialize using node provider from context (covers XSLT InMemoryNodeStore)
        var nodeProvider = (context as QueryExecutionContext)?.NodeProvider;

        // Handle method-specific serialization
        if (method == OutputMethod.Json)
            return ValueTask.FromResult<object?>(SerializeFunction.SerializeItem(arg, nodeProvider));

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

        // FORG0002: base URI must be a valid absolute URI (must contain a scheme with ':')
        // On Linux, Uri.TryCreate treats absolute paths like /foo as file:///foo — reject those
        // Also reject URIs with double '#' which are syntactically invalid per RFC 3986
        if (baseUri.Contains("##"))
            throw new XQueryRuntimeException("FORG0002", $"The base URI '{baseUri}' is not a valid URI");
        if (!baseUri.Contains(':') || !Uri.TryCreate(baseUri, UriKind.Absolute, out var baseUriObj))
            throw new XQueryRuntimeException("FORG0002", $"The base URI '{baseUri}' is not a valid absolute URI");

        // FORG0002: relative URI must be a valid URI reference (no double '#')
        if (relative.Contains("##"))
            throw new XQueryRuntimeException("FORG0002", $"The relative URI '{relative}' is not a valid URI reference");

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
        var nsUri = arguments[0]?.ToString() ?? "";
        var qname = arguments[1]?.ToString() ?? "";

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

        var nsId = string.IsNullOrEmpty(nsUri) ? NamespaceId.None : new NamespaceId((uint)Math.Abs(nsUri.GetHashCode()));
        var result = new QName(nsId, localName, prefix) { RuntimeNamespace = string.IsNullOrEmpty(nsUri) ? null : nsUri };
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
