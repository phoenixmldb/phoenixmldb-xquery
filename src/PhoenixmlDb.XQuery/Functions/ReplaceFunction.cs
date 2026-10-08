using System.Collections.Concurrent;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:replace($input, $pattern, $replacement) as xs:string
/// </summary>
public sealed class ReplaceFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "replace");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "input"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "pattern"), Type = XdmSequenceType.String },
        new() { Name = new QName(NamespaceId.None, "replacement"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var input = arguments[0]?.ToString() ?? "";
        if (arguments[1] is null)
            throw context.Error("XPTY0004", "Pattern argument to fn:replace cannot be an empty sequence");
        if (arguments[2] is null)
            throw context.Error("XPTY0004", "Replacement argument to fn:replace cannot be an empty sequence");
        var pattern = arguments[1]!.ToString() ?? "";
        var replacement = arguments[2]!.ToString() ?? "";
        try
        {
            XQueryRegexHelper.ThrowIfCancelled(context);
            XQueryRegexHelper.ValidateXsdRegex(pattern);
            var netPattern = XQueryRegexHelper.ConvertXPathPatternToNet(pattern);
            netPattern = XQueryRegexHelper.ConvertXsdEscapesToNet(netPattern);
            netPattern = XQueryRegexHelper.FixDollarAnchor(netPattern);
            netPattern = XQueryRegexHelper.FixDotForSurrogatePairs(netPattern);
            var regex = XQueryRegexHelper.CreateRegex(netPattern, System.Text.RegularExpressions.RegexOptions.None, XQueryRegexHelper.MatchTimeoutOf(context));
            // FORX0003: pattern must not match empty string
            if (regex.IsMatch(""))
                throw context.Error("FORX0003",
                    "Pattern matches a zero-length string in fn:replace");
            var netReplacement = XQueryRegexHelper.ConvertXPathReplacementToNet(replacement, pattern);
            return ValueTask.FromResult<object?>(regex.Replace(input, netReplacement));
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException ex)
        {
            throw XQueryRegexHelper.MatchTimedOut(context, ex);
        }
        catch (XQueryException) { throw; }
        catch (InvalidOperationException) { throw; }
        catch (ArgumentException ex)
        {
            throw context.Error("FORX0002",
                $"Invalid regular expression: {ex.Message}");
        }
    }
}
