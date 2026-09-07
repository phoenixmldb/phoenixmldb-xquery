using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>fn:unparsed-text-lines($href as xs:string?, $encoding as xs:string) as xs:string*</summary>
public sealed class UnparsedTextLines2Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "unparsed-text-lines");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "href"), Type = XdmSequenceType.OptionalString },
         new() { Name = new QName(NamespaceId.None, "encoding"), Type = XdmSequenceType.String }];

    public override async ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var text = await new UnparsedText2Function().InvokeAsync(arguments, context).ConfigureAwait(false);
        if (text is not string s) return Array.Empty<object>();
        return UnparsedTextLinesFunction.SplitLines(s);
    }
}
