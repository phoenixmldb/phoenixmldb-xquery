using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:contains-token($input as xs:string*, $token as xs:string) as xs:boolean
/// Tests if whitespace-separated tokens contain the given token (XPath 3.1/4.0).
/// </summary>
public sealed class ContainsTokenFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "contains-token");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "input"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "token"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var input = arguments[0];
        var token = arguments[1]?.ToString()?.Trim() ?? "";
        if (string.IsNullOrEmpty(token)) return ValueTask.FromResult<object?>(false);

        var strings = input is object?[] arr
            ? arr.Select(i => i?.ToString() ?? "")
            : new[] { input?.ToString() ?? "" };

        foreach (var str in strings)
        {
            var tokens = str.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Any(t => string.Equals(t.Trim(), token, StringComparison.Ordinal)))
                return ValueTask.FromResult<object?>(true);
        }
        return ValueTask.FromResult<object?>(false);
    }
}
