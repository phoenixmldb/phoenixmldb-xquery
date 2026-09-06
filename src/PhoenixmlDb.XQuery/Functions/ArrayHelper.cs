using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// XQuery 3.1 array functions.
/// </summary>
internal static class ArrayHelper
{
    /// <summary>
    /// Validates that a position argument is an integer (not decimal/double/float) and returns it.
    /// Per spec, array position arguments are xs:integer — non-integer numerics are XPTY0004.
    /// </summary>
    internal static int RequireIntegerPosition(object? arg, string functionName)
    {
        if (arg is decimal || arg is double || arg is float)
            throw new XQueryRuntimeException("XPTY0004",
                $"{functionName} requires an xs:integer position, got {arg.GetType().Name} value {arg}");
        return Convert.ToInt32(arg);
    }
}
