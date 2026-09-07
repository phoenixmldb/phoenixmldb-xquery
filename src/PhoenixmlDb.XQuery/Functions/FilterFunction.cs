using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:filter($seq, $predicate) as item()*
/// </summary>
public sealed class FilterFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "filter");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "f"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var seq = SequenceHelper.Flatten(arguments[0]);
        var callable = arguments[1];
        if (!CallableCoercion.IsCallable(callable))
            throw new XQueryRuntimeException("XPTY0004", "Second argument to fn:filter must be a function");

        var results = new List<object?>();
        foreach (var item in seq)
        {
            var result = await CallableCoercion.InvokeUnaryAsync(callable, item, context);
            // Per spec, fn:filter's $f has type function(item()) as xs:boolean.
            // Non-boolean results raise XPTY0004, but nodes/untypedAtomic are atomized
            // and cast to xs:boolean per function coercion rules.
            var unwrapped = result;
            if (unwrapped is object?[] arr)
            {
                if (arr.Length == 0) unwrapped = null;
                else if (arr.Length == 1) unwrapped = arr[0];
                else throw new XQueryRuntimeException("XPTY0004",
                    "fn:filter predicate must return a single xs:boolean");
            }
            if (unwrapped is null)
                throw new XQueryRuntimeException("XPTY0004",
                    "fn:filter predicate must return xs:boolean, got empty sequence");
            if (unwrapped is bool b)
            {
                if (b) results.Add(item);
            }
            else
            {
                // Atomize nodes, then cast to xs:boolean
                var atomized = DataFunction.Atomize(unwrapped);
                if (atomized is XsUntypedAtomic ua)
                {
                    // xs:untypedAtomic → xs:boolean: "0"/"false" → false, "1"/"true" → true
                    if (bool.TryParse(ua.Value, out var boolVal))
                    {
                        if (boolVal) results.Add(item);
                    }
                    else if (ua.Value == "0") { /* false, skip */ }
                    else if (ua.Value == "1") { results.Add(item); }
                    else throw new XQueryRuntimeException("FORG0001",
                        $"Cannot cast '{ua.Value}' to xs:boolean");
                }
                else
                {
                    throw new XQueryRuntimeException("XPTY0004",
                        $"fn:filter predicate must return xs:boolean, got {unwrapped.GetType().Name}");
                }
            }
        }
        return results.ToArray();
    }
}
