using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// array:for-each($array as array(*), $action as function(item()*) as item()*) as array(*)
/// </summary>
public sealed class ArrayForEachFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "for-each");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "action"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var array = arguments[0] as IList<object?> ?? [];
        var callable = arguments[1]
            ?? throw new XQueryRuntimeException("XPTY0004", "Second argument to array:for-each must be callable");

        var result = new List<object?>();

        foreach (var member in array)
        {
            var transformed = await CallableCoercion.InvokeUnaryAsync(callable, member, context);
            result.Add(transformed);
        }

        return result;
    }
}
