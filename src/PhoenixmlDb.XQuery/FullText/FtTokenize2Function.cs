using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Functions;

namespace PhoenixmlDb.XQuery.FullText;

/// <summary>
/// ft:tokenize($text as xs:string?, $language as xs:string) as xs:string*
/// Tokenizes text using a language-specific analyzer.
/// </summary>
public sealed class FtTokenize2Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Ft, "tokenize");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.String, Occurrence = Occurrence.ZeroOrMore };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "text"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "language"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var text = arguments[0]?.ToString();
        var language = arguments[1]?.ToString();
        if (string.IsNullOrEmpty(text)) return ValueTask.FromResult<object?>(Array.Empty<string>());

        var options = new FullTextAnalysisOptions { Language = language, Stemming = true };
        var terms = FullTextEngine.Analyze(text, options);
        var result = terms.Select(t => t.Text).ToArray();
        return ValueTask.FromResult<object?>(result);
    }
}
