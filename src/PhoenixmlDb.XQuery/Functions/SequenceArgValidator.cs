using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

internal static class SequenceArgValidator
{
    /// <summary>
    /// Validates that a value is numeric (or untypedAtomic/boolean which promote to double).
    /// Strings that are not valid xs:double values raise XPTY0004.
    /// </summary>
    internal static void RequireNumeric(object? value, string functionName, int paramPosition, Ast.ExecutionContext? context = null)
    {
        var atomized = QueryExecutionContext.AtomizeSingle(value);
        if (atomized is null or int or long or double or float or decimal
            or System.Numerics.BigInteger or bool or XsUntypedAtomic)
            return;
        if (atomized is string)
            throw new XQueryRuntimeException("XPTY0004",
                $"Required item type of {paramPosition}{(paramPosition == 2 ? "nd" : "rd")} argument of {functionName}() is xs:double; got xs:string");
    }
}
