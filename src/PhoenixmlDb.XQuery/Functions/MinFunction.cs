using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:min($arg) as xs:anyAtomicType?
/// </summary>
public sealed class MinFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "min");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.AnyAtomicType, Occurrence = Occurrence.ZeroOrOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        return FindMinMax(arguments[0], context, isMin: true);
    }

    internal static ValueTask<object?> FindMinMax(object? arg, Ast.ExecutionContext context, bool isMin)
        => FindMinMax(arg, CollationHelper.GetDefaultComparison(context), isMin);

    internal static ValueTask<object?> FindMinMax(object? arg, StringComparison comparison, bool isMin)
    {
        var items = arg as IEnumerable<object?> ?? [arg];
        object? result = null;
        bool? useStringComparison = null;
        bool hasDouble = false, hasFloat = false, hasDecimal = false;
        bool hasNaN = false;
        bool hasString = false, hasAnyUri = false;

        foreach (var rawItem in items)
        {
            var item = QueryExecutionContext.Atomize(rawItem);
            if (item is null) continue;

            // Validate orderable type — non-orderable types throw FORG0006 even for single items
            if (item is XsDuration)
                throw new Execution.XQueryRuntimeException("FORG0006",
                    "Values of type 'xs:duration' are not orderable for min/max");
            if (item is Core.QName)
                throw new Execution.XQueryRuntimeException("FORG0006",
                    "Values of type 'xs:QName' are not orderable for min/max");

            if (item is Xdm.XsUntypedAtomic ua)
            {
                // xs:untypedAtomic is cast to xs:double per spec
                // Use parameterless TryParse which handles NaN/Infinity
                if (double.TryParse(ua.Value, out var uaParsed))
                    item = uaParsed;
                else
                    throw new Execution.XQueryRuntimeException("FORG0001",
                        $"Cannot cast xs:untypedAtomic '{ua.Value}' to xs:double");
            }
            if (item is Xdm.XsTypedString tsItem) item = tsItem.Value;
            if (item is Xdm.XsAnyUri) hasAnyUri = true;
            if (useStringComparison == null && item is string)
                useStringComparison = rawItem is string;
            if (item is string) hasString = true;
            if (item is string s && useStringComparison != true)
            {
                if (double.TryParse(s, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                    item = parsed;
            }
            // Track widest numeric type for promotion
            if (item is double d && double.IsNaN(d)) { hasNaN = true; hasDouble = true; }
            else if (item is float f && float.IsNaN(f)) { hasNaN = true; hasFloat = true; }
            else if (item is double) hasDouble = true;
            else if (item is float) hasFloat = true;
            else if (item is decimal) hasDecimal = true;

            if (result is null) { result = item; continue; }
            var cmp = CompareValues(item, result, comparison);
            if (isMin ? cmp < 0 : cmp > 0) result = item;
        }

        // NaN propagation: if any value is NaN, min/max returns NaN
        if (hasNaN && result != null)
        {
            if (hasDouble)
                return ValueTask.FromResult<object?>(double.NaN);
            return ValueTask.FromResult<object?>((object)float.NaN);
        }

        // Type promotion: promote to widest numeric type
        if (result != null && (hasDouble || hasFloat || hasDecimal))
        {
            if (hasDouble && result is not double)
                result = Convert.ToDouble(result, System.Globalization.CultureInfo.InvariantCulture);
            else if (hasFloat && !hasDouble && result is not float)
                result = Convert.ToSingle(result, System.Globalization.CultureInfo.InvariantCulture);
            else if (hasDecimal && !hasDouble && !hasFloat && result is long or int)
                result = Convert.ToDecimal(result, System.Globalization.CultureInfo.InvariantCulture);
        }

        // anyURI/string promotion: when mixed, result should be xs:string
        if (hasAnyUri && hasString && result is Xdm.XsAnyUri uriResult)
            result = uriResult.ToString();

        return ValueTask.FromResult<object?>(result);
    }

    internal static int CompareValues(object? a, object? b) => CompareValues(a, b, StringComparison.Ordinal);

    internal static int CompareValues(object? a, object? b, StringComparison stringComparison)
    {
        if (a is null && b is null) return 0;
        if (a is null) return -1;
        if (b is null) return 1;

        // Unwrap XsTypedString to plain string for comparison purposes
        if (a is Xdm.XsTypedString typedA) a = typedA.Value;
        if (b is Xdm.XsTypedString typedB) b = typedB.Value;
        // Unwrap derived-integer-typed values to their CLR long so min()/max() over
        // xs:long, xs:int, etc. are recognized as numeric and compared exactly, rather
        // than falling through to the FORG0006 "not orderable" guard below.
        if (a is Xdm.XsTypedInteger intA) a = intA.Value;
        if (b is Xdm.XsTypedInteger intB) b = intB.Value;

        bool aNum = a is int or long or double or float or decimal or System.Numerics.BigInteger;
        bool bNum = b is int or long or double or float or decimal or System.Numerics.BigInteger;

        if (aNum && bNum)
        {
            if (a is double or float || b is double or float)
            {
                var da = a is System.Numerics.BigInteger abi ? (double)abi : Convert.ToDouble(a);
                var db = b is System.Numerics.BigInteger bbi ? (double)bbi : Convert.ToDouble(b);
                return da.CompareTo(db);
            }
            if (a is System.Numerics.BigInteger || b is System.Numerics.BigInteger)
            {
                var ba = a is System.Numerics.BigInteger ba1 ? ba1 : (System.Numerics.BigInteger)Convert.ToInt64(a);
                var bb = b is System.Numerics.BigInteger bb1 ? bb1 : (System.Numerics.BigInteger)Convert.ToInt64(b);
                return ba.CompareTo(bb);
            }
            return Convert.ToDecimal(a).CompareTo(Convert.ToDecimal(b));
        }

        if (a is string sa && b is string sb)
            return string.Compare(sa, sb, stringComparison);

        // Date/time comparisons
        if (a is XsDateTime xdtA && b is XsDateTime xdtB)
            return xdtA.CompareTo(xdtB);
        if (a is XsDate xdA && b is XsDate xdB)
            return xdA.CompareTo(xdB);
        if (a is XsTime xtA && b is XsTime xtB)
            return xtA.CompareTo(xtB);
        if (a is DateTimeOffset dtA && b is DateTimeOffset dtB)
            return dtA.CompareTo(dtB);
        if (a is DateOnly dA && b is DateOnly dB)
            return dA.CompareTo(dB);
        if (a is TimeOnly tA && b is TimeOnly tB)
            return tA.CompareTo(tB);

        // Duration comparisons
        if (a is TimeSpan tsA && b is TimeSpan tsB)
            return tsA.CompareTo(tsB);
        if (a is YearMonthDuration ymA && b is YearMonthDuration ymB)
            return ymA.CompareTo(ymB);

        // xs:anyURI: comparable as string (anyURI promotes to string)
        if (a is Xdm.XsAnyUri && b is Xdm.XsAnyUri)
            return string.Compare(a.ToString(), b.ToString(), stringComparison);
        if ((a is Xdm.XsAnyUri || a is string) && (b is Xdm.XsAnyUri || b is string))
            return string.Compare(a.ToString(), b.ToString(), stringComparison);

        // Boolean: false < true
        if (a is bool boolA && b is bool boolB)
            return boolA.CompareTo(boolB);

        // Duration comparisons: xs:duration (without subtype) is not orderable
        if (a is XsDuration || b is XsDuration)
            throw new Execution.XQueryRuntimeException("FORG0006",
                $"Values of type 'xs:duration' are not orderable for min/max");

        // xs:hexBinary / xs:base64Binary: compare as unsigned byte sequences
        if (a is Xdm.XdmValue axv && axv.RawValue is byte[] aBytes)
        {
            if (b is Xdm.XdmValue bxv && bxv.RawValue is byte[] bBytes && axv.Type == bxv.Type)
                return aBytes.AsSpan().SequenceCompareTo(bBytes);
            throw new Execution.XQueryRuntimeException("FORG0006",
                $"Cannot compare {axv.Type} with {b.GetType().Name} for min/max");
        }
        if (b is Xdm.XdmValue bxv2 && bxv2.RawValue is byte[])
            throw new Execution.XQueryRuntimeException("FORG0006",
                $"Cannot compare {a.GetType().Name} with {bxv2.Type} for min/max");

        // FORG0006: Incompatible typed atomic values
        // Remaining types are not orderable
        throw new Execution.XQueryRuntimeException("FORG0006",
            $"Values of type '{a.GetType().Name}' and '{b.GetType().Name}' are not orderable for min/max");
    }
}
