using System.Collections.Concurrent;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:tokenize($input, $pattern) as xs:string*
/// </summary>
public sealed class TokenizeFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "tokenize");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.String, Occurrence = Occurrence.ZeroOrMore };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "input"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "pattern"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var nodeProvider = (context as Execution.QueryExecutionContext)?.NodeProvider;
        var input = ConcatFunction.XQueryStringValue(Execution.QueryExecutionContext.Atomize(arguments[0], nodeProvider));
        var pattern = arguments[1]?.ToString() ?? "";

        // Per XPath spec: if $input is the zero-length string, return empty sequence
        if (input.Length == 0)
            return ValueTask.FromResult<object?>(Array.Empty<string>());

        try
        {
            var regex = RegexCache.GetOrCreate(pattern);
            // FORX0003: pattern must not match empty string
            if (regex.IsMatch(""))
                throw new InvalidOperationException("FORX0003: The supplied pattern matches a zero-length string");
            var tokens = TokenizeSplit(regex, input);
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

    /// <summary>
    /// Split input by regex matches without including captured group values
    /// (unlike .NET's Regex.Split which includes captured groups in results).
    /// </summary>
    internal static string[] TokenizeSplit(System.Text.RegularExpressions.Regex regex, string input, Ast.ExecutionContext? context = null)
    {
        var tokens = new List<string>();
        int lastEnd = 0;
        foreach (System.Text.RegularExpressions.Match m in regex.Matches(input))
        {
            tokens.Add(input[lastEnd..m.Index]);
            lastEnd = m.Index + m.Length;
        }
        tokens.Add(input[lastEnd..]);
        return tokens.ToArray();
    }
}
