using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// Base class for XSD type constructor functions (xs:integer, xs:double, etc.).
/// These are single-argument functions in the http://www.w3.org/2001/XMLSchema namespace.
/// </summary>
public abstract class TypeConstructorFunction : XQueryFunction
{
    private readonly string _typeName;

    protected TypeConstructorFunction(string typeName)
    {
        _typeName = typeName;
    }

    public override QName Name => new(FunctionNamespaces.Xs, _typeName);
    public override XdmSequenceType ReturnType => XdmSequenceType.Item;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.Item }];

    /// <summary>
    /// Normalizes +00:00 and -00:00 timezone suffixes to Z (canonical form per XSD).
    /// </summary>
    protected static string NormalizeTimezone(string value)
    {
        if (value.EndsWith("+00:00", StringComparison.Ordinal) || value.EndsWith("-00:00", StringComparison.Ordinal))
            return string.Concat(value.AsSpan(0, value.Length - 6), "Z");
        return value;
    }
}

// ──────────────────────────────────────────────
// Numeric type constructors
// ──────────────────────────────────────────────

/// <summary>xs:integer($arg)</summary>
public sealed class IntegerConstructorFunction : TypeConstructorFunction
{
    public IntegerConstructorFunction() : base("integer") { }

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = QueryExecutionContext.Atomize(arguments[0]);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        try
        {
            var result = arg switch
            {
                long l => l,
                int i => (long)i,
                short s => (long)s,
                byte b => (long)b,
                decimal d => (long)d,
                double dbl => double.IsNaN(dbl) || double.IsInfinity(dbl) || dbl >= 9.2233720368547758e18 || dbl < -9.2233720368547758e18
                    ? throw new XQueryException("FOCA0003", $"xs:double value {dbl} out of range for xs:integer")
                    : (long)dbl,
                float f => float.IsNaN(f) || float.IsInfinity(f) || f >= 9.2233720368547758e18f || f < -9.2233720368547758e18f
                    ? throw new XQueryException("FOCA0003", $"xs:float value {f} out of range for xs:integer")
                    : (long)f,
                bool bv => bv ? 1L : 0L,
                string s => long.Parse(s.Trim(), CultureInfo.InvariantCulture),
                _ => Convert.ToInt64(arg, CultureInfo.InvariantCulture)
            };
            return ValueTask.FromResult<object?>(result);
        }
        catch (FormatException)
        {
            throw new XQueryException("FORG0001", $"Cannot cast '{arg}' to xs:integer");
        }
        catch (OverflowException)
        {
            throw new XQueryException("FORG0001", $"Cannot cast '{arg}' to xs:integer: value out of range");
        }
    }
}

/// <summary>xs:decimal($arg)</summary>
public sealed class DecimalConstructorFunction : TypeConstructorFunction
{
    public DecimalConstructorFunction() : base("decimal") { }

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = QueryExecutionContext.Atomize(arguments[0]);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        try
        {
            var result = arg switch
            {
                decimal d => d,
                long l => (decimal)l,
                int i => (decimal)i,
                double dbl => (decimal)dbl,
                float f => (decimal)f,
                bool bv => bv ? 1m : 0m,
                string s => decimal.Parse(s.Trim(), CultureInfo.InvariantCulture),
                _ => Convert.ToDecimal(arg, CultureInfo.InvariantCulture)
            };
            return ValueTask.FromResult<object?>(result);
        }
        catch (FormatException)
        {
            throw new XQueryException("FORG0001", $"Cannot cast '{arg}' to xs:decimal");
        }
        catch (OverflowException)
        {
            throw new XQueryException("FORG0001", $"Cannot cast '{arg}' to xs:decimal: value out of range");
        }
    }
}

/// <summary>xs:double($arg)</summary>
public sealed class DoubleConstructorFunction : TypeConstructorFunction
{
    public DoubleConstructorFunction() : base("double") { }

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = QueryExecutionContext.Atomize(arguments[0]);
        if (arg is null) return ValueTask.FromResult<object?>(null);

        try
        {
            var result = arg switch
            {
                double d => d,
                float f => (double)f,
                decimal d => (double)d,
                long l => (double)l,
                int i => (double)i,
                bool bv => bv ? 1.0 : 0.0,
                string s => ParseXsDouble(s.Trim()),
                _ => Convert.ToDouble(arg, CultureInfo.InvariantCulture)
            };
            return ValueTask.FromResult<object?>(result);
        }
        catch (FormatException)
        {
            throw new XQueryException("FORG0001", $"Cannot cast '{arg}' to xs:double");
        }
        catch (OverflowException)
        {
            throw new XQueryException("FORG0001", $"Cannot cast '{arg}' to xs:double: value out of range");
        }
    }

    private static double ParseXsDouble(string s) => s switch
    {
        "INF" => double.PositiveInfinity,
        "-INF" => double.NegativeInfinity,
        "NaN" => double.NaN,
        _ => double.Parse(s, CultureInfo.InvariantCulture)
    };
}

