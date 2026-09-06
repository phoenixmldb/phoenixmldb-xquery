using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// array:for-each-pair($array1 as array(*), $array2 as array(*), $f as function(item()*, item()*) as item()*) as array(*)
/// </summary>
public sealed class ArrayForEachPairFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "for-each-pair");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "array1"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "array2"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "f"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var array1 = arguments[0] as IList<object?> ?? [];
        var array2 = arguments[1] as IList<object?> ?? [];
        var callable = arguments[2];

        // Validate callable is a function with arity 2
        if (callable is not XQueryFunction f)
            throw new XQueryRuntimeException("XPTY0004",
                "Third argument to array:for-each-pair must be a function");
        if (f.Parameters.Count != 2)
            throw new XQueryRuntimeException("XPTY0004",
                $"Function passed to array:for-each-pair must have arity 2, got {f.Parameters.Count}");

        var result = new List<object?>();
        var minLen = Math.Min(array1.Count, array2.Count);

        for (var i = 0; i < minLen; i++)
        {
            var transformed = await f.InvokeAsync([array1[i], array2[i]], context);
            result.Add(transformed);
        }

        return result;
    }
}
