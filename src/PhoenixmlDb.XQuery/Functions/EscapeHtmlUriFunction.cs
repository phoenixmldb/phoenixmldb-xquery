using System.Collections.Concurrent;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:escape-html-uri($arg) as xs:string
/// </summary>
public sealed class EscapeHtmlUriFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "escape-html-uri");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalString }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        var str = arg is null ? "" : StringArgument.AsString(arg)
            ?? throw new Execution.XQueryRuntimeException("XPTY0004",
                $"Expected xs:string argument for fn:escape-html-uri, got {arg.GetType().Name}");
        // escape-html-uri: only escape non-ASCII characters and characters not valid in URIs
        var sb = new System.Text.StringBuilder(str.Length);
        // By code point, not UTF-16 unit: each half of a surrogate pair encoded alone gave
        // %EF%BF%BD twice instead of the four UTF-8 bytes of the character (W3C expression-1601).
        Span<byte> utf8 = stackalloc byte[4];
        foreach (var rune in str.EnumerateRunes())
        {
            if (rune.Value >= 0x20 && rune.Value <= 0x7E)
                sb.Append((char)rune.Value);
            else
            {
                var length = rune.EncodeToUtf8(utf8);
                foreach (var b in utf8[..length])
                    sb.Append($"%{b:X2}");
            }
        }
        return ValueTask.FromResult<object?>(sb.ToString());
    }
}
