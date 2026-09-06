using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// map:build($seq as item()*, $key as function(item()) as xs:anyAtomicType,
///           $value as function(item()) as item()*) as map(*)
/// Builds a map from a sequence using key and value functions (XPath 4.0).
/// </summary>
public sealed class MapBuildFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Map, "build");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "key"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ZeroOrOne } },
        new() { Name = new QName(NamespaceId.None, "value"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ZeroOrOne } }
    ];
    public override bool IsVariadic => true;
    public override int MinArity => 1;
    public override int MaxArity => 3;

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var seq = arguments[0];
        // XPath 4.0: $key and $value both default to the identity function fn($x){$x},
        // so map:build($input) keys each item by itself. A null keyFn here means
        // "argument omitted" → identity.
        var keyFn = arguments.Count > 1 ? arguments[1] as XQueryFunction : null;
        var valueFn = arguments.Count > 2 ? arguments[2] as XQueryFunction : null;
        if (seq == null) return MapHelper.NewMap();

        var items = seq is object?[] arr ? arr : new[] { seq };
        var result = MapHelper.NewMap();
        foreach (var item in items)
        {
            var key = keyFn != null
                ? await keyFn.InvokeAsync([item], context).ConfigureAwait(false)
                : item;
            var value = valueFn != null
                ? await valueFn.InvokeAsync([item], context).ConfigureAwait(false)
                : item;
            if (key != null)
            {
                var atomKey = QueryExecutionContext.AtomizeTyped(key) ?? key;
                result[atomKey] = value;
            }
        }
        return result;
    }
}
