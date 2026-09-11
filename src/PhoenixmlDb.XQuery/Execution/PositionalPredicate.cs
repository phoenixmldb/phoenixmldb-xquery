using System.Numerics;

namespace PhoenixmlDb.XQuery.Execution;

/// <summary>
/// Whether a predicate's value selects the item at <c>position</c>: a NUMERIC value by
/// equality with the position (XPath 3.1 §3.3.2), anything else by its effective boolean value.
/// </summary>
/// <remarks>
/// This was written out three times — twice in FilterOperator, once in PerNodeStepOperator — as
/// type tests for int, long, double and decimal. BigInteger and float were in none of them, so
/// such a value fell through to EBV, which is true for any non-zero number, and the predicate
/// kept EVERY item: <c>(10,20,30)[xs:integer('2')]</c> returned all three. One definition now,
/// covering every numeric the engine produces.
/// </remarks>
internal static class PositionalPredicate
{
    public static bool Selects(object? value, long position) => value switch
    {
        Xdm.XsTypedInteger typed => typed.Value == position,
        long l => l == position,
        int i => i == position,
        BigInteger b => b == position,
        // An integer position converts to double exactly below 2^53; NaN and fractions never match.
        double d => d == position,
        // Compared as double: comparing as float would round a large position into a false match.
        float f => (double)f == position,
        decimal m => m == position,
        _ => QueryExecutionContext.EffectiveBooleanValue(value),
    };
}
