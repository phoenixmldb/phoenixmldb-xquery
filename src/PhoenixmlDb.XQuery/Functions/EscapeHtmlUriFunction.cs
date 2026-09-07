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
        if (arg is not null && arg is not string && arg is not Xdm.XsUntypedAtomic && arg is not Xdm.XsAnyUri)
            throw new Execution.XQueryRuntimeException("XPTY0004",
                $"Expected xs:string argument for fn:escape-html-uri, got {arg.GetType().Name}");
        var str = arg?.ToString() ?? "";
        // escape-html-uri: only escape non-ASCII characters and characters not valid in URIs
        var sb = new System.Text.StringBuilder(str.Length);
        foreach (var ch in str)
        {
            if (ch >= 0x20 && ch <= 0x7E)
                sb.Append(ch);
            else
            {
                foreach (var b in System.Text.Encoding.UTF8.GetBytes(new[] { ch }))
                    sb.Append($"%{b:X2}");
            }
        }
        return ValueTask.FromResult<object?>(sb.ToString());
    }
}
