using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// Shared validation helpers for map functions.
/// </summary>
internal static class MapHelper
{
    /// <summary>Creates a new ordered XDM map (insertion-order iteration, XPath 4.0).</summary>
    internal static Execution.OrderedXdmMap NewMap()
        => new(Execution.XdmMapKeyComparer.Instance);

    /// <summary>
    /// Copies a map into a new ordered XDM map, preserving the source's iteration
    /// order and XDM-aware key comparison.
    /// </summary>
    internal static Execution.OrderedXdmMap CopyMap(IDictionary<object, object?> source)
        => new(source, Execution.XdmMapKeyComparer.Instance);

    /// <summary>
    /// Validates that the argument is a single map. Throws XPTY0004 for non-maps, sequences of maps, empty sequences.
    /// </summary>
    internal static IDictionary<object, object?> RequireMap(object? arg, string funcName)
    {
        if (arg is IDictionary<object, object?> map)
            return map;
        // sequence of maps, function items, non-map sequences, empty sequence → XPTY0004
        throw new XQueryRuntimeException("XPTY0004",
            $"First argument to {funcName} must be a single map");
    }

    /// <summary>
    /// Validates that the key argument is a single atomic value (not a sequence).
    /// </summary>
    internal static object RequireSingleAtomicKey(object? arg, string funcName)
    {
        var key = QueryExecutionContext.AtomizeTyped(arg);
        if (key is null)
            throw new XQueryRuntimeException("XPTY0004",
                $"Key argument to {funcName} must be a single atomic value");
        if (key is object?[] arr)
        {
            if (arr.Length == 1 && arr[0] is not null)
                return arr[0]!;
            throw new XQueryRuntimeException("XPTY0004",
                $"Key argument to {funcName} must be a single atomic value, got sequence of {arr.Length} items");
        }
        return key;
    }
}
