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
        return ClampPosition(arg);
    }

    /// <summary>
    /// An xs:integer position as an int, clamped: no array has 2^31 members, so a position
    /// beyond the int range is out of bounds whatever the array, and the caller's bounds check
    /// raises FOAY0001. Convert.ToInt32 threw .NET's OverflowException instead
    /// (array:get([1], 4294967297)).
    /// </summary>
    internal static int ClampPosition(object? arg)
    {
        var value = arg switch
        {
            System.Numerics.BigInteger big => big,
            PhoenixmlDb.Xdm.XsTypedInteger typed => new System.Numerics.BigInteger(typed.Value),
            _ => new System.Numerics.BigInteger(Convert.ToInt64(arg, System.Globalization.CultureInfo.InvariantCulture)),
        };
        if (value > int.MaxValue) return int.MaxValue;
        if (value < int.MinValue) return int.MinValue;
        return (int)value;
    }
}
