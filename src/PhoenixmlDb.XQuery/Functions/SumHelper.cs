using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

internal static class SumHelper
{
    // nodeProvider resolves the string value of a storage-deserialized element (NULL
    // precomputed StringValue); without it the element atomizes to '' and the cast to
    // xs:double fails, while fn:data() over the same nodes works (phoenixmldb-xquery#5).
    internal static ValueTask<object?> SumCore(object? arg, object? zero,
        INodeProvider? nodeProvider = null)
    {
        // Atomize first so XDM arrays in the input flatten to their atomic members
        // (e.g. fn:sum([[1,2],[3,4]]) → 10), per F&O fn:sum array-flattening semantics.
        if (arg is List<object?> || (arg is IEnumerable<object?> probe && probe.Any(static x => x is List<object?>)))
            arg = DataFunction.Atomize(arg, nodeProvider);
        var items = arg as IEnumerable<object?> ?? [arg];
        bool hasDouble = false, hasFloat = false, hasDecimal = false, hasInt = false;
        long intSum = 0;
        decimal decSum = 0;
        double dblSum = 0;
        float fltSum = 0;
        TimeSpan tsSum = TimeSpan.Zero;
        int ymSum = 0;
        bool hasDayTime = false, hasYearMonth = false;
        int count = 0;
        // Per F&O fn:sum: "if $arg is a sequence containing exactly one value, then
        // fn:sum returns that value" — unchanged, preserving its dynamic type. Capture
        // the single contributing item (after atomization) so a lone derived-integer
        // value such as xs:unsignedShort(1) stays xs:unsignedShort rather than being
        // demoted to a bare xs:integer by the accumulation path (QT3 K2-SeqSUMFunc-4).
        object? singleItem = null;

        foreach (var rawItem in items)
        {
            // Use AtomizeTyped to preserve xs:untypedAtomic (from element content)
            // so it can be cast to xs:double per the spec
            var item = QueryExecutionContext.AtomizeTyped(rawItem, nodeProvider);
            if (item is null) continue;
            count++;
            singleItem = count == 1 ? item : null;
            // Unwrap derived-integer-typed values (xs:long, xs:int, …) to their CLR long so
            // they accumulate via the integer branch below; otherwise XsTypedInteger matches
            // no branch and silently contributes 0 to the sum.
            if (item is Xdm.XsTypedInteger tiSum) item = tiSum.Value;
            if (item is TimeSpan ts) { hasDayTime = true; tsSum += ts; }
            else if (item is YearMonthDuration ym) { hasYearMonth = true; ymSum += ym.TotalMonths; }
            else if (item is double d) { hasDouble = true; dblSum += d; }
            else if (item is float f) { hasFloat = true; fltSum += f; dblSum += f; }
            else if (item is decimal) { hasDecimal = true; decSum += Convert.ToDecimal(item); }
            else if (item is int or long) { hasInt = true; intSum += Convert.ToInt64(item); }
            else if (item is Xdm.XsUntypedAtomic ua)
            {
                // Per XPath spec: xs:untypedAtomic is cast to xs:double for sum()
                hasDouble = true;
                if (double.TryParse(ua.Value, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var uv))
                    dblSum += uv;
                else
                    throw new XQueryRuntimeException("FORG0001", $"Cannot cast '{ua.Value}' to xs:double");
            }
            else if (item is bool)
            { throw new XQueryRuntimeException("FORG0006", "Invalid argument type for fn:sum: xs:boolean"); }
            else if (item is string)
            { throw new XQueryRuntimeException("FORG0006", $"Invalid argument type for fn:sum: xs:string"); }
            else if (item is Uri or Xdm.XsAnyUri)
            { throw new XQueryRuntimeException("FORG0006", "Invalid argument type for fn:sum: xs:anyURI"); }
            else if (item is Xdm.XsDuration)
            { throw new XQueryRuntimeException("FORG0006", "Invalid argument type for fn:sum: xs:duration (use dayTimeDuration or yearMonthDuration)"); }
        }

        if (count == 0) return ValueTask.FromResult(zero);

        // Single-value sequence: fn:sum returns that value unchanged, preserving its
        // dynamic type (so sum(xs:unsignedShort(1)) instance of xs:unsignedShort holds).
        // Only the derived-integer case needs this special handling — every other
        // single-value type round-trips through the accumulation path unchanged.
        if (count == 1 && singleItem is Xdm.XsTypedInteger)
            return ValueTask.FromResult<object?>(singleItem);

        // FORG0006: incompatible type mixing (numeric + duration, or dayTime + yearMonth)
        bool hasNumeric = hasDouble || hasFloat || hasDecimal || hasInt;
        if ((hasDayTime || hasYearMonth) && hasNumeric)
            throw new XQueryRuntimeException("FORG0006",
                $"Invalid argument types for fn:sum: cannot mix numeric and duration values");
        if (hasDayTime && hasYearMonth)
            throw new XQueryRuntimeException("FORG0006",
                $"Invalid argument types for fn:sum: cannot mix dayTimeDuration and yearMonthDuration");

        if (hasDayTime) return ValueTask.FromResult<object?>(tsSum);
        if (hasYearMonth) return ValueTask.FromResult<object?>(new YearMonthDuration(ymSum));
        if (hasDouble) return ValueTask.FromResult<object?>(dblSum + (double)decSum + intSum);
        // xs:float promotion: if only floats (no doubles), return float
        if (hasFloat) return ValueTask.FromResult<object?>((float)(fltSum + (float)decSum + intSum));
        if (hasDecimal) return ValueTask.FromResult<object?>(decSum + intSum);
        return ValueTask.FromResult<object?>(intSum);
    }
}
