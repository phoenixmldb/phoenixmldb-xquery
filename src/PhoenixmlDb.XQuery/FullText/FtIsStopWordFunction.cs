using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Functions;

namespace PhoenixmlDb.XQuery.FullText;

/// <summary>
/// ft:is-stop-word($word as xs:string) as xs:boolean
/// Tests if a word is a stop word in the default language.
/// </summary>
public sealed class FtIsStopWordFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Ft, "is-stop-word");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "word"), Type = XdmSequenceType.String }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var word = arguments[0]?.ToString() ?? "";
        // Analyze the word — if it produces no tokens, it's a stop word
        var terms = FullTextEngine.Analyze(word, new FullTextAnalysisOptions { Stemming = false });
        return ValueTask.FromResult<object?>(terms.Count == 0);
    }
}
