using System.Numerics;
using System.Text;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.Xdm.Serialization;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Functions;
using PhoenixmlDb.XQuery.Optimizer;

namespace PhoenixmlDb.XQuery.Execution;

/// <summary>
/// Range expression: start to end → yields integers.
/// </summary>
public sealed class RangeOperator : PhysicalOperator
{
    public required PhysicalOperator Start { get; init; }
    public required PhysicalOperator End { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        if (await EvaluateBoundsAsync(context).ConfigureAwait(false) is not var (sBig, eBig))
            yield break;
        if (sBig < long.MinValue || eBig > long.MaxValue)
        {
            for (var i = sBig; i <= eBig; i++)
            {
                if ((i - sBig) % 1024 == 0)
                    context.CancellationToken.ThrowIfCancellationRequested();
                yield return i >= long.MinValue && i <= long.MaxValue ? (object)(long)i : i;
            }
        }
        else
        {
            var s = (long)sBig;
            var e = (long)eBig;
            for (var i = s; i <= e; i++)
            {
                if ((i - s) % 1024 == 0)
                    context.CancellationToken.ThrowIfCancellationRequested();
                yield return i;
                if (i == long.MaxValue) break;
            }
        }
    }

    /// <summary>
    /// The first and last integer of the range, or null for an empty operand. A caller that needs
    /// only the count or a slice of the range (fn:count, fn:subsequence) uses these instead of
    /// generating the integers (QT3 cbcl-subsequence: 1 to 3000000000).
    /// </summary>
    internal async ValueTask<(BigInteger First, BigInteger Last)?> EvaluateBoundsAsync(QueryExecutionContext context)
    {
        object? startVal = null, endVal = null;
        await foreach (var item in Start.ExecuteAsync(context))
        { startVal = item; break; }
        await foreach (var item in End.ExecuteAsync(context))
        { endVal = item; break; }

        if (startVal == null || endVal == null)
            return null;

        startVal = context.AtomizeWithNodes(startVal);
        endVal = context.AtomizeWithNodes(endVal);
        // xs:untypedAtomic is cast to xs:integer; other non-integer types are XPTY0004
        if (startVal is Xdm.XsUntypedAtomic sua) startVal = CastToInteger(sua);
        if (endVal is Xdm.XsUntypedAtomic eua) endVal = CastToInteger(eua);
        if (startVal is double or float or decimal)
            throw new XQueryRuntimeException("XPTY0004", "Range expression requires xs:integer operands");
        if (endVal is double or float or decimal)
            throw new XQueryRuntimeException("XPTY0004", "Range expression requires xs:integer operands");
        var first = startVal is BigInteger sb ? sb : new BigInteger(Convert.ToInt64(startVal));
        var last = endVal is BigInteger eb ? eb : new BigInteger(Convert.ToInt64(endVal));
        return (first, last);
    }

    // An xs:untypedAtomic operand is cast to xs:integer; a lexical form that is not one raises
    // FORG0001. long.Parse threw a .NET FormatException instead, which surfaced with no error code.
    private static object CastToInteger(Xdm.XsUntypedAtomic value)
    {
        var lexical = value.Value.Trim();
        if (long.TryParse(lexical, System.Globalization.NumberStyles.AllowLeadingSign,
                System.Globalization.CultureInfo.InvariantCulture, out var l))
            return l;
        if (BigInteger.TryParse(lexical, System.Globalization.NumberStyles.AllowLeadingSign,
                System.Globalization.CultureInfo.InvariantCulture, out var big))
            return big;
        throw new XQueryRuntimeException("FORG0001", $"Cannot cast '{value.Value}' to xs:integer");
    }
}
