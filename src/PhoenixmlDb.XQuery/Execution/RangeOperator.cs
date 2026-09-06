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
        object? startVal = null, endVal = null;
        await foreach (var item in Start.ExecuteAsync(context))
        { startVal = item; break; }
        await foreach (var item in End.ExecuteAsync(context))
        { endVal = item; break; }

        if (startVal == null || endVal == null)
            yield break;

        startVal = context.AtomizeWithNodes(startVal);
        endVal = context.AtomizeWithNodes(endVal);
        // xs:untypedAtomic is cast to xs:integer; other non-integer types are XPTY0004
        if (startVal is Xdm.XsUntypedAtomic sua) startVal = long.Parse(sua.Value);
        if (endVal is Xdm.XsUntypedAtomic eua) endVal = long.Parse(eua.Value);
        if (startVal is double or float or decimal)
            throw new XQueryRuntimeException("XPTY0004", "Range expression requires xs:integer operands");
        if (endVal is double or float or decimal)
            throw new XQueryRuntimeException("XPTY0004", "Range expression requires xs:integer operands");
        // Support BigInteger ranges for values beyond long range
        if (startVal is BigInteger || endVal is BigInteger)
        {
            var sBig = startVal is BigInteger sb2 ? sb2 : new BigInteger(Convert.ToInt64(startVal));
            var eBig = endVal is BigInteger eb2 ? eb2 : new BigInteger(Convert.ToInt64(endVal));
            for (var i = sBig; i <= eBig; i++)
            {
                if ((i - sBig) % 1024 == 0)
                    context.CancellationToken.ThrowIfCancellationRequested();
                yield return i >= long.MinValue && i <= long.MaxValue ? (object)(long)i : i;
            }
        }
        else
        {
            var s = Convert.ToInt64(startVal);
            var e = Convert.ToInt64(endVal);
            for (var i = s; i <= e; i++)
            {
                if ((i - s) % 1024 == 0)
                    context.CancellationToken.ThrowIfCancellationRequested();
                yield return i;
            }
        }
    }
}
