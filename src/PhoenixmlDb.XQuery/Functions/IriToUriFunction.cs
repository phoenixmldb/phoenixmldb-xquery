using System.Collections.Concurrent;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:iri-to-uri($arg) as xs:string
/// </summary>
public sealed class IriToUriFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "iri-to-uri");
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
                $"Expected xs:string argument for fn:iri-to-uri, got {arg.GetType().Name}");
        var sb = new System.Text.StringBuilder(str.Length);
        // By code point, not UTF-16 unit, so a surrogate pair encodes as one character.
        Span<byte> utf8 = stackalloc byte[4];
        foreach (var rune in str.EnumerateRunes())
        {
            // IRI-to-URI: percent-encode characters outside of the URI character set
            if (rune.Value <= 0x20 || rune.Value >= 0x7F || "<>\"{}|\\^`".Contains((char)rune.Value))
            {
                var length = rune.EncodeToUtf8(utf8);
                foreach (var b in utf8[..length])
                    sb.Append($"%{b:X2}");
            }
            else
            {
                sb.Append((char)rune.Value);
            }
        }
        return ValueTask.FromResult<object?>(sb.ToString());
    }
}
