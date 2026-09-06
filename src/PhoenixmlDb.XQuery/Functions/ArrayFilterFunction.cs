using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// array:filter($array as array(*), $function as function(item()*) as xs:boolean) as array(*)
/// </summary>
public sealed class ArrayFilterFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "filter");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "function"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var array = arguments[0] as IList<object?> ?? [];
        var callable = arguments[1]
            ?? throw new XQueryRuntimeException("XPTY0004", "Second argument to array:filter must be callable");

        var result = new List<object?>();

        foreach (var member in array)
        {
            var keep = await CallableCoercion.InvokeUnaryAsync(callable, member, context);
            if (keep is not bool b)
                throw new XQueryRuntimeException("XPTY0004",
                    $"array:filter callback must return xs:boolean, got {(keep?.GetType().Name ?? "empty sequence")}");
            if (b)
                result.Add(member);
        }

        return result;
    }
}
