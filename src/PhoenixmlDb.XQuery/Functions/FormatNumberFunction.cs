using System.Globalization;
using System.Numerics;
using System.Text;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:format-number($value, $picture) as xs:string
/// </summary>
public sealed class FormatNumberFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "format-number");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "value"), Type = new XdmSequenceType { ItemType = ItemType.Double, Occurrence = Occurrence.ZeroOrOne } },
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

    internal static string FormatNumberImpl(object? rawValue, string picture, Analysis.DecimalFormatProperties df, Ast.ExecutionContext? context = null)
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
    private static decimal DecimalPow10(int exponent, Ast.ExecutionContext? context = null)
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

    private static string[] SplitSubPictures(string picture, char separator, Ast.ExecutionContext? context = null)
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

    private static bool IsZeroDigit(char c, Analysis.DecimalFormatProperties df, Ast.ExecutionContext? context = null)
    {
        return c >= df.ZeroDigit && c < (char)(df.ZeroDigit + 10);
    }

    private static string GetPrefix(string subPicture, Analysis.DecimalFormatProperties df, Ast.ExecutionContext? context = null)
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

    private static string GetSuffix(string subPicture, Analysis.DecimalFormatProperties df, Ast.ExecutionContext? context = null)
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

    private static string GetBody(string subPicture, Analysis.DecimalFormatProperties df, Ast.ExecutionContext? context = null)
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
        int patternDigitCount, Ast.ExecutionContext? context = null)
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

    private static string ReplaceDigits(string s, Analysis.DecimalFormatProperties df, Ast.ExecutionContext? context = null)
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
    private static string NormalizePictureNonBmp(string picture, int zeroCodePoint, Ast.ExecutionContext? context = null)
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
    private static string DenormalizeResultNonBmp(string result, int zeroCodePoint, Ast.ExecutionContext? context = null)
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
        string? nonBmpDecimalSep, string? nonBmpGroupingSep, Ast.ExecutionContext? context = null)
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
