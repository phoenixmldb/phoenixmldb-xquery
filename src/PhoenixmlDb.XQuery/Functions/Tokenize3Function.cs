using System.Collections.Concurrent;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:tokenize($input, $pattern, $flags) as xs:string*
/// </summary>
public sealed class Tokenize3Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "tokenize");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.String, Occurrence = Occurrence.ZeroOrMore };
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
        var nodeProvider = (context as Execution.QueryExecutionContext)?.NodeProvider;
        var input = ConcatFunction.XQueryStringValue(Execution.QueryExecutionContext.Atomize(arguments[0], nodeProvider));
        var pattern = arguments[1]?.ToString() ?? "";
        var flags = arguments[2]?.ToString() ?? "";

        if (input.Length == 0)
            return ValueTask.FromResult<object?>(Array.Empty<string>());

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
            // FORX0003: pattern must not match empty string
            if (regex.IsMatch(""))
                throw new InvalidOperationException("FORX0003: The supplied pattern matches a zero-length string");
            var tokens = TokenizeFunction.TokenizeSplit(regex, input);
            return ValueTask.FromResult<object?>(tokens);
        }
        catch (InvalidOperationException)
        {
            throw; // FORX0003 errors
        }
        catch (ArgumentException ex)
        {
            throw context.Error("FORX0002",
                $"Invalid regular expression: {ex.Message}");
        }
    }
}
