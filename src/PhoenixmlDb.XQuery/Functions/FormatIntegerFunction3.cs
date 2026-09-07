using System.Globalization;
using System.Numerics;
using System.Text;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:format-integer($value, $picture, $lang) as xs:string (3-argument version)
/// </summary>
public sealed class FormatIntegerFunction3 : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "format-integer");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "value"), Type = new XdmSequenceType { ItemType = ItemType.Integer, Occurrence = Occurrence.ZeroOrOne } },
        new() { Name = new QName(NamespaceId.None, "picture"), Type = XdmSequenceType.String },
        new() { Name = new QName(NamespaceId.None, "lang"), Type = new XdmSequenceType { ItemType = ItemType.String, Occurrence = Occurrence.ZeroOrOne } }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        if (arguments[0] == null)
            return ValueTask.FromResult<object?>("");

        var value = Convert.ToInt64(Execution.QueryExecutionContext.Atomize(arguments[0]), CultureInfo.InvariantCulture);
        var picture = arguments[1]?.ToString() ?? "1";
        var lang = arguments[2]?.ToString();

        var result = FormatIntegerFunction.FormatIntegerStatic(value, picture, lang, context);
        return ValueTask.FromResult<object?>(result);
    }
}