/// <summary>xs:float($arg)</summary>
public sealed class FloatConstructorFunction : TypeConstructorFunction
{
    public FloatConstructorFunction() : base("float") { }

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = QueryExecutionContext.Atomize(arguments[0]);
        if (arg is null) return ValueTask.FromResult<object?>(null);

        try
        {
            var result = arg switch
            {
                float f => f,
                double d => (float)d,
                decimal dc => (float)dc,
                long l => (float)l,
                int i => (float)i,
                bool bv => bv ? 1.0f : 0.0f,
                string s => ParseXsFloat(s.Trim()),
                _ => Convert.ToSingle(arg, CultureInfo.InvariantCulture)
            };
            return ValueTask.FromResult<object?>(result);
        }
        catch (FormatException)
        {
            throw new XQueryException("FORG0001", $"Cannot cast '{arg}' to xs:float");
        }
        catch (OverflowException)
        {
            throw new XQueryException("FORG0001", $"Cannot cast '{arg}' to xs:float: value out of range");
        }
    }

    private static float ParseXsFloat(string s) => s switch
    {
        "INF" => float.PositiveInfinity,
        "-INF" => float.NegativeInfinity,
        "NaN" => float.NaN,
        _ => float.Parse(s, CultureInfo.InvariantCulture)
    };
}

// ──────────────────────────────────────────────
// Integer subtypes
// ──────────────────────────────────────────────

/// <summary>xs:int($arg)</summary>
public sealed class IntConstructorFunction : TypeConstructorFunction
{
    public IntConstructorFunction() : base("int") { }

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = QueryExecutionContext.Atomize(arguments[0]);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        var result = arg switch
        {
            int i => (long)i,
            long l => EnsureRange(l, int.MinValue, int.MaxValue),
            string s => (long)int.Parse(s.Trim(), CultureInfo.InvariantCulture),
            _ => (long)Convert.ToInt32(arg, CultureInfo.InvariantCulture)
        };
        return ValueTask.FromResult<object?>(result);
    }

    private static long EnsureRange(long val, long min, long max) =>
        val >= min && val <= max ? val : throw new OverflowException($"Value {val} out of range for xs:int");
}

/// <summary>xs:long($arg)</summary>
public sealed class LongConstructorFunction : TypeConstructorFunction
{
    public LongConstructorFunction() : base("long") { }

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = QueryExecutionContext.Atomize(arguments[0]);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        var result = arg switch
        {
            long l => l,
            int i => (long)i,
            string s => long.Parse(s.Trim(), CultureInfo.InvariantCulture),
            _ => Convert.ToInt64(arg, CultureInfo.InvariantCulture)
        };
        return ValueTask.FromResult<object?>(result);
    }
}

/// <summary>xs:short($arg)</summary>
public sealed class ShortConstructorFunction : TypeConstructorFunction
{
    public ShortConstructorFunction() : base("short") { }

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = QueryExecutionContext.Atomize(arguments[0]);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        var result = arg switch
        {
            long l => EnsureRange(l, short.MinValue, short.MaxValue),
            int i => EnsureRange(i, short.MinValue, short.MaxValue),
            string s => (long)short.Parse(s.Trim(), CultureInfo.InvariantCulture),
            _ => (long)Convert.ToInt16(arg, CultureInfo.InvariantCulture)
        };
        return ValueTask.FromResult<object?>(result);
    }

    private static long EnsureRange(long val, long min, long max) =>
        val >= min && val <= max ? val : throw new OverflowException($"Value {val} out of range for xs:short");
}

/// <summary>xs:byte($arg)</summary>
public sealed class ByteConstructorFunction : TypeConstructorFunction
{
    public ByteConstructorFunction() : base("byte") { }

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = QueryExecutionContext.Atomize(arguments[0]);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        // xs:byte is signed (-128 to 127) in XSD
        var result = arg switch
        {
            long l => EnsureRange(l, sbyte.MinValue, sbyte.MaxValue),
            int i => EnsureRange(i, sbyte.MinValue, sbyte.MaxValue),
            string s => (long)sbyte.Parse(s.Trim(), CultureInfo.InvariantCulture),
            _ => (long)Convert.ToSByte(arg, CultureInfo.InvariantCulture)
        };
        return ValueTask.FromResult<object?>(result);
    }

    private static long EnsureRange(long val, long min, long max) =>
        val >= min && val <= max ? val : throw new OverflowException($"Value {val} out of range for xs:byte");
}

