using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Functions;

namespace PhoenixmlDb.XQuery.FullText;

/// <summary>
/// phx:is-stop-word($word as xs:string) as xs:boolean
/// Tests if a word is a stop word in the default language.
/// </summary>
public sealed class FtIsStopWordFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Phx, "is-stop-word", "phx");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "word"), Type = XdmSequenceType.String }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var word = arguments[0]?.ToString() ?? "";
        // Membership in the default stop-word list. This analyzed the word and called "no tokens"
        // a stop word, through an analyzer with no stop-word filter: every stop word was false
        // and any letterless input ("123", "") was true (#70).
        return ValueTask.FromResult<object?>(FullTextEngine.IsDefaultStopWord(word));
    }
}
