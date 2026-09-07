using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>xs:double($arg)</summary>
public sealed class DoubleConstructorFunction : TypeConstructorFunction
{
    public DoubleConstructorFunction() : base("double") { }

    protected override ValueTask<object?> InvokeCoreAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = AtomizeArg(arguments[0], context);
        if (arg is null) return ValueTask.FromResult<object?>(null);

        try
        {
            var result = arg switch
            {
                double d => d,
                float f => (double)f,
                decimal d => d == 0m ? 0.0 : (double)d,
                long l => (double)l,
                int i => (double)i,
                bool bv => bv ? 1.0 : 0.0,
                string s => ParseXsDouble(s.Trim()),
                Xdm.XsUntypedAtomic ua => ParseXsDouble(ua.Value.Trim()),
                Xdm.XsAnyUri uri => ParseXsDouble(uri.Value.Trim()),
                _ => Convert.ToDouble(arg, CultureInfo.InvariantCulture)
            };
            return ValueTask.FromResult<object?>(result);
        }
        catch (FormatException)
        {
            throw context.Error("FORG0001", $"Cannot cast '{arg}' to xs:double");
        }
        catch (OverflowException)
        {
            throw context.Error("FORG0001", $"Cannot cast '{arg}' to xs:double: value out of range");
        }
    }

    /// <summary>Strict xs:double lexical parse shared with the xs:numeric constructor.</summary>
    internal static double ParseXsDoubleStrict(string s) => ParseXsDouble(s);

    private static double ParseXsDouble(string s)
    {
        if (s == "INF" || s == "+INF") return double.PositiveInfinity;
        if (s == "-INF") return double.NegativeInfinity;
        if (s == "NaN") return double.NaN;
        // Reject case-insensitive variants that .NET would accept (nan, inf, infinity, etc.)
        if (s.Equals("nan", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("inf", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("infinity", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("+infinity", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("-infinity", StringComparison.OrdinalIgnoreCase))
            throw new FormatException($"Invalid xs:double value: '{s}'");
        return double.Parse(s, CultureInfo.InvariantCulture);
    }
}
