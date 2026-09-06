using System.Collections.Concurrent;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:matches($input, $pattern, $flags) as xs:boolean
/// </summary>
public sealed class Matches3Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "matches");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "input"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "pattern"), Type = XdmSequenceType.String },
        new() { Name = new QName(NamespaceId.None, "flags"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var input = arguments[0]?.ToString() ?? "";
        if (arguments[1] is null)
            throw context.Error("XPTY0004", "Pattern argument to fn:matches cannot be an empty sequence");
        if (arguments[2] is null)
            throw context.Error("XPTY0004", "Flags argument to fn:matches cannot be an empty sequence");
        var pattern = arguments[1]!.ToString() ?? "";
        var flags = arguments[2]!.ToString() ?? "";
        try
        {
            var isLiteral = flags.Contains('q', StringComparison.Ordinal);
            // XPath 'x' flag: strip whitespace from pattern before any processing
            if (!isLiteral && flags.Contains('x', StringComparison.Ordinal))
                pattern = XQueryRegexHelper.StripXModeWhitespace(pattern);
            if (!isLiteral)
                XQueryRegexHelper.ValidateXsdRegex(pattern);
            var netPattern = isLiteral
                ? System.Text.RegularExpressions.Regex.Escape(pattern)
                : XQueryRegexHelper.ConvertXPathPatternToNet(pattern);
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
            return ValueTask.FromResult<object?>(regex.IsMatch(input));
        }
        catch (System.Text.RegularExpressions.RegexParseException ex)
        {
            throw new InvalidOperationException($"FORX0002: Invalid regular expression '{pattern}': {ex.Message}", ex);
        }
    }
}
