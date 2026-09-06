using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// Helpers to coerce maps and arrays (which are callable as functions per XPath 3.1)
/// into a plain delegate for use by higher-order functions like fn:filter, fn:for-each, etc.
/// </summary>
internal static class CallableCoercion
{
    public static async ValueTask<object?> InvokeUnaryAsync(object? callable, object? arg, Ast.ExecutionContext context)
    {
        switch (callable)
        {
            case XQueryFunction fn:
                return await fn.InvokeAsync([arg], context);
            case IDictionary<object, object?> map:
                if (arg != null && MapKeyHelper.TryGetValue(map, arg, out var v))
                    return v;
                return null;
            case List<object?> array:
                if (arg == null) return null;
                var pos = Convert.ToInt32(arg);
                if (pos >= 1 && pos <= array.Count)
                    return array[pos - 1];
                throw new XQueryRuntimeException("FOAY0001", $"Array index {pos} out of range");
            default:
                throw new XQueryRuntimeException("XPTY0004", "Value is not callable as a function");
        }
    }

    public static bool IsCallable(object? value) =>
        value is XQueryFunction or IDictionary<object, object?> or List<object?>;
}
