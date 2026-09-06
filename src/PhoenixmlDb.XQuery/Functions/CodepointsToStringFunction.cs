using System.Collections.Concurrent;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:codepoints-to-string($arg) as xs:string
/// </summary>
public sealed class CodepointsToStringFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "codepoints-to-string");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = new() { ItemType = ItemType.Integer, Occurrence = Occurrence.ZeroOrMore } }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg == null) return ValueTask.FromResult<object?>("");
        var codepoints = arg is IEnumerable<object?> seq
            ? seq.Select(x => QueryExecutionContext.ToInt(x))
            : [QueryExecutionContext.ToInt(arg)];
        // Use StringBuilder to avoid allocating intermediate string objects per codepoint
        var sb = new System.Text.StringBuilder();
        foreach (var cp in codepoints)
        {
            // XML 1.0 Char: #x9 | #xA | #xD | [#x20-#xD7FF] | [#xE000-#xFFFD] | [#x10000-#x10FFFF]
            var valid = cp == 0x9 || cp == 0xA || cp == 0xD
                || (cp >= 0x20 && cp <= 0xD7FF)
                || (cp >= 0xE000 && cp <= 0xFFFD)
                || (cp >= 0x10000 && cp <= 0x10FFFF);
            if (!valid)
                throw new XQueryRuntimeException("FOCH0001",
                    $"Invalid XML character codepoint: {cp}");
            sb.Append(char.ConvertFromUtf32(cp));
        }
        return ValueTask.FromResult<object?>(sb.ToString());
    }
}
