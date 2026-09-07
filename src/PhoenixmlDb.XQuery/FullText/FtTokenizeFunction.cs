using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Functions;

namespace PhoenixmlDb.XQuery.FullText;

/// <summary>
/// ft:tokenize($text as xs:string?) as xs:string*
/// Tokenizes text using the default full-text analyzer.
/// Useful for debugging and understanding how text is analyzed.
/// </summary>
public sealed class FtTokenizeFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Ft, "tokenize");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.String, Occurrence = Occurrence.ZeroOrMore };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "text"), Type = XdmSequenceType.OptionalString }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var text = arguments[0]?.ToString();
        if (string.IsNullOrEmpty(text)) return ValueTask.FromResult<object?>(Array.Empty<string>());

        var terms = FullTextEngine.Analyze(text);
        var result = terms.Select(t => t.Text).ToArray();
        return ValueTask.FromResult<object?>(result);
    }
}
