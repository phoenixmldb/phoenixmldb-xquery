using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// Parses a numeric string to its appropriate .NET numeric type.
/// Used by numeric functions after atomizing node values.
/// </summary>
internal static class NumericParseHelper
{
    public static object ParseNumericString(string s)
    {
        if (long.TryParse(s, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var l))
            return l;
        if (double.TryParse(s, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var d))
            return d;
        throw new XQueryRuntimeException("FORG0001", $"Cannot cast '{s}' to a numeric type");
    }

    /// <summary>
    /// Validates that an atomized argument is a numeric type (xs:integer, xs:decimal, xs:float, xs:double)
    /// or xs:untypedAtomic (string from untyped source, which gets cast to xs:double).
    /// Raises XPTY0004 for xs:string, xs:boolean, xs:anyURI, and other non-numeric types.
    /// </summary>
    public static object? ValidateNumericArg(object? atomized, string functionName)
    {
        if (atomized is null) return null;
        return atomized switch
        {
            int or long or System.Numerics.BigInteger or decimal or float or double => atomized,
            // XPath F&O 4.0 §4.5.1: fn:abs/floor/ceiling/round of an xs:integer subtype
            // returns xs:integer (the base type). Unwrap XsTypedInteger to its long Value;
            // the function result will be re-tagged xs:integer by downstream serialization.
            // Preserving the subtype tag through these functions is not required by spec
            // and lets the value hit Convert.ToDouble in the function bodies, which fails
            // because XsTypedInteger doesn't implement IConvertible.
            Xdm.XsTypedInteger ti => ti.Value,
            // xs:untypedAtomic values (strings from untyped XML) should be cast to xs:double
            string s when IsUntypedAtomic(s) => ParseNumericString(s),
            string => throw new XQueryRuntimeException("XPTY0004",
                $"Cannot pass xs:string to {functionName} — expected numeric type"),
            bool => throw new XQueryRuntimeException("XPTY0004",
                $"Cannot pass xs:boolean to {functionName} — expected numeric type"),
            Uri => throw new XQueryRuntimeException("XPTY0004",
                $"Cannot pass xs:anyURI to {functionName} — expected numeric type"),
            // The XDM date/time, duration and binary types are NOT numeric, and passing one to
            // a numeric function is XPTY0004. They used to fall through the catch-all below and
            // reach Convert.ToDouble, which threw a raw InvalidCastException — "Unable to cast
            // object of type 'PhoenixmlDb.Xdm.XsDate' to type 'System.IConvertible'" — leaking a
            // CLR exception out of the engine where a typed XQuery error belongs. 126 QT3 cases
            // expected XPTY0004 here.
            Xdm.XsDate or Xdm.XsTime or Xdm.XsDateTime or Xdm.XsDuration
                or Xdm.XsGDay or Xdm.XsGMonth or Xdm.XsGMonthDay
                or Xdm.XsGYear or Xdm.XsGYearMonth
                => throw new XQueryRuntimeException("XPTY0004",
                    $"Cannot pass {atomized.GetType().Name} to {functionName} — expected numeric type"),
            Core.QName => throw new XQueryRuntimeException("XPTY0004",
                $"Cannot pass xs:QName to {functionName} — expected numeric type"),
            // Deliberately still open: an unrecognised type may be a custom numeric wrapper.
            // Everything KNOWN to be non-numeric is rejected above rather than relying on this.
            _ => atomized
        };
    }

    // In our engine, all strings from atomization are xs:untypedAtomic unless they come
    // from a typed context. Since we don't do schema validation, strings from XML content
    // are untypedAtomic. Strings from XQuery string literals are xs:string.
    // For now, treat string arguments to numeric functions as errors — the QT3 tests
    // that pass strings explicitly (xs:string("1")) expect XPTY0004.
    private static bool IsUntypedAtomic(string _) => false;

    /// <summary>
    /// Validates and converts an argument to double for math functions.
    /// Rejects non-numeric types with XPTY0004.
    /// </summary>
    public static double ValidateAndConvertToDouble(object? arg, string functionName)
    {
        if (arg is double d) return d;
        if (arg is float f) return f;
        if (arg is int i) return i;
        if (arg is long l) return l;
        if (arg is decimal m) return (double)m;
        if (arg is System.Numerics.BigInteger bi) return (double)bi;
        if (arg is Xdm.XsTypedInteger ti) return ti.Value;
        if (arg is bool)
            throw new XQueryRuntimeException("XPTY0004",
                $"Cannot pass xs:boolean to {functionName} — expected numeric type");
        if (arg is string)
            throw new XQueryRuntimeException("XPTY0004",
                $"Cannot pass xs:string to {functionName} — expected numeric type");
        return Convert.ToDouble(arg);
    }
}
