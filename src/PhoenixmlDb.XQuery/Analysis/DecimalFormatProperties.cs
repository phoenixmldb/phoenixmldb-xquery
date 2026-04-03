namespace PhoenixmlDb.XQuery.Analysis;

/// <summary>
/// Properties for a decimal format, as specified by declare decimal-format in XQuery prolog
/// or the format-number function in XPath/XQuery.
/// See XPath F&amp;O §4.7.1: Decimal format declarations.
/// </summary>
public sealed class DecimalFormatProperties
{
    public char DecimalSeparator { get; set; } = '.';
    public char GroupingSeparator { get; set; } = ',';
    public string Infinity { get; set; } = "Infinity";
    public char MinusSign { get; set; } = '-';
    public string NaN { get; set; } = "NaN";
    public char Percent { get; set; } = '%';
    public char PerMille { get; set; } = '\u2030';
    public char ZeroDigit { get; set; } = '0';
    public char Digit { get; set; } = '#';
    public char PatternSeparator { get; set; } = ';';
    public char ExponentSeparator { get; set; } = 'e';

    /// <summary>
    /// Default decimal format with all XPath default property values.
    /// </summary>
    public static DecimalFormatProperties Default { get; } = new();

    /// <summary>
    /// Creates a DecimalFormatProperties from a dictionary of property name→value pairs.
    /// </summary>
    public static DecimalFormatProperties FromDictionary(IDictionary<string, string> props)
    {
        var result = new DecimalFormatProperties();
        foreach (var (key, value) in props)
        {
            switch (key)
            {
                case "decimal-separator" when value.Length == 1:
                    result.DecimalSeparator = value[0];
                    break;
                case "grouping-separator" when value.Length == 1:
                    result.GroupingSeparator = value[0];
                    break;
                case "infinity":
                    result.Infinity = value;
                    break;
                case "minus-sign" when value.Length == 1:
                    result.MinusSign = value[0];
                    break;
                case "NaN":
                    result.NaN = value;
                    break;
                case "percent" when value.Length == 1:
                    result.Percent = value[0];
                    break;
                case "per-mille" when value.Length == 1:
                    result.PerMille = value[0];
                    break;
                case "zero-digit" when value.Length == 1:
                    result.ZeroDigit = value[0];
                    break;
                case "digit" when value.Length == 1:
                    result.Digit = value[0];
                    break;
                case "pattern-separator" when value.Length == 1:
                    result.PatternSeparator = value[0];
                    break;
                case "exponent-separator" when value.Length == 1:
                    result.ExponentSeparator = value[0];
                    break;
            }
        }
        return result;
    }
}
