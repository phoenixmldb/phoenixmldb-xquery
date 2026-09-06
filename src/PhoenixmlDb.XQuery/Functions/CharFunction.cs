using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:char($name as xs:string) as xs:string — returns character by Unicode name or hex (XPath 4.0).
/// Accepts: hex codepoint (e.g., "A0"), Unicode name (e.g., "NO-BREAK SPACE"), or HTML entity name.
/// </summary>
public sealed class CharFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "char");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "name"), Type = XdmSequenceType.String }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var name = arguments[0]?.ToString() ?? "";

        // Try as hex codepoint first (e.g., "A0", "2019", "1F600")
        if (int.TryParse(name, System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture, out var codepoint))
        {
            return ValueTask.FromResult<object?>(char.ConvertFromUtf32(codepoint));
        }

        // Try common named characters
        var result = name.ToUpperInvariant() switch
        {
            "TAB" or "CHARACTER TABULATION" => "\t",
            "NEWLINE" or "LINE FEED" or "LF" => "\n",
            "CARRIAGE RETURN" or "CR" => "\r",
            "SPACE" => " ",
            "NO-BREAK SPACE" or "NBSP" => "\u00A0",
            "ZERO WIDTH SPACE" => "\u200B",
            "ZERO WIDTH NON-JOINER" or "ZWNJ" => "\u200C",
            "ZERO WIDTH JOINER" or "ZWJ" => "\u200D",
            "SOFT HYPHEN" or "SHY" => "\u00AD",
            "EN DASH" => "\u2013",
            "EM DASH" => "\u2014",
            "LEFT SINGLE QUOTATION MARK" => "\u2018",
            "RIGHT SINGLE QUOTATION MARK" => "\u2019",
            "LEFT DOUBLE QUOTATION MARK" => "\u201C",
            "RIGHT DOUBLE QUOTATION MARK" => "\u201D",
            "BULLET" => "\u2022",
            "HORIZONTAL ELLIPSIS" => "\u2026",
            "EURO SIGN" => "\u20AC",
            "COPYRIGHT SIGN" => "\u00A9",
            "REGISTERED SIGN" => "\u00AE",
            "TRADE MARK SIGN" => "\u2122",
            "DEGREE SIGN" => "\u00B0",
            "MULTIPLICATION SIGN" => "\u00D7",
            "DIVISION SIGN" => "\u00F7",
            _ => null
        };

        if (result != null)
            return ValueTask.FromResult<object?>(result);

        throw new InvalidOperationException($"FOCH0005: Unknown character name '{name}'");
    }
}
