using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:parse-json($json-text as xs:string, $options as map(*)) as item()?
/// </summary>
public sealed class ParseJson2Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "parse-json");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Item, Occurrence = Occurrence.ZeroOrOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "json-text"), Type = XdmSequenceType.String },
        new() { Name = new QName(NamespaceId.None, "options"), Type = XdmSequenceType.Item }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var opts = ParseJsonOptions.FromMap(arguments[1]);
        return ParseJsonFunction.ParseJsonCore(arguments[0]?.ToString(), opts, context);
    }
}
