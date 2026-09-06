using System.Collections.Concurrent;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:lower-case($arg) as xs:string
/// </summary>
public sealed class LowerCaseFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "lower-case");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalString }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var nodeProvider = (context as Execution.QueryExecutionContext)?.NodeProvider;
        var str = ConcatFunction.XQueryStringValue(arguments[0], nodeProvider);
        return ValueTask.FromResult<object?>(FullUnicodeLowerCase(str));
    }

    /// <summary>
    /// Full Unicode lower-case mapping per Unicode SpecialCasing.txt.
    /// Handles characters like U+0130 (I with dot above) that expand to multiple codepoints.
    /// </summary>
    internal static string FullUnicodeLowerCase(string input, Ast.ExecutionContext? context = null)
    {
        if (!ContainsSpecialLowerCaseChar(input))
            return input.ToLowerInvariant();

        var sb = new System.Text.StringBuilder(input.Length + 4);
        for (int i = 0; i < input.Length; i++)
        {
            int cp = char.ConvertToUtf32(input, i);
            if (char.IsHighSurrogate(input[i])) i++;

            if (s_specialLowerCase.TryGetValue(cp, out var replacement))
            {
                foreach (var rcp in replacement)
                    sb.Append(char.ConvertFromUtf32(rcp));
            }
            else
            {
                sb.Append(char.ConvertFromUtf32(cp).ToLowerInvariant());
            }
        }
        return sb.ToString();
    }

    private static bool ContainsSpecialLowerCaseChar(string s, Ast.ExecutionContext? context = null)
    {
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == 0x0130) return true;
        }
        return false;
    }

    /// <summary>Unconditional full lower-case mappings from Unicode SpecialCasing.txt</summary>
    private static readonly Dictionary<int, int[]> s_specialLowerCase = new()
    {
        { 0x0130, [0x0069, 0x0307] },  // İ → i + combining dot above
    };
}
