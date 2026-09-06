using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Functions;

namespace PhoenixmlDb.XQuery.FullText;

/// <summary>
/// ft:stem($term as xs:string) as xs:string
/// Returns the stemmed form of a word using the default analyzer.
/// </summary>
public sealed class FtStemFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Ft, "stem");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "term"), Type = XdmSequenceType.String }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var term = arguments[0]?.ToString() ?? "";
        var terms = FullTextEngine.Analyze(term);
        return ValueTask.FromResult<object?>(terms.Count > 0 ? terms[0].Text : term);
    }
}
