using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>fn:contains-token($input, $token, $collation) as xs:boolean</summary>
public sealed class ContainsToken3Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "contains-token");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "input"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "token"), Type = XdmSequenceType.String },
        new() { Name = new QName(NamespaceId.None, "collation"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var collation = arguments[2]?.ToString() ?? "";
        var comparison = collation.Contains("case-insensitive", StringComparison.OrdinalIgnoreCase)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var input = arguments[0];
        var token = arguments[1]?.ToString()?.Trim() ?? "";
        if (string.IsNullOrEmpty(token)) return ValueTask.FromResult<object?>(false);

        var strings = input is object?[] arr
            ? arr.Select(i => i?.ToString() ?? "")
            : new[] { input?.ToString() ?? "" };

        foreach (var str in strings)
        {
            var tokens = str.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Any(t => string.Equals(t.Trim(), token, comparison)))
                return ValueTask.FromResult<object?>(true);
        }
        return ValueTask.FromResult<object?>(false);
    }
}
