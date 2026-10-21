using System.Numerics;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Functions;

namespace PhoenixmlDb.XQuery.Execution;

/// <summary>
/// Evaluates <c>fn:substring</c>, <c>fn:string-length</c> and <c>fn:starts-with</c> over a
/// <see cref="StringSlice"/> without making its string (xquery#287). Each path applies only where
/// its operands are plain (a variable, a substring of one, a numeric position); anything else
/// returns false or null and the call is evaluated the ordinary way, with the same result.
/// </summary>
internal static class StringSliceEvaluation
{
    /// <summary>
    /// The slice <paramref name="op"/> evaluates to: a variable holding a string or a slice, or
    /// <c>fn:substring</c> of one with numeric positions; null for any other operator or value.
    /// With <paramref name="fromSliceOnly"/>, a chain that starts at a plain string is null too,
    /// so that an ordinary query never pays for the slice path.
    /// </summary>
    internal static StringSlice? TryEvaluate(PhysicalOperator op, QueryExecutionContext context, bool fromSliceOnly)
    {
        switch (op)
        {
            case VariableOperator variable:
                return context.GetVariableRaw(variable.VariableName) switch
                {
                    StringSlice slice => slice,
                    string s when !fromSliceOnly => StringSlice.Of(s),
                    _ => null,
                };
            case FunctionCallOperator call when call.ArgumentOperators.Count is 2 or 3:
                var function = call.ResolvedFunction(context);
                if (function is not (SubstringFunction or Substring3Function))
                    return null;
                if (TryEvaluate(call.ArgumentOperators[0], context, fromSliceOnly) is not { } source)
                    return null;
                if (Position(call.ArgumentOperators[1], context) is not { } start)
                    return null;
                if (call.ArgumentOperators.Count == 2)
                    return SubstringFunction.TryBounds(start, out var from)
                        ? source.Substring(from, int.MaxValue)
                        : source.Substring(1, 0);
                if (Position(call.ArgumentOperators[2], context) is not { } length)
                    return null;
                return Substring3Function.TryBounds(start, length, out var first, out var count)
                    ? source.Substring(first, count)
                    : source.Substring(1, 0);
            default:
                return null;
        }
    }

    /// <summary>
    /// <c>fn:string-length</c>, <c>fn:starts-with</c> or <c>fn:substring</c> of a slice, for
    /// <paramref name="call"/>; false when its first argument is not one, or another argument is
    /// not plain.
    /// </summary>
    internal static bool TryEvaluateCall(FunctionCallOperator call, XQueryFunction function,
        QueryExecutionContext context, out object? result)
    {
        result = null;
        switch (function)
        {
            case StringLengthFunction when call.ArgumentOperators.Count == 1:
                if (TryEvaluate(call.ArgumentOperators[0], context, fromSliceOnly: true) is not { } measured)
                    return false;
                result = measured.CodepointLength;
                return true;
            case StartsWithFunction when call.ArgumentOperators.Count == 2:
                if (TryEvaluate(call.ArgumentOperators[0], context, fromSliceOnly: true) is not { } tested)
                    return false;
                if (!PhysicalOperator.CanEvaluateSync(call.ArgumentOperators[1]))
                    return false;
                var prefix = call.ArgumentOperators[1].EvaluateSync(context) switch
                {
                    null => "",
                    string s => s,
                    StringSlice slice => slice.ToString(),
                    _ => null,
                };
                if (prefix is null)
                    return false;
                result = tested.Span.StartsWith(prefix, CollationHelper.GetDefaultComparison(context));
                return true;
            case SubstringFunction or Substring3Function:
                if (TryEvaluate(call, context, fromSliceOnly: true) is not { } part)
                    return false;
                result = part.ToString();
                return true;
            default:
                return false;
        }
    }

    // One numeric item, as fn:substring's xs:double parameter receives it; null for anything the
    // function call converts or rejects itself.
    private static double? Position(PhysicalOperator op, QueryExecutionContext context)
    {
        if (!PhysicalOperator.CanEvaluateSync(op))
            return null;
        return op.EvaluateSync(context) switch
        {
            double d => d,
            float f => f,
            decimal m => (double)m,
            long l => l,
            int i => i,
            BigInteger b => (double)b,
            _ => null,
        };
    }
}
