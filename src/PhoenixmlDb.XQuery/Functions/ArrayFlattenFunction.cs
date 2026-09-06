using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// array:flatten($input as item()*) as item()*
/// </summary>
public sealed class ArrayFlattenFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "flatten");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "input"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var result = new List<object?>();
        Flatten(arguments[0], result);
        return ValueTask.FromResult<object?>(result.ToArray());
    }

    private static void Flatten(object? item, List<object?> result)
    {
        switch (item)
        {
            case IList<object?> array:
                foreach (var member in array)
                {
                    Flatten(member, result);
                }
                break;

            case IEnumerable<object?> seq when item is not string:
                foreach (var member in seq)
                {
                    Flatten(member, result);
                }
                break;

            default:
                if (item != null)
                {
                    result.Add(item);
                }
                break;
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// XPath/XQuery 4.0 new array functions
// ═══════════════════════════════════════════════════════════════════════════
