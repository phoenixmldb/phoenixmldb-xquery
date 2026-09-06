using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// array:build($seq as item()*, $fn as function(item()) as item()*) as array(*)
/// Builds an array from a sequence, optionally applying a function (XPath 4.0).
/// </summary>
public sealed class ArrayBuildFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Array, "build");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "fn"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ZeroOrOne } }
    ];
    public override bool IsVariadic => true;
    public override int MinArity => 1;
    public override int MaxArity => 2;

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var seq = arguments[0];
        var fn = arguments.Count > 1 ? arguments[1] as XQueryFunction : null;
        if (seq == null) return new List<object?>();

        var items = seq is object?[] arr ? arr : new[] { seq };
        var result = new List<object?>();

        if (fn != null)
        {
            foreach (var item in items)
            {
                var mapped = await fn.InvokeAsync([item], context).ConfigureAwait(false);
                result.Add(mapped);
            }
        }
        else
        {
            result.AddRange(items);
        }
        return result;
    }
}