/// <summary>xs:unsignedLong($arg)</summary>
public sealed class UnsignedLongConstructorFunction : TypeConstructorFunction
{
    public UnsignedLongConstructorFunction() : base("unsignedLong") { }

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = QueryExecutionContext.Atomize(arguments[0]);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        // Return as long since that's the engine's integer representation
        var result = arg switch
        {
            long l when l >= 0 => l,
            long l => throw new OverflowException($"Value {l} out of range for xs:unsignedLong"),
            int i when i >= 0 => (long)i,
            int i => throw new OverflowException($"Value {i} out of range for xs:unsignedLong"),
            string s => checked((long)ulong.Parse(s.Trim(), CultureInfo.InvariantCulture)),
            _ => Convert.ToInt64(arg, CultureInfo.InvariantCulture)
        };
        return ValueTask.FromResult<object?>(result);
    }
}

/// <summary>xs:unsignedInt($arg)</summary>
public sealed class UnsignedIntConstructorFunction : TypeConstructorFunction
{
    public UnsignedIntConstructorFunction() : base("unsignedInt") { }

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = QueryExecutionContext.Atomize(arguments[0]);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        var result = arg switch
        {
            long l => EnsureRange(l, 0, uint.MaxValue),
            int i when i >= 0 => (long)i,
            int i => throw new OverflowException($"Value {i} out of range for xs:unsignedInt"),
            string s => (long)uint.Parse(s.Trim(), CultureInfo.InvariantCulture),
            _ => (long)Convert.ToUInt32(arg, CultureInfo.InvariantCulture)
        };
        return ValueTask.FromResult<object?>(result);
    }

    private static long EnsureRange(long val, long min, long max) =>
        val >= min && val <= max ? val : throw new OverflowException($"Value {val} out of range for xs:unsignedInt");
}

/// <summary>xs:unsignedShort($arg)</summary>
public sealed class UnsignedShortConstructorFunction : TypeConstructorFunction
{
    public UnsignedShortConstructorFunction() : base("unsignedShort") { }

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = QueryExecutionContext.Atomize(arguments[0]);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        var result = arg switch
        {
            long l => EnsureRange(l, 0, ushort.MaxValue),
            int i when i >= 0 => (long)i,
            string s => (long)ushort.Parse(s.Trim(), CultureInfo.InvariantCulture),
            _ => (long)Convert.ToUInt16(arg, CultureInfo.InvariantCulture)
        };
        return ValueTask.FromResult<object?>(result);
    }

    private static long EnsureRange(long val, long min, long max) =>
        val >= min && val <= max ? val : throw new OverflowException($"Value {val} out of range for xs:unsignedShort");
}

/// <summary>xs:unsignedByte($arg)</summary>
public sealed class UnsignedByteConstructorFunction : TypeConstructorFunction
{
    public UnsignedByteConstructorFunction() : base("unsignedByte") { }

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = QueryExecutionContext.Atomize(arguments[0]);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        var result = arg switch
        {
            long l => EnsureRange(l, byte.MinValue, byte.MaxValue),
            int i => EnsureRange(i, byte.MinValue, byte.MaxValue),
            string s => (long)byte.Parse(s.Trim(), CultureInfo.InvariantCulture),
            _ => (long)Convert.ToByte(arg, CultureInfo.InvariantCulture)
        };
        return ValueTask.FromResult<object?>(result);
    }

    private static long EnsureRange(long val, long min, long max) =>
        val >= min && val <= max ? val : throw new OverflowException($"Value {val} out of range for xs:unsignedByte");
}

/// <summary>xs:positiveInteger($arg)</summary>
public sealed class PositiveIntegerConstructorFunction : TypeConstructorFunction
{
    public PositiveIntegerConstructorFunction() : base("positiveInteger") { }

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = QueryExecutionContext.Atomize(arguments[0]);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        var val = arg switch
        {
            long l => l,
            int i => (long)i,
            string s => long.Parse(s.Trim(), CultureInfo.InvariantCulture),
            _ => Convert.ToInt64(arg, CultureInfo.InvariantCulture)
        };
        return val > 0
            ? ValueTask.FromResult<object?>((long)val)
            : throw new OverflowException($"Value {val} out of range for xs:positiveInteger (must be > 0)");
    }
}

/// <summary>xs:nonNegativeInteger($arg)</summary>
public sealed class NonNegativeIntegerConstructorFunction : TypeConstructorFunction
{
    public NonNegativeIntegerConstructorFunction() : base("nonNegativeInteger") { }

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = QueryExecutionContext.Atomize(arguments[0]);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        var val = arg switch
        {
            long l => l,
            int i => (long)i,
            string s => long.Parse(s.Trim(), CultureInfo.InvariantCulture),
            _ => Convert.ToInt64(arg, CultureInfo.InvariantCulture)
        };
        return val >= 0
            ? ValueTask.FromResult<object?>((long)val)
            : throw new OverflowException($"Value {val} out of range for xs:nonNegativeInteger (must be >= 0)");
    }
}

