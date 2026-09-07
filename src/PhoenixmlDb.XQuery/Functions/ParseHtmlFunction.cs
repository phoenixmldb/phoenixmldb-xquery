using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:parse-html($html as xs:string?) as document-node()? — parses HTML into an XDM tree (XPath 4.0).
/// Uses .NET's XmlDocument with a best-effort HTML-to-XML approach.
/// </summary>
public sealed class ParseHtmlFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "parse-html");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Document, Occurrence = Occurrence.ZeroOrOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "html"), Type = XdmSequenceType.OptionalString }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var html = arguments[0]?.ToString();
        if (string.IsNullOrEmpty(html)) return ValueTask.FromResult<object?>(null);

        try
        {
            // Best-effort HTML parsing: wrap in root if needed, try as XML
            var normalized = html.Trim();
            if (!normalized.StartsWith('<'))
                normalized = $"<html><body>{normalized}</body></html>";

            // Try parsing as well-formed XML first
            var doc = new System.Xml.XmlDocument();
            doc.PreserveWhitespace = true;
            try
            {
                doc.LoadXml(normalized);
            }
            catch (System.Xml.XmlException)
            {
                // The input is HTML that is not well-formed XML — implied end tags (<p> closing
                // a previous <p>), void elements (<br>), implicit html/head/body. Parsing that
                // needs an HTML5 tokenizer and tree builder, which this engine does not have and
                // .NET does not provide.
                //
                // It previously ESCAPED the whole input into <html><body>, so
                //
                //     parse-html("<p>This is a line.<br>This is a line.<p>…")
                //
                // returned a document whose body was the literal source text. That is not a
                // parse, and worse it is a SILENT one: callers received a plausible document and
                // no indication anything had gone wrong. Reported by Martin Honnen 2026-08-22,
                // against Saxon's correct
                // <html><head/><body><p>This is a line.<br/>…</p><p>…</p></body></html>.
                //
                // Failing loudly is not a fix, but it is honest, and it is strictly better than
                // returning a wrong answer that looks right. FODC0006 is the code for input that
                // cannot be parsed into the required form.
                throw new Execution.XQueryRuntimeException("FODC0006",
                    "fn:parse-html: HTML that is not well-formed XML is not supported by this " +
                    "engine — no HTML5 tokenizer is available. Well-formed XHTML input parses " +
                    "normally. Pre-parse the markup, or use fn:parse-xml on well-formed input.");
            }

            // Return the parsed document as a LINQ XDocument for downstream processing
            return ValueTask.FromResult<object?>(
                System.Xml.Linq.XDocument.Parse(doc.OuterXml));
        }
        catch (System.Xml.XmlException)
        {
            // Completely unparseable HTML — return null
            return ValueTask.FromResult<object?>(null);
        }
    }
}
