using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:codepoint($char as xs:string) as xs:integer — returns Unicode codepoint (XPath 4.0).
/// </summary>
public sealed class CodepointFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "codepoint");
    public override XdmSequenceType ReturnType => XdmSequenceType.Integer;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "char"), Type = XdmSequenceType.String }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var str = arguments[0]?.ToString() ?? "";
        if (str.Length == 0)
            throw new InvalidOperationException("FOCH0004: fn:codepoint requires a single character string");
        var cp = char.ConvertToUtf32(str, 0);
        return ValueTask.FromResult<object?>((long)cp);
    }
}
