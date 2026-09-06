using System.Globalization;
using System.Numerics;
using System.Text;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

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
    internal static string? AtomizeToOptionalString(object? argument, Ast.ExecutionContext? context = null)
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

    public static string Format(DateTimeOffset dt, string picture, bool hasDate, bool hasTime, string? language = null, string? calendar = null, long? extendedYear = null, bool hasTimezone = true, string? place = null, Ast.ExecutionContext? context = null)
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
                    throw context.Error("FOFD1340", $"Invalid calendar name: {calendar}");
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
                    throw context.Error("FOFD1340", $"Invalid calendar name: {calendar}");
                // Treat as namespaced (non-standard) calendar — fall through to Gregorian fallback
                namespaceUri = $"urn:calendar-prefix:{prefix}"; // synthetic namespace to indicate it's prefixed
            }

            // Validate local name is a valid NCName (basic check)
            if (localName.Length == 0 || char.IsDigit(localName[0]))
                throw context.Error("FOFD1340", $"Invalid calendar name: {calendar}");

            // Unknown calendar in no namespace (or empty namespace) → FOFD1340
            if ((namespaceUri == null || namespaceUri.Length == 0) && !knownCalendars.Contains(localName))
                throw context.Error("FOFD1340", $"Unknown calendar: {calendar}");
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
    private static bool IsValidWidthSpec(string s, Ast.ExecutionContext? context = null)
    {
        if (s.Length == 0) return false;
        // Valid patterns: "N", "N-M", "N-*", "*-N", "*"
        foreach (var c in s)
        {
            if (c != '-' && c != '*' && !char.IsAsciiDigit(c)) return false;
        }
        return true;
    }

    private static string FormatComponent(DateTimeOffset dt, string spec, bool hasDate, bool hasTime, long? extendedYear = null, bool hasTimezone = true, string effectiveLanguage = "en", TimeZoneInfo? resolvedTz = null, Ast.ExecutionContext? context = null)
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
            throw context.Error("XTDE1340", $"Invalid component '{component}' in date/time picture string");

        // XTDE1350: Component not available in value type
        if (DateComponents.Contains(component) && !hasDate)
            throw context.Error("XTDE1350", $"Date component '{component}' is not available in a time value");
        if (TimeComponents.Contains(component) && !hasTime)
            throw context.Error("XTDE1350", $"Time component '{component}' is not available in a date value");

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
            throw context.Error("FOFD1340", $"Invalid width specifier: minimum width ({minWidth.Value}) exceeds maximum width ({maxWidth.Value})");

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
    private static void ValidatePresentationDigits(string presentation, char component, Ast.ExecutionContext? context = null)
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
                    throw context.Error("FOFD1340", $"Mixed digit families in presentation: '{presentation}'");

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
                throw context.Error("FOFD1340", $"Invalid presentation: mandatory digit after optional digit in '{presentation}'");
            else
                throw context.Error("FOFD1340", $"Invalid presentation: optional digit (#) after mandatory digit in '{presentation}'");
        }

        // For fractional seconds: patterns with only 9 and/or # digits are valid
        // (9 = show digit position but trim trailing zeros, # = omit if not significant)
    }

    private static string FormatYear(int year, string presentation, int? minWidth, int? maxWidth, Ast.ExecutionContext? context = null)
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

    private static string ToAlpha(int value, bool upper, Ast.ExecutionContext? context = null)
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

    private static string GetOrdinalSuffix(int value, Ast.ExecutionContext? context = null)
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

    private static string FormatEra(int year, string presentation, Ast.ExecutionContext? context = null)
    {
        var era = year > 0 ? "AD" : "BC";
        if (presentation.Length > 0 && (presentation[0] == 'N' || presentation[0] == 'n'))
            return ApplyCase(era, presentation);
        return era;
    }

    private static string FormatAmPm(int hour, string presentation, int? minWidth, int? maxWidth, Ast.ExecutionContext? context = null)
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

    private static int ISOWeekOfYear(DateTimeOffset dt, Ast.ExecutionContext? context = null)
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

    private static int GetWeekOfMonth(DateTimeOffset dt, Ast.ExecutionContext? context = null)
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

    private static int GetWeekOfMonthRaw(int year, int month, int day, DayOfWeek dayOfWeek, Ast.ExecutionContext? context = null)
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
    private static string? GetTimezoneAbbreviation(DateTimeOffset dt, TimeSpan offset, Ast.ExecutionContext? context = null)
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

    private static string ApplyCase(string name, string presentation, Ast.ExecutionContext? context = null)
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

    private static string ToRoman(int value, Ast.ExecutionContext? context = null)
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

    private static string NumberToWords(int value, bool ordinal, Ast.ExecutionContext? context = null)
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

    private static string FormatWord(int value, string presentation, bool ordinal, Ast.ExecutionContext? context = null)
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
    internal static DateTimeOffset SafeTimeOffset(TimeOnly time, TimeSpan offset, Ast.ExecutionContext? context = null)
    {
        // Use a date in the middle of the range to avoid overflow with large offsets (e.g. +13:00, -14:00)
        var safeDate = new DateOnly(2000, 1, 1);
        return new DateTimeOffset(safeDate, time, offset);
    }

    internal static DateTimeOffset ExtractDateTimeOffset(Xdm.XsDate xd, out long? extendedYear, Ast.ExecutionContext? context = null)
    {
        extendedYear = xd.ExtendedYear;
        return new DateTimeOffset(xd.Date, TimeOnly.MinValue, xd.Timezone ?? TimeSpan.Zero);
    }

    internal static DateTimeOffset ExtractDateTimeOffset(Xdm.XsDateTime xdt, out long? extendedYear, Ast.ExecutionContext? context = null)
    {
        extendedYear = xdt.ExtendedYear;
        return xdt.Value;
    }
}
