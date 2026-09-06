using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>fn:unparsed-text-lines($href as xs:string?) as xs:string*</summary>
public sealed class UnparsedTextLinesFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "unparsed-text-lines");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "href"), Type = XdmSequenceType.OptionalString }];

    public override async ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var text = await new UnparsedTextFunction().InvokeAsync(arguments, context).ConfigureAwait(false);
        if (text is not string s) return Array.Empty<object>();
        return SplitLines(s);
    }

    /// <summary>
    /// Split text into lines per XQuery spec. Line endings per XML 1.0 Section 2.11:
    /// #xD#xA (CRLF), #xD (CR alone), #xA (LF).
    /// The trailing empty string after the last line ending is not included.
    /// </summary>
    internal static object?[] SplitLines(string text, Ast.ExecutionContext? context = null)
    {
        var lines = new List<string>();
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r')
            {
                lines.Add(text[start..i]);
                // CR followed by LF is a single line ending
                if (i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
                start = i + 1;
            }
            else if (c == '\n')
            {
                lines.Add(text[start..i]);
                start = i + 1;
            }
        }
        // Add the remaining text after the last line ending
        var trailing = text[start..];
        if (trailing.Length > 0)
            lines.Add(trailing);
        return lines.Select(l => (object?)l).ToArray();
    }
}
