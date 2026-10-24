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
        // $encoding is an xs:string: another atomic type fails the function conversion rules
        // (XPTY0004), rather than being read as an encoding name (QT3 fn-unparsed-text-010).
        if (arguments[1] is { } encodingArg && StringArgument.AsString(encodingArg) is null)
            throw new XQueryRuntimeException("XPTY0004",
                $"The encoding argument of fn:unparsed-text must be an xs:string, not {encodingArg.GetType().Name}");
        var encodingName = arguments[1] is null ? "utf-8" : StringArgument.AsString(arguments[1])!;
        System.Text.Encoding encoding;
        try { encoding = System.Text.Encoding.GetEncoding(encodingName); }
        // NotSupportedException: an encoding .NET knows by name and refuses to provide (utf-7).
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            throw new XQueryRuntimeException("FOUT1190", $"Unknown encoding: '{encodingName}'");
        }
        return await UnparsedTextFunction.ReadUnparsedText(href, encoding, context).ConfigureAwait(false);
    }
}
