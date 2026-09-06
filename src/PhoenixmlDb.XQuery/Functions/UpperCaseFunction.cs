using System.Collections.Concurrent;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:upper-case($arg) as xs:string
/// </summary>
public sealed class UpperCaseFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "upper-case");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalString }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var nodeProvider = (context as Execution.QueryExecutionContext)?.NodeProvider;
        var atomized = Execution.QueryExecutionContext.Atomize(arguments[0], nodeProvider);
        if (atomized is null) return ValueTask.FromResult<object?>(string.Empty);
        StringLengthFunction.RequireStringLike(atomized, "upper-case");
        var str = ConcatFunction.XQueryStringValue(atomized);
        return ValueTask.FromResult<object?>(FullUnicodeUpperCase(str));
    }

    /// <summary>
    /// Full Unicode upper-case mapping per Unicode SpecialCasing.txt.
    /// .NET's ToUpperInvariant() uses simple (1-to-1) case mapping, but the XQuery spec
    /// requires full case mapping where certain characters expand to multiple codepoints
    /// (e.g. ß → SS, Armenian ligatures → decomposed pairs).
    /// </summary>
    internal static string FullUnicodeUpperCase(string input, Ast.ExecutionContext? context = null)
    {
        // Fast path: if no special characters, just use ToUpperInvariant
        if (!ContainsSpecialUpperCaseChar(input))
            return input.ToUpperInvariant();

        var sb = new System.Text.StringBuilder(input.Length + 4);
        for (int i = 0; i < input.Length; i++)
        {
            int cp = char.ConvertToUtf32(input, i);
            if (char.IsHighSurrogate(input[i])) i++;

            if (s_specialUpperCase.TryGetValue(cp, out var replacement))
            {
                foreach (var rcp in replacement)
                    sb.Append(char.ConvertFromUtf32(rcp));
            }
            else
            {
                sb.Append(char.ConvertFromUtf32(cp).ToUpperInvariant());
            }
        }
        return sb.ToString();
    }

    private static bool ContainsSpecialUpperCaseChar(string s, Ast.ExecutionContext? context = null)
    {
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            // Quick check for the known special casing ranges
            if (c == 0x00DF || c == 0x0149 || c == 0x01F0 ||
                c == 0x0390 || c == 0x03B0 || c == 0x0587 ||
                (c >= 0xFB00 && c <= 0xFB06) || (c >= 0xFB13 && c <= 0xFB17))
                return true;
        }
        return false;
    }

    /// <summary>Unconditional full upper-case mappings from Unicode SpecialCasing.txt</summary>
    private static readonly Dictionary<int, int[]> s_specialUpperCase = new()
    {
        { 0x00DF, [0x0053, 0x0053] },                  // ß → SS
        { 0x0149, [0x02BC, 0x004E] },                  // ŉ → ʼN
        { 0x01F0, [0x004A, 0x030C] },                  // ǰ → J̌
        { 0x0390, [0x0399, 0x0308, 0x0301] },          // ΐ → Ϊ́
        { 0x03B0, [0x03A5, 0x0308, 0x0301] },          // ΰ → Ϋ́
        { 0x0587, [0x0535, 0x0552] },                   // և → ԵՒ
        { 0xFB00, [0x0046, 0x0046] },                   // ﬀ → FF
        { 0xFB01, [0x0046, 0x0049] },                   // ﬁ → FI
        { 0xFB02, [0x0046, 0x004C] },                   // ﬂ → FL
        { 0xFB03, [0x0046, 0x0046, 0x0049] },           // ﬃ → FFI
        { 0xFB04, [0x0046, 0x0046, 0x004C] },           // ﬄ → FFL
        { 0xFB05, [0x0053, 0x0054] },                   // ﬅ → ST
        { 0xFB06, [0x0053, 0x0054] },                   // ﬆ → ST
        { 0xFB13, [0x0544, 0x0546] },                   // ﬓ → ՄՆ
        { 0xFB14, [0x0544, 0x0535] },                   // ﬔ → ՄԵ
        { 0xFB15, [0x0544, 0x053B] },                   // ﬕ → ՄԻ
        { 0xFB16, [0x054E, 0x0546] },                   // ﬖ → ՎՆ
        { 0xFB17, [0x0544, 0x053D] },                   // ﬗ → ՄԽ
    };
}