/// <summary>xs:negativeInteger($arg)</summary>
public sealed class NegativeIntegerConstructorFunction : TypeConstructorFunction
{
    public NegativeIntegerConstructorFunction() : base("negativeInteger") { }

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = QueryExecutionContext.Atomize(arguments[0]);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        var val = arg switch
        {
            long l => l,
            int i => (long)i,
            string s => long.Parse(s.Trim(), CultureInfo.InvariantCulture),
            _ => Convert.ToInt64(arg, CultureInfo.InvariantCulture)
        };
        return val < 0
            ? ValueTask.FromResult<object?>((long)val)
            : throw new OverflowException($"Value {val} out of range for xs:negativeInteger (must be < 0)");
    }
}

/// <summary>xs:nonPositiveInteger($arg)</summary>
public sealed class NonPositiveIntegerConstructorFunction : TypeConstructorFunction
{
    public NonPositiveIntegerConstructorFunction() : base("nonPositiveInteger") { }

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = QueryExecutionContext.Atomize(arguments[0]);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        var val = arg switch
        {
            long l => l,
            int i => (long)i,
            string s => long.Parse(s.Trim(), CultureInfo.InvariantCulture),
            _ => Convert.ToInt64(arg, CultureInfo.InvariantCulture)
        };
        return val <= 0
            ? ValueTask.FromResult<object?>((long)val)
            : throw new OverflowException($"Value {val} out of range for xs:nonPositiveInteger (must be <= 0)");
    }
}

// ──────────────────────────────────────────────
// String/URI type constructors
// ──────────────────────────────────────────────

/// <summary>xs:string($arg)</summary>
public sealed class StringConstructorFunction : TypeConstructorFunction
{
    public StringConstructorFunction() : base("string") { }

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = QueryExecutionContext.Atomize(arguments[0]);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        var result = arg switch
        {
            string s => s,
            bool bv => bv ? "true" : "false",
            double d => ConcatFunction.FormatDoubleXPath(d),
            float f => ConcatFunction.FormatFloatXPath(f),
            decimal m => m.ToString("G29", CultureInfo.InvariantCulture),
            IFormattable fmt => fmt.ToString(null, CultureInfo.InvariantCulture),
            _ => arg.ToString() ?? ""
        };
        return ValueTask.FromResult<object?>(result);
    }
}

/// <summary>xs:boolean($arg)</summary>
public sealed class BooleanConstructorFunction : TypeConstructorFunction
{
    public BooleanConstructorFunction() : base("boolean") { }

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = QueryExecutionContext.Atomize(arguments[0]);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        var result = arg switch
        {
            bool b => b,
            string s => s.Trim() is "true" or "1",
            long l => l != 0,
            int i => i != 0,
            double d => d != 0 && !double.IsNaN(d),
            float f => f != 0 && !float.IsNaN(f),
            decimal d => d != 0,
            _ => Convert.ToBoolean(arg, CultureInfo.InvariantCulture)
        };
        return ValueTask.FromResult<object?>(result);
    }
}

/// <summary>xs:anyURI($arg)</summary>
public sealed class AnyUriConstructorFunction : TypeConstructorFunction
{
    public AnyUriConstructorFunction() : base("anyURI") { }

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = QueryExecutionContext.Atomize(arguments[0]);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        return ValueTask.FromResult<object?>(new Xdm.XsAnyUri(arg.ToString() ?? ""));
    }
}

/// <summary>xs:untypedAtomic($arg)</summary>
public sealed class UntypedAtomicConstructorFunction : TypeConstructorFunction
{
    public UntypedAtomicConstructorFunction() : base("untypedAtomic") { }

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = QueryExecutionContext.Atomize(arguments[0]);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        return ValueTask.FromResult<object?>(new Xdm.XsUntypedAtomic(arg.ToString() ?? ""));
    }
}

/// <summary>xs:normalizedString($arg)</summary>
public sealed class NormalizedStringConstructorFunction : TypeConstructorFunction
{
    public NormalizedStringConstructorFunction() : base("normalizedString") { }

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = QueryExecutionContext.Atomize(arguments[0]);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        // Replace tabs, newlines, carriage returns with spaces
        var s = (arg.ToString() ?? "")
            .Replace('\t', ' ')
            .Replace('\n', ' ')
            .Replace('\r', ' ');
        return ValueTask.FromResult<object?>(s);
    }
}

