using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:avg($arg) as xs:anyAtomicType?
/// </summary>
public sealed class AvgFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "avg");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Double, Occurrence = Occurrence.ZeroOrOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var items = arguments[0] as IEnumerable<object?> ?? [arguments[0]];
        // Detect duration types on first item to use specialized averaging
        TimeSpan? dtdSum = null;
        Xdm.YearMonthDuration? ymdSum = null;
        double dblSum = 0;
        decimal decSum = 0;
        float fltSum = 0;
        bool hasDouble = false, hasFloat = false, hasDecimal = false, hasInteger = false;
        int count = 0;
        foreach (var rawItem in items)
        {
            var item = QueryExecutionContext.AtomizeTyped(rawItem);
            if (item != null)
            {
                if (item is TimeSpan ts)
                {
                    dtdSum = (dtdSum ?? TimeSpan.Zero) + ts;
                    count++;
                    continue;
                }
                if (item is Xdm.YearMonthDuration ymd)
                {
                    ymdSum = ymdSum.HasValue
                        ? new Xdm.YearMonthDuration(ymdSum.Value.TotalMonths + ymd.TotalMonths)
                        : ymd;
                    count++;
                    continue;
                }
                if (item is bool)
                    // FORG0006 ("unsupported operand type"), not XPTY0004. fn:sum raises
                    // FORG0006 for all four of boolean/string/anyURI/duration; fn:avg agreed
                    // only on string and used XPTY0004 for the other two — inconsistent with
                    // its own twin three lines away, and with the spec.
                    throw new XQueryRuntimeException("FORG0006", "Invalid argument type for fn:avg: xs:boolean");
                if (item is string)
                    throw new XQueryRuntimeException("FORG0006", $"Invalid argument type for fn:avg: xs:string");
                if (item is Uri)
                    throw new XQueryRuntimeException("FORG0006", "Invalid argument type for fn:avg: xs:anyURI");
                // Per XPath spec: xs:untypedAtomic is cast to xs:double
                if (item is Xdm.XsUntypedAtomic ua)
                {
                    if (double.TryParse(ua.Value, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var uaParsed))
                        item = uaParsed;
                    else
                        throw new XQueryRuntimeException("FORG0001", $"Cannot cast '{ua.Value}' to xs:double");
                }
                if (item is float f) { hasFloat = true; fltSum += f; }
                else if (item is double) { hasDouble = true; }
                else if (item is decimal d) { hasDecimal = true; decSum += d; }
                else if (item is int or long) { hasInteger = true; decSum += Convert.ToDecimal(item); }
                // BigInteger is how an xs:integer outside long range is represented, and it does
                // NOT implement IConvertible — so Convert.ToDouble/ToDecimal throw a raw
                // InvalidCastException and fn:avg crashed on a perfectly valid xs:integer input.
                // The explicit conversion operators are the supported route. fn:sum is unaffected:
                // its accumulation is a closed else-if chain, so nothing falls through to a
                // Convert call the way it does here.
                else if (item is System.Numerics.BigInteger bigi) { hasInteger = true; decSum += (decimal)bigi; }
                dblSum += item is System.Numerics.BigInteger bigd
                    ? (double)bigd
                    : Convert.ToDouble(item);
                count++;
            }
        }
        if (count == 0)
            return ValueTask.FromResult<object?>(null);

        // FORG0006: incompatible type mixing (numeric + duration, or dayTime + yearMonth)
        bool hasNumeric = hasDouble || hasFloat || hasDecimal || hasInteger;
        bool hasDuration = dtdSum.HasValue || ymdSum.HasValue;
        if (hasDuration && hasNumeric)
            throw new XQueryRuntimeException("FORG0006",
                $"Invalid argument types for fn:avg: cannot mix numeric and duration values");
        if (dtdSum.HasValue && ymdSum.HasValue)
            throw new XQueryRuntimeException("FORG0006",
                $"Invalid argument types for fn:avg: cannot mix dayTimeDuration and yearMonthDuration");

        if (dtdSum.HasValue)
        {
            var avgTicks = dtdSum.Value.Ticks / count;
            return ValueTask.FromResult<object?>(new TimeSpan(avgTicks));
        }
        if (ymdSum.HasValue)
        {
            var avgMonths = (int)Math.Round((double)ymdSum.Value.TotalMonths / count);
            return ValueTask.FromResult<object?>(new Xdm.YearMonthDuration(avgMonths));
        }
        if (hasDouble) return ValueTask.FromResult<object?>(dblSum / count);
        // xs:float: when mixing float with decimal/integer, promote all to float
        if (hasFloat) return ValueTask.FromResult<object?>((float)(dblSum / count));
        // xs:integer and xs:decimal both average to xs:decimal
        if (hasDecimal || hasInteger) return ValueTask.FromResult<object?>(decSum / count);
        return ValueTask.FromResult<object?>(dblSum / count);
    }
}
