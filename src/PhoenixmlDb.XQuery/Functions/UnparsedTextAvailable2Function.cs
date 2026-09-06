using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>fn:unparsed-text-available($href as xs:string?, $encoding as xs:string) as xs:boolean</summary>
public sealed class UnparsedTextAvailable2Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "unparsed-text-available");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "href"), Type = XdmSequenceType.OptionalString },
         new() { Name = new QName(NamespaceId.None, "encoding"), Type = XdmSequenceType.String }];

    public override async ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        UnparsedTextAvailableFunction.RequireStringArgument(arguments[0], "href");
        UnparsedTextAvailableFunction.RequireStringArgument(arguments[1], "encoding", allowEmpty: false);
        var href = arguments[0]?.ToString();
        if (href is null) return false;
        var encodingName = arguments[1]?.ToString() ?? "utf-8";
        System.Text.Encoding encoding;
        try { encoding = System.Text.Encoding.GetEncoding(encodingName); }
        catch (ArgumentException) { return false; }
        try
        {
            await UnparsedTextFunction.ReadUnparsedText(href, encoding, context).ConfigureAwait(false);
            return true;
        }
        catch (XQueryRuntimeException)
        {
            return false;
        }
    }
}