/// <summary>xs:token($arg)</summary>
public sealed class TokenConstructorFunction : TypeConstructorFunction
{
    public TokenConstructorFunction() : base("token") { }

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = QueryExecutionContext.Atomize(arguments[0]);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        // Normalize: replace tabs/newlines/CR with spaces, collapse multiple spaces, strip leading/trailing
        var s = (arg.ToString() ?? "")
            .Replace('\t', ' ')
            .Replace('\n', ' ')
            .Replace('\r', ' ');
        // Collapse runs of spaces
        while (s.Contains("  ", StringComparison.Ordinal))
            s = s.Replace("  ", " ");
        s = s.Trim();
        return ValueTask.FromResult<object?>(s);
    }
}

/// <summary>xs:language($arg)</summary>
public sealed class LanguageConstructorFunction : TypeConstructorFunction
{
    public LanguageConstructorFunction() : base("language") { }

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = QueryExecutionContext.Atomize(arguments[0]);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        return ValueTask.FromResult<object?>(arg.ToString() ?? "");
    }
}

/// <summary>xs:Name($arg)</summary>
public sealed class NameConstructorFunction : TypeConstructorFunction
{
    public NameConstructorFunction() : base("Name") { }

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = QueryExecutionContext.Atomize(arguments[0]);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        return ValueTask.FromResult<object?>(arg.ToString() ?? "");
    }
}

/// <summary>xs:NCName($arg)</summary>
public sealed class NCNameConstructorFunction : TypeConstructorFunction
{
    public NCNameConstructorFunction() : base("NCName") { }

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = QueryExecutionContext.Atomize(arguments[0]);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        return ValueTask.FromResult<object?>(arg.ToString() ?? "");
    }
}

/// <summary>xs:NMTOKEN($arg)</summary>
public sealed class NMTokenConstructorFunction : TypeConstructorFunction
{
    public NMTokenConstructorFunction() : base("NMTOKEN") { }

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = QueryExecutionContext.Atomize(arguments[0]);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        return ValueTask.FromResult<object?>(arg.ToString() ?? "");
    }
}

/// <summary>xs:ENTITY($arg)</summary>
public sealed class EntityConstructorFunction : TypeConstructorFunction
{
    public EntityConstructorFunction() : base("ENTITY") { }

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = QueryExecutionContext.Atomize(arguments[0]);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        return ValueTask.FromResult<object?>(arg.ToString() ?? "");
    }
}

/// <summary>
/// xs:NMTOKENS($arg) — XSD list type constructor.
/// Splits whitespace-separated string into a sequence of xs:NMTOKEN values.
/// </summary>
public sealed class NMTokensConstructorFunction : TypeConstructorFunction
{
    public NMTokensConstructorFunction() : base("NMTOKENS") { }

    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.String, Occurrence = Occurrence.ZeroOrMore };

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = QueryExecutionContext.Atomize(arguments[0]);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        var s = arg.ToString() ?? "";
        var tokens = s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return ValueTask.FromResult<object?>(tokens.Cast<object?>().ToArray());
    }
}

/// <summary>
/// xs:IDREFS($arg) — XSD list type constructor.
/// Splits whitespace-separated string into a sequence of xs:IDREF values.
/// </summary>
public sealed class IDRefsConstructorFunction : TypeConstructorFunction
{
    public IDRefsConstructorFunction() : base("IDREFS") { }

    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.String, Occurrence = Occurrence.ZeroOrMore };

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = QueryExecutionContext.Atomize(arguments[0]);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        var s = arg.ToString() ?? "";
        var tokens = s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return ValueTask.FromResult<object?>(tokens.Cast<object?>().ToArray());
    }
}

/// <summary>
/// xs:ENTITIES($arg) — XSD list type constructor.
/// Splits whitespace-separated string into a sequence of xs:ENTITY values.
/// </summary>
public sealed class EntitiesConstructorFunction : TypeConstructorFunction
{
    public EntitiesConstructorFunction() : base("ENTITIES") { }

    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.String, Occurrence = Occurrence.ZeroOrMore };

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = QueryExecutionContext.Atomize(arguments[0]);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        var s = arg.ToString() ?? "";
        var tokens = s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return ValueTask.FromResult<object?>(tokens.Cast<object?>().ToArray());
    }
}

// ──────────────────────────────────────────────
// Date/Time type constructors
// ──────────────────────────────────────────────

/// <summary>xs:date($arg)</summary>
public sealed class DateConstructorFunction : TypeConstructorFunction
{
    public DateConstructorFunction() : base("date") { }

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = QueryExecutionContext.Atomize(arguments[0]);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        if (arg is XsDate xd) return ValueTask.FromResult<object?>(xd);
        if (arg is DateOnly d) return ValueTask.FromResult<object?>(new XsDate(d, null));
        if (arg is XsDateTime xdt) return ValueTask.FromResult<object?>(new XsDate(DateOnly.FromDateTime(xdt.Value.DateTime), xdt.HasTimezone ? xdt.Value.Offset : null));
        if (arg is DateTimeOffset dto) return ValueTask.FromResult<object?>(new XsDate(DateOnly.FromDateTime(dto.DateTime), dto.Offset));
        var s = arg.ToString()!.Trim();
        return ValueTask.FromResult<object?>(XsDate.Parse(s));
    }
}

