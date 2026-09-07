using System.Collections.Concurrent;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:matches($input, $pattern) as xs:boolean
/// </summary>
public sealed class MatchesFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "matches");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "input"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "pattern"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var input = arguments[0]?.ToString() ?? "";
        if (arguments[1] is null)
            throw context.Error("XPTY0004", "Pattern argument to fn:matches cannot be an empty sequence");
        var pattern = arguments[1]!.ToString() ?? "";
        try
        {
            var regex = RegexCache.GetOrCreate(pattern);
            return ValueTask.FromResult<object?>(regex.IsMatch(input));
        }
        catch (System.Text.RegularExpressions.RegexParseException ex)
        {
            throw context.Error("FORX0002", $"Invalid regular expression '{pattern}': {ex.Message}");
        }
    }
}
