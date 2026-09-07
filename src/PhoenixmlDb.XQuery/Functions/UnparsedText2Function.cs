using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>fn:unparsed-text($href as xs:string?, $encoding as xs:string) as xs:string?</summary>
public sealed class UnparsedText2Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "unparsed-text");
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalString;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "href"), Type = XdmSequenceType.OptionalString },
         new() { Name = new QName(NamespaceId.None, "encoding"), Type = XdmSequenceType.String }];

    public override async ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var href = arguments[0]?.ToString();
        if (href is null) return null;
        var encodingName = arguments[1]?.ToString() ?? "utf-8";
        System.Text.Encoding encoding;
        try { encoding = System.Text.Encoding.GetEncoding(encodingName); }
        catch (ArgumentException) { throw new XQueryRuntimeException("FOUT1190", $"Unknown encoding: '{encodingName}'"); }
        return await UnparsedTextFunction.ReadUnparsedText(href, encoding, context).ConfigureAwait(false);
    }
}