/// <summary>xs:time($arg)</summary>
public sealed class TimeConstructorFunction : TypeConstructorFunction
{
    public TimeConstructorFunction() : base("time") { }

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = QueryExecutionContext.Atomize(arguments[0]);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        if (arg is XsTime xt) return ValueTask.FromResult<object?>(xt);
        if (arg is TimeOnly t) return ValueTask.FromResult<object?>(new XsTime(t, null, (int)(t.Ticks % TimeSpan.TicksPerSecond)));
        if (arg is XsDateTime xdt) return ValueTask.FromResult<object?>(new XsTime(TimeOnly.FromDateTime(xdt.Value.DateTime), xdt.HasTimezone ? xdt.Value.Offset : null, xdt.FractionalTicks));
        if (arg is DateTimeOffset dto) return ValueTask.FromResult<object?>(new XsTime(TimeOnly.FromDateTime(dto.DateTime), dto.Offset, (int)(dto.Ticks % TimeSpan.TicksPerSecond)));
        var s = arg.ToString()!.Trim();
        return ValueTask.FromResult<object?>(XsTime.Parse(s));
    }
}

/// <summary>xs:dateTime($arg)</summary>
public sealed class DateTimeConstructorFunction : TypeConstructorFunction
{
    public DateTimeConstructorFunction() : base("dateTime") { }

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = QueryExecutionContext.Atomize(arguments[0]);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        if (arg is XsDateTime xdt) return ValueTask.FromResult<object?>(xdt);
        var s = arg.ToString()!.Trim();
        return ValueTask.FromResult<object?>(XsDateTime.Parse(s));
    }
}

/// <summary>xs:duration($arg)</summary>
public sealed class DurationConstructorFunction : TypeConstructorFunction
{
    public DurationConstructorFunction() : base("duration") { }

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = QueryExecutionContext.Atomize(arguments[0]);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        if (arg is Xdm.XsDuration d) return ValueTask.FromResult<object?>(d);
        var s = arg.ToString()!.Trim();
        return ValueTask.FromResult<object?>(Xdm.XsDuration.Parse(s));
    }
}

/// <summary>xs:dayTimeDuration($arg)</summary>
public sealed class DayTimeDurationConstructorFunction : TypeConstructorFunction
{
    public DayTimeDurationConstructorFunction() : base("dayTimeDuration") { }

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = QueryExecutionContext.Atomize(arguments[0]);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        if (arg is Xdm.DayTimeDuration dtd) return ValueTask.FromResult<object?>(dtd);
        if (arg is TimeSpan ts) return ValueTask.FromResult<object?>(ts);
        var s = arg.ToString()!.Trim();
        // Try TimeSpan first (most common case), fall back to DayTimeDuration for overflow
        try
        {
            return ValueTask.FromResult<object?>(XmlConvert.ToTimeSpan(s));
        }
        catch (OverflowException)
        {
            return ValueTask.FromResult<object?>(Xdm.DayTimeDuration.Parse(s));
        }
    }
}

/// <summary>xs:yearMonthDuration($arg)</summary>
public sealed class YearMonthDurationConstructorFunction : TypeConstructorFunction
{
    public YearMonthDurationConstructorFunction() : base("yearMonthDuration") { }

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = QueryExecutionContext.Atomize(arguments[0]);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        if (arg is Xdm.YearMonthDuration ymd) return ValueTask.FromResult<object?>(ymd);
        var s = arg.ToString()!.Trim();
        return ValueTask.FromResult<object?>(Xdm.YearMonthDuration.Parse(s));
    }
}

/// <summary>xs:gYear($arg)</summary>
public sealed class GYearConstructorFunction : TypeConstructorFunction
{
    public GYearConstructorFunction() : base("gYear") { }

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = QueryExecutionContext.Atomize(arguments[0]);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        if (arg is Xdm.XsGYear existing) return ValueTask.FromResult<object?>(existing);
        if (arg is Xdm.XsDate d)
            return ValueTask.FromResult<object?>(new Xdm.XsGYear(FormatGYear(d.EffectiveYear, d.Timezone)));
        if (arg is Xdm.XsDateTime dt)
            return ValueTask.FromResult<object?>(new Xdm.XsGYear(FormatGYear(dt.EffectiveYear, dt.HasTimezone ? dt.Value.Offset : null)));
        return ValueTask.FromResult<object?>(new Xdm.XsGYear(NormalizeTimezone(arg.ToString()!.Trim())));
    }

    private static string FormatGYear(long year, TimeSpan? tz)
    {
        var sb = new System.Text.StringBuilder(16);
        if (year < 0) { sb.Append('-'); sb.Append((-year).ToString("D4", System.Globalization.CultureInfo.InvariantCulture)); }
        else sb.Append(year.ToString("D4", System.Globalization.CultureInfo.InvariantCulture));
        Xdm.XsDate.AppendTimezone(sb, tz);
        return sb.ToString();
    }
}

