using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// array:index-of($array as array(*), $target as item()*) as xs:integer*
/// Returns positions where the target appears (XPath 4.0).
/// </summary>
public sealed class ArrayIndexOfFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "index-of");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Integer, Occurrence = Occurrence.ZeroOrMore };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "target"), Type = XdmSequenceType.ZeroOrMoreItems }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        if (arguments[0] is not List<object?> list) return ValueTask.FromResult<object?>(Array.Empty<object>());
        var target = Execution.QueryExecutionContext.Atomize(arguments[1])?.ToString();

        var result = new List<object?>();
        for (var i = 0; i < list.Count; i++)
        {
            var val = Execution.QueryExecutionContext.Atomize(list[i])?.ToString();
            if (Equals(val, target))
                result.Add((long)(i + 1));
        }
        return ValueTask.FromResult<object?>(result.Count == 1 ? result[0] : result.ToArray());
    }
}
