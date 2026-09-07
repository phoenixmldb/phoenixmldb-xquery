using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:characters($arg as xs:string?) as xs:string* — splits string into characters (XPath 4.0).
/// </summary>
public sealed class CharactersFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "characters");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.String, Occurrence = Occurrence.ZeroOrMore };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalString }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var str = arguments[0]?.ToString();
        if (string.IsNullOrEmpty(str)) return ValueTask.FromResult<object?>(Array.Empty<string>());
        // Use StringInfo to handle surrogate pairs correctly
        var chars = new List<string>();
        var enumerator = System.Globalization.StringInfo.GetTextElementEnumerator(str);
        while (enumerator.MoveNext())
            chars.Add(enumerator.GetTextElement());
        return ValueTask.FromResult<object?>(chars.ToArray());
    }
}