/// <summary>xs:gYearMonth($arg)</summary>
public sealed class GYearMonthConstructorFunction : TypeConstructorFunction
{
    public GYearMonthConstructorFunction() : base("gYearMonth") { }

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = QueryExecutionContext.Atomize(arguments[0]);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        if (arg is Xdm.XsGYearMonth existing) return ValueTask.FromResult<object?>(existing);
        if (arg is Xdm.XsDate d)
            return ValueTask.FromResult<object?>(new Xdm.XsGYearMonth(FormatGYearMonth(d.EffectiveYear, d.Date.Month, d.Timezone)));
        if (arg is Xdm.XsDateTime dt)
            return ValueTask.FromResult<object?>(new Xdm.XsGYearMonth(FormatGYearMonth(dt.EffectiveYear, dt.Value.Month, dt.HasTimezone ? dt.Value.Offset : null)));
        return ValueTask.FromResult<object?>(new Xdm.XsGYearMonth(NormalizeTimezone(arg.ToString()!.Trim())));
    }

    private static string FormatGYearMonth(long year, int month, TimeSpan? tz)
    {
        var sb = new System.Text.StringBuilder(20);
        if (year < 0) { sb.Append('-'); sb.Append((-year).ToString("D4", System.Globalization.CultureInfo.InvariantCulture)); }
        else sb.Append(year.ToString("D4", System.Globalization.CultureInfo.InvariantCulture));
        sb.Append('-');
        sb.Append(month.ToString("D2", System.Globalization.CultureInfo.InvariantCulture));
        Xdm.XsDate.AppendTimezone(sb, tz);
        return sb.ToString();
    }
}

/// <summary>xs:gMonth($arg)</summary>
public sealed class GMonthConstructorFunction : TypeConstructorFunction
{
    public GMonthConstructorFunction() : base("gMonth") { }

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = QueryExecutionContext.Atomize(arguments[0]);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        if (arg is Xdm.XsGMonth existing) return ValueTask.FromResult<object?>(existing);
        if (arg is Xdm.XsDate d)
            return ValueTask.FromResult<object?>(new Xdm.XsGMonth(FormatGMonth(d.Date.Month, d.Timezone)));
        if (arg is Xdm.XsDateTime dt)
            return ValueTask.FromResult<object?>(new Xdm.XsGMonth(FormatGMonth(dt.Value.Month, dt.HasTimezone ? dt.Value.Offset : null)));
        return ValueTask.FromResult<object?>(new Xdm.XsGMonth(NormalizeTimezone(arg.ToString()!.Trim())));
    }

    private static string FormatGMonth(int month, TimeSpan? tz)
    {
        var sb = new System.Text.StringBuilder(12);
        sb.Append("--");
        sb.Append(month.ToString("D2", System.Globalization.CultureInfo.InvariantCulture));
        Xdm.XsDate.AppendTimezone(sb, tz);
        return sb.ToString();
    }
}

/// <summary>xs:gMonthDay($arg)</summary>
public sealed class GMonthDayConstructorFunction : TypeConstructorFunction
{
    public GMonthDayConstructorFunction() : base("gMonthDay") { }

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = QueryExecutionContext.Atomize(arguments[0]);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        if (arg is Xdm.XsGMonthDay existing) return ValueTask.FromResult<object?>(existing);
        if (arg is Xdm.XsDate d)
            return ValueTask.FromResult<object?>(new Xdm.XsGMonthDay(FormatGMonthDay(d.Date.Month, d.Date.Day, d.Timezone)));
        if (arg is Xdm.XsDateTime dt)
            return ValueTask.FromResult<object?>(new Xdm.XsGMonthDay(FormatGMonthDay(dt.Value.Month, dt.Value.Day, dt.HasTimezone ? dt.Value.Offset : null)));
        return ValueTask.FromResult<object?>(new Xdm.XsGMonthDay(NormalizeTimezone(arg.ToString()!.Trim())));
    }

    private static string FormatGMonthDay(int month, int day, TimeSpan? tz)
    {
        var sb = new System.Text.StringBuilder(14);
        sb.Append("--");
        sb.Append(month.ToString("D2", System.Globalization.CultureInfo.InvariantCulture));
        sb.Append('-');
        sb.Append(day.ToString("D2", System.Globalization.CultureInfo.InvariantCulture));
        Xdm.XsDate.AppendTimezone(sb, tz);
        return sb.ToString();
    }
}

