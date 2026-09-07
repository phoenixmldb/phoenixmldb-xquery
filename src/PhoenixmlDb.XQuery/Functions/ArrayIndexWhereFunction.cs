using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// array:index-where($array as array(*), $pred as function(item()*) as xs:boolean) as xs:integer*
/// Returns positions where the predicate is true (XPath 4.0).
/// </summary>
public sealed class ArrayIndexWhereFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "index-where");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Integer, Occurrence = Occurrence.ZeroOrMore };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "pred"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        if (arguments[0] is not List<object?> list || arguments[1] is not XQueryFunction pred)
            return Array.Empty<object>();

        var result = new List<object?>();
        for (var i = 0; i < list.Count; i++)
        {
            var match = await pred.InvokeAsync([list[i]], context).ConfigureAwait(false);
            if (match is true || (match is not false && match != null))
                result.Add((long)(i + 1));
        }
        return result.Count == 1 ? result[0] : result.ToArray();
    }
}
