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
/// Unary operator node.
/// </summary>
public sealed class UnaryOperatorNode : PhysicalOperator
{
    public required PhysicalOperator Operand { get; init; }
    public required UnaryOperator Operator { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        object? value;
        bool hasValue;
        if (Operator == UnaryOperator.Not)
        {
            // not() needs the full sequence for correct EBV (FORG0006 on multi-atom sequences)
            var items = new List<object?>();
            await foreach (var item in Operand.ExecuteAsync(context))
                items.Add(item);
            value = items.Count switch
            {
                0 => null,
                1 => items[0],
                _ => items.ToArray()
            };
            hasValue = true; // not() of empty sequence = true (EBV rules)
        }
        else
        {
            value = null;
            hasValue = false;
            await foreach (var item in Operand.ExecuteAsync(context))
            {
                value = item;
                hasValue = true;
                break;
            }
        }

        // XPath 3.1 §4.2: unary +/- of empty sequence yields the empty sequence
        // (arithmetic operators return empty when any operand is empty, except in
        // XPath 1.0 backwards-compatible mode).
        if (!hasValue && !context.BackwardsCompatible
            && Operator is UnaryOperator.Plus or UnaryOperator.Minus)
        {
            yield break;
        }

        var result = Operator switch
        {
            UnaryOperator.Plus => CoerceToNumeric(value, context.BackwardsCompatible),
            UnaryOperator.Minus => Negate(value, context.BackwardsCompatible),
            UnaryOperator.Not => !QueryExecutionContext.EffectiveBooleanValue(value),
            _ => value
        };

        yield return result;
    }

    private static object? CoerceToNumeric(object? value, bool backwardsCompatible = false)
    {
        value = QueryExecutionContext.Atomize(value);
        if (value is null) return value;
        // Unwrap a derived-integer-typed value to its exact CLR long. XsTypedInteger does
        // not implement IConvertible, so the Convert.ToDouble fallback below would throw —
        // and converting a near-long.MaxValue value through double would lose precision.
        if (value is Xdm.XsTypedInteger tInt) value = tInt.Value;
        if (value is int or long or double or decimal or float or BigInteger) return value;
        if (value is Xdm.XsUntypedAtomic u)
            return double.TryParse(u.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d2) ? d2 : double.NaN;
        if (value is string s)
        {
            if (backwardsCompatible)
                return double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : double.NaN;
            throw new XQueryRuntimeException("XPTY0004", "Unary plus is not defined for xs:string");
        }
        return Convert.ToDouble(value);
    }

    private static object? Negate(object? value, bool backwardsCompatible = false)
    {
        value = QueryExecutionContext.Atomize(value);
        if (value is null) return null; // -() = ()
        // Unwrap a derived-integer-typed value to its exact CLR long so the integer switch
        // arm below negates it without precision loss. XsTypedInteger does not implement
        // IConvertible, so the Convert.ToDouble fallback would otherwise throw (and a
        // near-long.MaxValue value cannot round-trip through double).
        if (value is Xdm.XsTypedInteger tInt) value = tInt.Value;
        // xs:untypedAtomic promotes to xs:double for arithmetic
        if (value is Xdm.XsUntypedAtomic u)
            value = double.TryParse(u.ToString(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var ud) ? ud : double.NaN;
        if (backwardsCompatible && value is string s)
            return -(double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : double.NaN);
        if (value is string)
            throw new XQueryRuntimeException("XPTY0004", "Unary minus is not defined for xs:string");
        // In backward-compat mode (XPath 1.0), all numbers are doubles, so -(integer 0) = double -0.0
        if (backwardsCompatible)
        {
            var d = value is BigInteger bi2 ? (double)bi2 : Convert.ToDouble(value);
            return -d;
        }
        return value switch
        {
            int i => -i,
            long l => -l,
            BigInteger bi => -bi,
            float f => -f,
            double d => -d,
            decimal m => -m,
            _ => -Convert.ToDouble(value)
        };
    }
}