/// <summary>xs:gDay($arg)</summary>
public sealed class GDayConstructorFunction : TypeConstructorFunction
{
    public GDayConstructorFunction() : base("gDay") { }

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = QueryExecutionContext.Atomize(arguments[0]);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        if (arg is Xdm.XsGDay existing) return ValueTask.FromResult<object?>(existing);
        if (arg is Xdm.XsDate d)
            return ValueTask.FromResult<object?>(new Xdm.XsGDay(FormatGDay(d.Date.Day, d.Timezone)));
        if (arg is Xdm.XsDateTime dt)
            return ValueTask.FromResult<object?>(new Xdm.XsGDay(FormatGDay(dt.Value.Day, dt.HasTimezone ? dt.Value.Offset : null)));
        return ValueTask.FromResult<object?>(new Xdm.XsGDay(NormalizeTimezone(arg.ToString()!.Trim())));
    }

    private static string FormatGDay(int day, TimeSpan? tz)
    {
        var sb = new System.Text.StringBuilder(12);
        sb.Append("---");
        sb.Append(day.ToString("D2", System.Globalization.CultureInfo.InvariantCulture));
        Xdm.XsDate.AppendTimezone(sb, tz);
        return sb.ToString();
    }
}

// ──────────────────────────────────────────────
// Binary type constructors
// ──────────────────────────────────────────────

/// <summary>xs:hexBinary($arg)</summary>
public sealed class HexBinaryConstructorFunction : TypeConstructorFunction
{
    public HexBinaryConstructorFunction() : base("hexBinary") { }

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = QueryExecutionContext.Atomize(arguments[0]);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        // Handle XdmValue binary inputs (cast from base64Binary to hexBinary or identity)
        if (arg is XdmValue xv && xv.RawValue is byte[] xvBytes)
            return ValueTask.FromResult<object?>(XdmValue.HexBinary(xvBytes));
        if (arg is byte[] bytes) return ValueTask.FromResult<object?>(XdmValue.HexBinary(bytes));
        var hex = arg.ToString()!.Trim();
        try
        {
            var data = Convert.FromHexString(hex);
            return ValueTask.FromResult<object?>(XdmValue.HexBinary(data));
        }
        catch (FormatException)
        {
            throw new InvalidOperationException($"FORG0001: Invalid value for xs:hexBinary: '{hex}'");
        }
    }
}

/// <summary>xs:base64Binary($arg)</summary>
public sealed class Base64BinaryConstructorFunction : TypeConstructorFunction
{
    public Base64BinaryConstructorFunction() : base("base64Binary") { }

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = QueryExecutionContext.Atomize(arguments[0]);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        // Handle XdmValue binary inputs (cast from hexBinary to base64Binary or identity)
        if (arg is XdmValue xv && xv.RawValue is byte[] xvBytes)
            return ValueTask.FromResult<object?>(XdmValue.Base64Binary(xvBytes));
        if (arg is byte[] bytes) return ValueTask.FromResult<object?>(XdmValue.Base64Binary(bytes));
        var b64 = arg.ToString()!.Trim();
        try
        {
            var data = Convert.FromBase64String(b64);
            return ValueTask.FromResult<object?>(XdmValue.Base64Binary(data));
        }
        catch (FormatException)
        {
            throw new InvalidOperationException($"FORG0001: Invalid value for xs:base64Binary: '{b64}'");
        }
    }
}

/// <summary>xs:QName($arg) - parses prefixed name into a QName value</summary>
public sealed class QNameConstructorFunction : TypeConstructorFunction
{
    public QNameConstructorFunction() : base("QName") { }

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = QueryExecutionContext.Atomize(arguments[0]);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        if (arg is QName q) return ValueTask.FromResult<object?>(q);
        var s = arg.ToString()!.Trim();
        var colonIdx = s.IndexOf(':', StringComparison.Ordinal);
        if (colonIdx > 0)
        {
            var prefix = s[..colonIdx];
            var localName = s[(colonIdx + 1)..];
            // Resolve prefix via in-scope namespace bindings
            string? nsUri = null;
            var qec = context as PhoenixmlDb.XQuery.Execution.QueryExecutionContext;
            var bindings = qec?.PrefixNamespaceBindings;
            if (bindings != null)
                bindings.TryGetValue(prefix, out nsUri);
            // FONS0004: prefix must resolve to a statically-known namespace
            if (string.IsNullOrEmpty(nsUri))
                throw new Execution.XQueryRuntimeException("FONS0004",
                    $"No namespace binding for prefix '{prefix}' in xs:QName()");
            var nsId = new NamespaceId((uint)Math.Abs(nsUri.GetHashCode()));
            return ValueTask.FromResult<object?>(new QName(nsId, localName, prefix) { RuntimeNamespace = nsUri });
        }
        return ValueTask.FromResult<object?>(new QName(NamespaceId.None, s));
    }
}
