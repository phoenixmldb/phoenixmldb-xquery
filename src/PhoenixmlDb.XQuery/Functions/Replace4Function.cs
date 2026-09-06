using System.Collections.Concurrent;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:replace($input, $pattern, $replacement, $flags) as xs:string
/// </summary>
public sealed class Replace4Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "replace");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "input"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "pattern"), Type = XdmSequenceType.String },
        new() { Name = new QName(NamespaceId.None, "replacement"), Type = XdmSequenceType.String },
        new() { Name = new QName(NamespaceId.None, "flags"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var input = arguments[0]?.ToString() ?? "";
        var pattern = arguments[1]?.ToString() ?? "";
        var replacement = arguments[2]?.ToString() ?? "";
        var flags = arguments[3]?.ToString() ?? "";
        try
        {
            var isLiteral = flags.Contains('q', StringComparison.Ordinal);
            // XPath 'x' flag: strip whitespace from pattern before any processing
            if (!isLiteral && flags.Contains('x', StringComparison.Ordinal))
                pattern = XQueryRegexHelper.StripXModeWhitespace(pattern);
            if (!isLiteral)
                XQueryRegexHelper.ValidateXsdRegex(pattern);
            string netReplacement;
            string netPattern;
            if (isLiteral)
            {
                // 'q' flag: pattern is literal, replacement is literal (no $N or \ escapes)
                netPattern = System.Text.RegularExpressions.Regex.Escape(pattern);
                netReplacement = replacement.Replace("$", "$$", StringComparison.Ordinal);
            }
            else
            {
                netPattern = XQueryRegexHelper.ConvertXPathPatternToNet(pattern);
                netReplacement = XQueryRegexHelper.ConvertXPathReplacementToNet(replacement, pattern);
            }
            netPattern = XQueryRegexHelper.ConvertXsdEscapesToNet(netPattern);
            if (flags.Contains('i', StringComparison.Ordinal))
                netPattern = XQueryRegexHelper.FixPropertyEscapesForCaseInsensitive(netPattern);
            if (!flags.Contains('m', StringComparison.Ordinal))
                netPattern = XQueryRegexHelper.FixDollarAnchor(netPattern);
            else
                netPattern = XQueryRegexHelper.FixCaretAnchorMultiline(netPattern);
            netPattern = XQueryRegexHelper.FixDotForSurrogatePairs(netPattern,
                flags.Contains('s', StringComparison.Ordinal));
            var options = XQueryRegexHelper.ParseFlags(flags);
            var regex = new System.Text.RegularExpressions.Regex(netPattern, options);
            // FORX0003: pattern must not match empty string
            if (!isLiteral && regex.IsMatch(""))
                throw context.Error("FORX0003",
                    "Pattern matches a zero-length string in fn:replace");
            return ValueTask.FromResult<object?>(regex.Replace(input, netReplacement));
        }
        catch (InvalidOperationException)
        {
            throw; // FORX0004 errors
        }
        catch (ArgumentException ex)
        {
            throw context.Error("FORX0002",
                $"Invalid regular expression: {ex.Message}");
        }
    }
}
