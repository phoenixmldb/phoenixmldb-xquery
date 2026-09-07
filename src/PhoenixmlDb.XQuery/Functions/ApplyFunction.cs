using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:apply($function, $array) as item()*
/// </summary>
public sealed class ApplyFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "apply");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "function"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "array"), Type = new() { ItemType = ItemType.Array, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var callable = arguments[0];
        // XDM arrays are List<object?>, but can also be object?[] in some contexts
        IReadOnlyList<object?> arr;
        if (arguments[1] is List<object?> list)
            arr = list;
        else if (arguments[1] is object?[] objArr)
            arr = objArr;
        else
            throw new XQueryRuntimeException("XPTY0004", "Second argument to fn:apply must be an array");

        // Maps and arrays are callable as functions (arity 1) per XPath 3.1
        if (callable is IDictionary<object, object?> map)
        {
            if (arr.Count != 1)
                throw new XQueryRuntimeException("FOAP0001",
                    $"Map as function expects 1 argument, but array has {arr.Count} member(s)");
            var key = arr[0];
            if (key != null && MapKeyHelper.TryGetValue(map, key, out var v))
                return v;
            return null;
        }
        if (callable is List<object?> xdmArray && callable != arguments[1])
        {
            if (arr.Count != 1)
                throw new XQueryRuntimeException("FOAP0001",
                    $"Array as function expects 1 argument, but array has {arr.Count} member(s)");
            var idx = Convert.ToInt32(arr[0]);
            if (idx >= 1 && idx <= xdmArray.Count)
                return xdmArray[idx - 1];
            throw new XQueryRuntimeException("FOAY0001", $"Array index {idx} out of range");
        }

        var func = callable as XQueryFunction
            ?? throw new XQueryRuntimeException("XPTY0004", "First argument to fn:apply must be a function");

        // FOAP0001: arity of function must match array size
        if (!func.IsVariadic && func.Parameters.Count != arr.Count)
            throw new XQueryRuntimeException("FOAP0001",
                $"Function expects {func.Parameters.Count} argument(s), but array has {arr.Count} member(s)");
        if (func.IsVariadic && arr.Count < func.Parameters.Count)
            throw new XQueryRuntimeException("FOAP0001",
                $"Function expects at least {func.Parameters.Count} argument(s), but array has {arr.Count} member(s)");

        return await func.InvokeAsync(arr, context);
    }
}
