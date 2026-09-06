using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:graphemes($arg as xs:string?) as xs:string* — splits into grapheme clusters (XPath 4.0).
/// Similar to fn:characters but handles multi-codepoint grapheme clusters correctly.
/// </summary>
public sealed class GraphemesFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "graphemes");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.String, Occurrence = Occurrence.ZeroOrMore };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalString }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var str = arguments[0]?.ToString();
        if (string.IsNullOrEmpty(str)) return ValueTask.FromResult<object?>(Array.Empty<string>());
        var graphemes = new List<string>();
        var enumerator = System.Globalization.StringInfo.GetTextElementEnumerator(str);
        while (enumerator.MoveNext())
            graphemes.Add(enumerator.GetTextElement());
        return ValueTask.FromResult<object?>(graphemes.ToArray());
    }
}
