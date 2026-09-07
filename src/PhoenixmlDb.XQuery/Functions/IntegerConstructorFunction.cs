using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>xs:integer($arg)</summary>
public sealed class IntegerConstructorFunction : TypeConstructorFunction
{
    public IntegerConstructorFunction() : base("integer") { }

    protected override ValueTask<object?> InvokeCoreAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = AtomizeArg(arguments[0], context);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        try
        {
            // object, not long: xs:integer is UNBOUNDED in XSD, so a value wider than long
            // stays a BigInteger. MatchesItemType already accepts BigInteger for
            // ItemType.Integer, and xs:unsignedLong already returns one above long.MaxValue —
            // this constructor was the only step in that chain that refused to carry it.
            object result = arg switch
            {
                long l => l,
                int i => (long)i,
                short s => (long)s,
                byte b => (long)b,
                decimal d => (long)d,
                double dbl => double.IsNaN(dbl) || double.IsInfinity(dbl) || dbl >= 9.2233720368547758e18 || dbl < -9.2233720368547758e18
                    ? throw context.Error("FOCA0003", $"xs:double value {dbl} out of range for xs:integer")
                    : (long)dbl,
                float f => float.IsNaN(f) || float.IsInfinity(f) || f >= 9.2233720368547758e18f || f < -9.2233720368547758e18f
                    ? throw context.Error("FOCA0003", $"xs:float value {f} out of range for xs:integer")
                    : (long)f,
                bool bv => bv ? 1L : 0L,
                string s => ParseIntegerText(s),
                Xdm.XsUntypedAtomic ua => ParseIntegerText(ua.Value),
                Xdm.XsAnyUri uri => ParseIntegerText(uri.Value),
                // A value already wider than long arrives as BigInteger — see
                // UnsignedLongConstructorFunction, which returns one for values above
                // long.MaxValue. BigInteger does not implement IConvertible, so the
                // Convert.ToInt64 fallback below threw InvalidCastException: a raw CLR error,
                // not a spec error, and one the catch clauses did not cover. Range-check it and
                // let the existing OverflowException handler report FORG0001 instead.
                System.Numerics.BigInteger bi when bi >= long.MinValue && bi <= long.MaxValue
                    => (long)bi,
                // Wider than long: keep the BigInteger. xs:integer has no upper bound.
                System.Numerics.BigInteger bi => bi,
                _ => Convert.ToInt64(arg, CultureInfo.InvariantCulture)
            };
            return ValueTask.FromResult<object?>(result);
        }
        catch (FormatException)
        {
            throw context.Error("FORG0001", $"Cannot cast '{arg}' to xs:integer");
        }
        catch (OverflowException)
        {
            throw context.Error("FORG0001", $"Cannot cast '{arg}' to xs:integer: value out of range");
        }
        catch (InvalidCastException)
        {
            // The Convert.ToInt64 fallback reaches types that do not implement IConvertible.
            // Surfacing the CLR message ("Unable to cast object of type ... to IConvertible")
            // tells a stylesheet author nothing they can act on.
            throw context.Error("FORG0001",
                $"Cannot cast a value of type {arg.GetType().Name} to xs:integer");
        }
    }

    /// <summary>
    /// Parses integer text, keeping the value exact when it is wider than <c>long</c>.
    /// </summary>
    /// <remarks>
    /// long.Parse threw OverflowException for a literal above long.MaxValue, so
    /// <c>xs:integer('18446744073709551615')</c> reported "value out of range" for a value
    /// xs:integer permits — the type is unbounded.
    /// </remarks>
    private static object ParseIntegerText(string text)
    {
        var t = text.Trim();
        return long.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)
            ? l
            : System.Numerics.BigInteger.Parse(t, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }
}
