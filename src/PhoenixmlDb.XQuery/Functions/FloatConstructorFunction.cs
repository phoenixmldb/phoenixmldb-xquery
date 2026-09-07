using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>xs:float($arg)</summary>
public sealed class FloatConstructorFunction : TypeConstructorFunction
{
    public FloatConstructorFunction() : base("float") { }

    protected override ValueTask<object?> InvokeCoreAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = AtomizeArg(arguments[0], context);
        if (arg is null) return ValueTask.FromResult<object?>(null);

        try
        {
            var result = arg switch
            {
                float f => f,
                double d => (float)d,
                decimal dc => dc == 0m ? 0.0f : (float)dc,
                long l => (float)l,
                int i => (float)i,
                bool bv => bv ? 1.0f : 0.0f,
                string s => ParseXsFloat(s.Trim()),
                Xdm.XsUntypedAtomic ua => ParseXsFloat(ua.Value.Trim()),
                Xdm.XsAnyUri uri => ParseXsFloat(uri.Value.Trim()),
                _ => Convert.ToSingle(arg, CultureInfo.InvariantCulture)
            };
            return ValueTask.FromResult<object?>(result);
        }
        catch (FormatException)
        {
            throw context.Error("FORG0001", $"Cannot cast '{arg}' to xs:float");
        }
        catch (OverflowException)
        {
            throw context.Error("FORG0001", $"Cannot cast '{arg}' to xs:float: value out of range");
        }
    }

    private static float ParseXsFloat(string s)
    {
        if (s == "INF" || s == "+INF") return float.PositiveInfinity;
        if (s == "-INF") return float.NegativeInfinity;
        if (s == "NaN") return float.NaN;
        // Reject case-insensitive variants that .NET would accept
        if (s.Equals("nan", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("inf", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("infinity", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("+infinity", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("-infinity", StringComparison.OrdinalIgnoreCase))
            throw new FormatException($"Invalid xs:float value: '{s}'");
        return float.Parse(s, CultureInfo.InvariantCulture);
    }
}

// ──────────────────────────────────────────────
// Integer subtypes
// ──────────────────────────────────────────────
