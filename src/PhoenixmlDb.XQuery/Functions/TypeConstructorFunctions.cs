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

    /// <summary>Require arg to be string, untypedAtomic, or boolean for derived string type casting.</summary>
    protected static void RequireStringOrUntyped(object arg, string typeName)
    {
        if (arg is string || arg is Xdm.XsUntypedAtomic || arg is bool) return;
        throw new XQueryRuntimeException("FORG0001", $"Cannot cast {arg.GetType().Name} to {typeName}");
    }

    /// <summary>Collapse whitespace per xs:token normalization (trim leading/trailing).</summary>
    protected static string NormalizeWhitespace(string s) => s.Trim();

    /// <summary>Converts an atomic value to its XQuery string representation for derived string type casting.</summary>
    protected static string AtomicToString(object arg) => arg switch
    {
        bool bv => bv ? "true" : "false",
        string s => s,
        Xdm.XsUntypedAtomic ua => ua.Value,
        _ => arg.ToString() ?? ""
    };

    /// <summary>
    /// Validates strict xs:time lexical form: HH:MM:SS[.fff...][timezone].
    /// .NET's DateTimeOffset.Parse is too lenient (accepts spaces, normalizes invalid tz).
    /// </summary>
    internal static void ValidateTimeLexical(string s)
    {
        // HH:MM:SS[.fractional][timezone]
        // Minimum: "HH:MM:SS" = 8 chars
        if (s.Length < 8)
            throw new XQueryRuntimeException("FORG0001", $"Invalid xs:time value: '{s}'");
        // HH
        if (!IsDigit(s[0]) || !IsDigit(s[1]))
            throw new XQueryRuntimeException("FORG0001", $"Invalid xs:time value: '{s}'");
        if (s[2] != ':')
            throw new XQueryRuntimeException("FORG0001", $"Invalid xs:time value: '{s}'");
        // MM
        if (!IsDigit(s[3]) || !IsDigit(s[4]))
            throw new XQueryRuntimeException("FORG0001", $"Invalid xs:time value: '{s}'");
        if (s[5] != ':')
            throw new XQueryRuntimeException("FORG0001", $"Invalid xs:time value: '{s}'");
        // SS
        if (!IsDigit(s[6]) || !IsDigit(s[7]))
            throw new XQueryRuntimeException("FORG0001", $"Invalid xs:time value: '{s}'");
        int i = 8;
        // Optional fractional seconds
        if (i < s.Length && s[i] == '.')
        {
            i++;
            if (i >= s.Length || !IsDigit(s[i]))
                throw new XQueryRuntimeException("FORG0001", $"Invalid xs:time value: '{s}'");
            while (i < s.Length && IsDigit(s[i])) i++;
        }
        // Optional timezone or end
        if (i < s.Length)
            ValidateTimezoneLexical(s, i);
        else if (i != s.Length)
            throw new XQueryRuntimeException("FORG0001", $"Invalid xs:time value: '{s}'");
    }

    /// <summary>
    /// Validates strict xs:date lexical form: [-]YYYY-MM-DD[timezone].
    /// Rejects spaces, 3-digit years, leading +.
    /// </summary>
    internal static void ValidateDateLexical(string s)
    {
        int i = 0;
        if (i < s.Length && s[i] == '-') i++;
        // Year: at least 4 digits
        int yearStart = i;
        while (i < s.Length && IsDigit(s[i])) i++;
        int yearDigits = i - yearStart;
        if (yearDigits < 4)
            throw new XQueryRuntimeException("FORG0001", $"Invalid xs:date value: '{s}'");
        // Separator
        if (i >= s.Length || s[i] != '-')
            throw new XQueryRuntimeException("FORG0001", $"Invalid xs:date value: '{s}'");
        i++;
        // MM
        if (i + 2 > s.Length || !IsDigit(s[i]) || !IsDigit(s[i + 1]))
            throw new XQueryRuntimeException("FORG0001", $"Invalid xs:date value: '{s}'");
        i += 2;
        // Separator
        if (i >= s.Length || s[i] != '-')
            throw new XQueryRuntimeException("FORG0001", $"Invalid xs:date value: '{s}'");
        i++;
        // DD
        if (i + 2 > s.Length || !IsDigit(s[i]) || !IsDigit(s[i + 1]))
            throw new XQueryRuntimeException("FORG0001", $"Invalid xs:date value: '{s}'");
        i += 2;
        // Optional timezone or end
        if (i < s.Length)
            ValidateTimezoneLexical(s, i);
        else if (i != s.Length)
            throw new XQueryRuntimeException("FORG0001", $"Invalid xs:date value: '{s}'");
    }

    /// <summary>
    /// Validates strict xs:dateTime lexical form: [-]YYYY-MM-DDTHH:MM:SS[.fff][timezone].
    /// Must contain 'T' separator between date and time.
    /// </summary>
    internal static void ValidateDateTimeLexical(string s)
    {
        int i = 0;
        if (i < s.Length && s[i] == '-') i++;
        // Year: at least 4 digits
        int yearStart = i;
        while (i < s.Length && IsDigit(s[i])) i++;
        int yearDigits = i - yearStart;
        if (yearDigits < 4)
            throw new XQueryRuntimeException("FORG0001", $"Invalid xs:dateTime value: '{s}'");
        // -MM-DD
        if (i >= s.Length || s[i] != '-')
            throw new XQueryRuntimeException("FORG0001", $"Invalid xs:dateTime value: '{s}'");
        i++;
        if (i + 2 > s.Length || !IsDigit(s[i]) || !IsDigit(s[i + 1]))
            throw new XQueryRuntimeException("FORG0001", $"Invalid xs:dateTime value: '{s}'");
        i += 2;
        if (i >= s.Length || s[i] != '-')
            throw new XQueryRuntimeException("FORG0001", $"Invalid xs:dateTime value: '{s}'");
        i++;
        if (i + 2 > s.Length || !IsDigit(s[i]) || !IsDigit(s[i + 1]))
            throw new XQueryRuntimeException("FORG0001", $"Invalid xs:dateTime value: '{s}'");
        i += 2;
        // T separator is mandatory
        if (i >= s.Length || s[i] != 'T')
            throw new XQueryRuntimeException("FORG0001", $"Invalid xs:dateTime value: '{s}'");
        i++;
        // Time part: HH:MM:SS[.fff][tz]
        if (i + 8 > s.Length)
            throw new XQueryRuntimeException("FORG0001", $"Invalid xs:dateTime value: '{s}'");
        if (!IsDigit(s[i]) || !IsDigit(s[i + 1]) || s[i + 2] != ':' ||
            !IsDigit(s[i + 3]) || !IsDigit(s[i + 4]) || s[i + 5] != ':' ||
            !IsDigit(s[i + 6]) || !IsDigit(s[i + 7]))
            throw new XQueryRuntimeException("FORG0001", $"Invalid xs:dateTime value: '{s}'");
        i += 8;
        // Optional fractional seconds
        if (i < s.Length && s[i] == '.')
        {
            i++;
            if (i >= s.Length || !IsDigit(s[i]))
                throw new XQueryRuntimeException("FORG0001", $"Invalid xs:dateTime value: '{s}'");
            while (i < s.Length && IsDigit(s[i])) i++;
        }
        if (i < s.Length)
            ValidateTimezoneLexical(s, i);
        else if (i != s.Length)
            throw new XQueryRuntimeException("FORG0001", $"Invalid xs:dateTime value: '{s}'");
    }

    /// <summary>Validates timezone lexical form at position i: Z | [+-]HH:MM</summary>
    internal static void ValidateTimezoneLexical(string s, int i)
    {
        if (i >= s.Length)
            throw new XQueryRuntimeException("FORG0001", $"Invalid timezone in value: '{s}'");
        if (s[i] == 'Z')
        {
            if (i + 1 != s.Length)
                throw new XQueryRuntimeException("FORG0001", $"Invalid timezone in value: '{s}'");
            return;
        }
        if (s[i] != '+' && s[i] != '-')
            throw new XQueryRuntimeException("FORG0001", $"Invalid timezone in value: '{s}'");
        i++;
        // Must be exactly HH:MM remaining
        if (i + 5 != s.Length)
            throw new XQueryRuntimeException("FORG0001", $"Invalid timezone in value: '{s}'");
        if (!IsDigit(s[i]) || !IsDigit(s[i + 1]) || s[i + 2] != ':' ||
            !IsDigit(s[i + 3]) || !IsDigit(s[i + 4]))
            throw new XQueryRuntimeException("FORG0001", $"Invalid timezone in value: '{s}'");
        int hh = (s[i] - '0') * 10 + (s[i + 1] - '0');
        int mm = (s[i + 3] - '0') * 10 + (s[i + 4] - '0');
        if (hh > 14 || mm > 59 || (hh == 14 && mm > 0))
            throw new XQueryRuntimeException("FORG0001", $"Invalid timezone in value: '{s}'");
    }

    /// <summary>
    /// Validates xs:duration lexical form: [-]P[nY][nM][nD][T[nH][nM][n[.f]S]].
    /// Rejects: bare "P"/"−P", "T" without following components, decimal without leading/trailing digits,
    /// "H" designation without "T", invalid characters.
    /// </summary>
    internal static void ValidateDurationLexical(string s)
    {
        int i = 0;
        if (i < s.Length && s[i] == '-') i++;
        if (i >= s.Length || s[i] != 'P')
            throw new XQueryRuntimeException("FORG0001", $"Invalid duration value: '{s}'");
        i++; // skip P
        bool hasAnyComponent = false;
        bool inTimePart = false;
        while (i < s.Length)
        {
            char c = s[i];
            if (c == 'T')
            {
                if (inTimePart)
                    throw new XQueryRuntimeException("FORG0001", $"Invalid duration value: '{s}'");
                inTimePart = true;
                i++;
                // T must be followed by at least one time component
                if (i >= s.Length)
                    throw new XQueryRuntimeException("FORG0001", $"Invalid duration value: 'T' must be followed by time components in '{s}'");
                continue;
            }
            if (c == '.')
            {
                // Decimal point must be preceded by digits
                throw new XQueryRuntimeException("FORG0001", $"Invalid duration value: decimal without leading digit in '{s}'");
            }
            if (IsDigit(c))
            {
                // Read digits
                while (i < s.Length && IsDigit(s[i])) i++;
                if (i >= s.Length)
                    throw new XQueryRuntimeException("FORG0001", $"Invalid duration value: digit without designator in '{s}'");
                c = s[i];
                if (c == '.')
                {
                    // Fractional — only valid before S
                    i++;
                    if (i >= s.Length || !IsDigit(s[i]))
                        throw new XQueryRuntimeException("FORG0001", $"Invalid duration value: decimal without trailing digit in '{s}'");
                    while (i < s.Length && IsDigit(s[i])) i++;
                    if (i >= s.Length || s[i] != 'S')
                        throw new XQueryRuntimeException("FORG0001", $"Invalid duration value: fractional only allowed before 'S' in '{s}'");
                    hasAnyComponent = true;
                    i++;
                }
                else if (c == 'Y' || c == 'M' || c == 'D' || c == 'H' || c == 'S')
                {
                    // Validate: H and S only after T
                    if ((c == 'H' || c == 'S') && !inTimePart)
                        throw new XQueryRuntimeException("FORG0001", $"Invalid duration value: '{c}' without 'T' in '{s}'");
                    hasAnyComponent = true;
                    i++;
                }
                else
                {
                    throw new XQueryRuntimeException("FORG0001", $"Invalid duration value: unexpected '{c}' in '{s}'");
                }
                continue;
            }
            throw new XQueryRuntimeException("FORG0001", $"Invalid duration value: unexpected '{c}' in '{s}'");
        }
        if (!hasAnyComponent)
            throw new XQueryRuntimeException("FORG0001", $"Invalid duration value: no components in '{s}'");
    }

    private static bool IsDigit(char c) => c >= '0' && c <= '9';
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
                Xdm.XsUntypedAtomic ua => long.Parse(ua.Value.Trim(), CultureInfo.InvariantCulture),
                Xdm.XsAnyUri uri => long.Parse(uri.Value.Trim(), CultureInfo.InvariantCulture),
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
                Xdm.XsUntypedAtomic ua => decimal.Parse(ua.Value.Trim(), CultureInfo.InvariantCulture),
                Xdm.XsAnyUri uri => decimal.Parse(uri.Value.Trim(), CultureInfo.InvariantCulture),
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
            throw new XQueryException("FORG0001", $"Cannot cast '{arg}' to xs:double");
        }
        catch (OverflowException)
        {
            throw new XQueryException("FORG0001", $"Cannot cast '{arg}' to xs:double: value out of range");
        }
    }

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
            throw new XQueryException("FORG0001", $"Cannot cast '{arg}' to xs:float");
        }
        catch (OverflowException)
        {
            throw new XQueryException("FORG0001", $"Cannot cast '{arg}' to xs:float: value out of range");
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
        var result = ConcatFunction.XQueryStringValue(arg);
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
            string s => ParseXsBoolean(s.Trim()),
            Xdm.XsUntypedAtomic ua => ParseXsBoolean(ua.Value.Trim()),
            Xdm.XsAnyUri uri => ParseXsBoolean(uri.Value.Trim()),
            long l => l != 0,
            int i => i != 0,
            double d => d != 0 && !double.IsNaN(d),
            float f => f != 0 && !float.IsNaN(f),
            decimal d => d != 0,
            _ => Convert.ToBoolean(arg, CultureInfo.InvariantCulture)
        };
        return ValueTask.FromResult<object?>(result);
    }

    private static bool ParseXsBoolean(string s) => s switch
    {
        "true" or "1" => true,
        "false" or "0" => false,
        _ => throw new XQueryRuntimeException("FORG0001", $"Cannot cast '{s}' to xs:boolean: valid values are 'true', 'false', '1', '0'")
    };
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
        // Only xs:string, xs:untypedAtomic, or xs:boolean can be cast to xs:language
        RequireStringOrUntyped(arg, "xs:language");
        var s = NormalizeWhitespace(AtomicToString(arg));
        if (s.Length == 0)
            throw new XQueryRuntimeException("FORG0001", "Empty string is not a valid xs:language");
        // Validate language tag per RFC 4646: [a-zA-Z]{1,8}(-[a-zA-Z0-9]{1,8})*
        if (!IsValidLanguage(s))
            throw new XQueryRuntimeException("FORG0001", $"Invalid xs:language: '{s}'");
        return ValueTask.FromResult<object?>(s);
    }

    private static bool IsValidLanguage(string s)
    {
        var parts = s.Split('-');
        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Length < 1 || part.Length > 8) return false;
            for (int j = 0; j < part.Length; j++)
            {
                var c = part[j];
                if (i == 0)
                {
                    // First subtag: only letters
                    if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))) return false;
                }
                else
                {
                    // Subsequent subtags: letters and digits
                    if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))) return false;
                }
            }
        }
        return true;
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
        RequireStringOrUntyped(arg, "xs:Name");
        var s = NormalizeWhitespace(AtomicToString(arg));
        if (s.Length == 0)
            throw new XQueryRuntimeException("FORG0001", "Empty string is not a valid xs:Name");
        // Validate XML Name: starts with NameStartChar, followed by NameChars
        try { XmlConvert.VerifyName(s); }
        catch { throw new XQueryRuntimeException("FORG0001", $"Invalid xs:Name: '{s}'"); }
        return ValueTask.FromResult<object?>(s);
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
        RequireStringOrUntyped(arg, "xs:NCName");
        var s = NormalizeWhitespace(AtomicToString(arg));
        if (s.Length == 0)
            throw new XQueryRuntimeException("FORG0001", "Empty string is not a valid xs:NCName");
        try { XmlConvert.VerifyNCName(s); }
        catch { throw new XQueryRuntimeException("FORG0001", $"Invalid xs:NCName: '{s}'"); }
        return ValueTask.FromResult<object?>(s);
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
        RequireStringOrUntyped(arg, "xs:NMTOKEN");
        var s = NormalizeWhitespace(AtomicToString(arg));
        if (s.Length == 0)
            throw new XQueryRuntimeException("FORG0001", "Empty string is not a valid xs:NMTOKEN");
        try { XmlConvert.VerifyNMTOKEN(s); }
        catch { throw new XQueryRuntimeException("FORG0001", $"Invalid xs:NMTOKEN: '{s}'"); }
        return ValueTask.FromResult<object?>(s);
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
        RequireStringOrUntyped(arg, "xs:ENTITY");
        var s = NormalizeWhitespace(AtomicToString(arg));
        if (s.Length == 0)
            throw new XQueryRuntimeException("FORG0001", "Empty string is not a valid xs:ENTITY");
        // ENTITY must be a valid NCName
        try { XmlConvert.VerifyNCName(s); }
        catch { throw new XQueryRuntimeException("FORG0001", $"Invalid xs:ENTITY: '{s}'"); }
        return ValueTask.FromResult<object?>(s);
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
        var s = arg is Xdm.XsUntypedAtomic ua ? ua.Value.Trim()
              : arg is Xdm.XsAnyUri uri ? uri.Value.Trim()
              : arg.ToString()!.Trim();
        DateTimeConstructorFunction.ValidateDateYearPrefix(s, "xs:date");
        ValidateDateLexical(s);
        try
        {
            return ValueTask.FromResult<object?>(XsDate.Parse(s));
        }
        catch (XQueryRuntimeException) { throw; }
        catch (Exception ex)
        {
            throw new XQueryRuntimeException("FORG0001", $"Cannot cast '{s}' to xs:date: {ex.Message}");
        }
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
        ValidateTimeLexical(s);
        try { return ValueTask.FromResult<object?>(XsTime.Parse(s)); }
        catch (XQueryRuntimeException) { throw; }
        catch (Exception ex) { throw new XQueryRuntimeException("FORG0001", $"Cannot cast '{s}' to xs:time: {ex.Message}"); }
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
        // xs:date → xs:dateTime: add T00:00:00 time component
        if (arg is XsDate xd)
        {
            var dto = new DateTimeOffset(xd.Date.ToDateTime(TimeOnly.MinValue),
                xd.Timezone ?? TimeSpan.Zero);
            return ValueTask.FromResult<object?>(new XsDateTime(dto, HasTimezone: xd.Timezone.HasValue));
        }
        var s = arg is Xdm.XsUntypedAtomic ua ? ua.Value.Trim()
              : arg is Xdm.XsAnyUri uri ? uri.Value.Trim()
              : arg.ToString()!.Trim();
        // Validate leading + (not allowed) and leading zeros in >4 digit year
        ValidateDateYearPrefix(s, "xs:dateTime");
        ValidateDateTimeLexical(s);
        try { return ValueTask.FromResult<object?>(XsDateTime.Parse(s)); }
        catch (XQueryRuntimeException) { throw; }
        catch (Exception ex) { throw new XQueryRuntimeException("FORG0001", $"Cannot cast '{s}' to xs:dateTime: {ex.Message}"); }
    }

    /// <summary>Validates that a date/dateTime string doesn't have a leading '+' or leading zeros in >4 digit year.</summary>
    internal static void ValidateDateYearPrefix(string s, string typeName)
    {
        if (s.Length == 0) return;
        int i = 0;
        if (s[i] == '+')
            throw new XQueryRuntimeException("FORG0001", $"Leading '+' is not allowed for {typeName}: '{s}'");
        if (s[i] == '-') i++;
        // Count year digits
        int digitStart = i;
        while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
        int digitCount = i - digitStart;
        // If >4 digits, leading zeros are prohibited
        if (digitCount > 4 && s[digitStart] == '0')
            throw new XQueryRuntimeException("FORG0001", $"Leading zeros in year with more than 4 digits not allowed for {typeName}: '{s}'");
    }
}

/// <summary>xs:dateTimeStamp($arg) — xs:dateTime with mandatory timezone</summary>
public sealed class DateTimeStampConstructorFunction : TypeConstructorFunction
{
    public DateTimeStampConstructorFunction() : base("dateTimeStamp") { }

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = QueryExecutionContext.Atomize(arguments[0]);
        if (arg is null) return ValueTask.FromResult<object?>(null);

        XsDateTime result;
        if (arg is XsDateTime xdt)
        {
            result = xdt;
        }
        else if (arg is XsDate xd)
        {
            // Cast xs:date → xs:dateTimeStamp: date must have timezone
            if (xd.Timezone is null)
                throw new XQueryRuntimeException("FORG0001",
                    "Cannot cast xs:date without timezone to xs:dateTimeStamp");
            result = new XsDateTime(
                new DateTimeOffset(xd.Date.ToDateTime(TimeOnly.MinValue), xd.Timezone.Value),
                HasTimezone: true);
        }
        else
        {
            var s = arg.ToString()!.Trim();
            try { result = XsDateTime.Parse(s); }
            catch (XQueryRuntimeException) { throw; }
            catch (Exception ex) { throw new XQueryRuntimeException("FORG0001", $"Cannot cast '{s}' to xs:dateTimeStamp: {ex.Message}"); }
        }

        // xs:dateTimeStamp requires a timezone
        if (!result.HasTimezone)
            throw new XQueryRuntimeException("FORG0001",
                "xs:dateTimeStamp requires a timezone component");

        return ValueTask.FromResult<object?>(result);
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
        // xs:dayTimeDuration → xs:duration (zero months, preserve day-time)
        if (arg is TimeSpan ts)
            return ValueTask.FromResult<object?>(new Xdm.XsDuration(0, ts));
        if (arg is Xdm.DayTimeDuration dtd)
            return ValueTask.FromResult<object?>(new Xdm.XsDuration(0, dtd.ToTimeSpan()));
        // xs:yearMonthDuration → xs:duration (preserve months, zero day-time)
        if (arg is Xdm.YearMonthDuration ymd)
            return ValueTask.FromResult<object?>(new Xdm.XsDuration(ymd.TotalMonths, TimeSpan.Zero));
        var s = arg is Xdm.XsUntypedAtomic ua ? ua.Value.Trim()
              : arg is Xdm.XsAnyUri uri ? uri.Value.Trim()
              : arg.ToString()!.Trim();
        ValidateDurationLexical(s);
        try { return ValueTask.FromResult<object?>(Xdm.XsDuration.Parse(s)); }
        catch (XQueryRuntimeException) { throw; }
        catch (Exception ex) { throw new XQueryRuntimeException("FORG0001", $"Cannot cast '{s}' to xs:duration: {ex.Message}"); }
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
        // xs:duration → xs:dayTimeDuration: extract day-time component, discard months
        if (arg is Xdm.XsDuration dur)
            return ValueTask.FromResult<object?>(dur.DayTime);
        // xs:yearMonthDuration → xs:dayTimeDuration: always PT0S (no day-time component)
        if (arg is Xdm.YearMonthDuration)
            return ValueTask.FromResult<object?>(TimeSpan.Zero);
        var s = arg is Xdm.XsUntypedAtomic ua ? ua.Value.Trim()
              : arg is Xdm.XsAnyUri uri ? uri.Value.Trim()
              : arg.ToString()!.Trim();
        // Validate duration lexical form first (rejects bare P, decimal without digits, etc.)
        ValidateDurationLexical(s);
        // dayTimeDuration must not contain Y or M (month) components
        ValidateDayTimeDurationString(s);
        // Try TimeSpan first (most common case), fall back to DayTimeDuration for overflow
        try
        {
            return ValueTask.FromResult<object?>(XmlConvert.ToTimeSpan(s));
        }
        catch (OverflowException)
        {
            try { return ValueTask.FromResult<object?>(Xdm.DayTimeDuration.Parse(s)); }
            catch (XQueryRuntimeException) { throw; }
            catch (Exception ex) { throw new XQueryRuntimeException("FORG0001", $"Cannot cast '{s}' to xs:dayTimeDuration: {ex.Message}"); }
        }
        catch (XQueryRuntimeException) { throw; }
        catch (Exception ex) { throw new XQueryRuntimeException("FORG0001", $"Cannot cast '{s}' to xs:dayTimeDuration: {ex.Message}"); }
    }

    /// <summary>Validates that a dayTimeDuration string does not contain Y or M (month) components.</summary>
    private static void ValidateDayTimeDurationString(string s)
    {
        // dayTimeDuration lexical form: [-]P[nD][T[nH][nM][nS]]
        // Must NOT contain Y or M-before-T (month) components
        int i = 0;
        if (i < s.Length && s[i] == '-') i++;
        if (i >= s.Length || s[i] != 'P')
            throw new XQueryRuntimeException("FORG0001", $"Cannot cast '{s}' to xs:dayTimeDuration: not a valid duration");
        i++; // skip P
        // Scan before T — only digits and D allowed, not Y or M
        while (i < s.Length && s[i] != 'T')
        {
            if (s[i] == 'Y')
                throw new XQueryRuntimeException("FORG0001", $"Cannot cast '{s}' to xs:dayTimeDuration: year component not allowed");
            if (s[i] == 'M')
                throw new XQueryRuntimeException("FORG0001", $"Cannot cast '{s}' to xs:dayTimeDuration: month component not allowed");
            i++;
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
        // xs:duration → xs:yearMonthDuration: extract month component, discard day-time
        if (arg is Xdm.XsDuration dur)
            return ValueTask.FromResult<object?>(new Xdm.YearMonthDuration(dur.TotalMonths));
        // xs:dayTimeDuration → xs:yearMonthDuration: always P0M (no month component)
        if (arg is TimeSpan)
            return ValueTask.FromResult<object?>(new Xdm.YearMonthDuration(0));
        var s = arg is Xdm.XsUntypedAtomic ua ? ua.Value.Trim()
              : arg is Xdm.XsAnyUri uri ? uri.Value.Trim()
              : arg.ToString()!.Trim();
        ValidateYearMonthDurationString(s);
        try { return ValueTask.FromResult<object?>(Xdm.YearMonthDuration.Parse(s)); }
        catch (XQueryRuntimeException) { throw; }
        catch (Exception ex) { throw new XQueryRuntimeException("FORG0001", $"Cannot cast '{s}' to xs:yearMonthDuration: {ex.Message}"); }
    }

    /// <summary>Validates yearMonthDuration lexical form: [-]P[nY][nM], no timezone, no day-time, must have at least Y or M.</summary>
    private static void ValidateYearMonthDurationString(string s)
    {
        int i = 0;
        if (i < s.Length && s[i] == '-') i++;
        if (i >= s.Length || s[i] != 'P')
            throw new XQueryRuntimeException("FORG0001", $"Cannot cast '{s}' to xs:yearMonthDuration: not a valid duration");
        i++; // skip P
        // After P, expect digits followed by Y and/or digits followed by M, nothing else
        bool hasDesignator = false;
        while (i < s.Length)
        {
            char c = s[i];
            if (c >= '0' && c <= '9') { i++; continue; }
            if (c == 'Y' || c == 'M') { hasDesignator = true; i++; continue; }
            // Any other character (T, D, H, S, +, Z, etc.) is invalid
            if (c == 'D' || c == 'T' || c == 'H' || c == 'S')
                throw new XQueryRuntimeException("FORG0001", $"Cannot cast '{s}' to xs:yearMonthDuration: day-time component not allowed");
            throw new XQueryRuntimeException("FORG0001", $"Cannot cast '{s}' to xs:yearMonthDuration: invalid character '{c}'");
        }
        if (!hasDesignator)
            throw new XQueryRuntimeException("FORG0001", $"Cannot cast '{s}' to xs:yearMonthDuration: must contain Y or M designator");
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
        var s = arg is Xdm.XsUntypedAtomic ua ? ua.Value.Trim()
              : arg is Xdm.XsAnyUri uri ? uri.Value.Trim()
              : arg.ToString()!.Trim();
        if (!ValidateGYear(s))
            throw new XQueryRuntimeException("FORG0001", $"Invalid xs:gYear value: '{s}'");
        return ValueTask.FromResult<object?>(new Xdm.XsGYear(NormalizeTimezone(s)));
    }

    /// <summary>Validates xs:gYear lexical form: -?[0-9]{4,}(Z|[+-][0-9]{2}:[0-9]{2})?</summary>
    private static bool ValidateGYear(string s)
    {
        int i = 0;
        if (i < s.Length && s[i] == '-') i++;
        // Leading '+' is not allowed per XSD
        if (i < s.Length && s[i] == '+') return false;
        int digitStart = i;
        while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
        int digitCount = i - digitStart;
        // Must have at least 4 digits
        if (digitCount < 4) return false;
        // Leading zeros only allowed if digit count is exactly 4
        if (digitCount > 4 && s[digitStart] == '0') return false;
        // Remainder must be empty or a valid timezone
        if (i == s.Length) return true;
        return ValidateTimezone(s, i);
    }

    /// <summary>Validates timezone suffix: Z | [+-]HH:MM</summary>
    internal static bool ValidateTimezone(string s, int i)
    {
        if (i >= s.Length) return true;
        if (s[i] == 'Z') return i + 1 == s.Length;
        if (s[i] != '+' && s[i] != '-') return false;
        i++;
        // HH:MM
        if (i + 5 != s.Length) return false;
        if (s[i] < '0' || s[i] > '9' || s[i + 1] < '0' || s[i + 1] > '9') return false;
        if (s[i + 2] != ':') return false;
        if (s[i + 3] < '0' || s[i + 3] > '9' || s[i + 4] < '0' || s[i + 4] > '9') return false;
        int hh = (s[i] - '0') * 10 + (s[i + 1] - '0');
        int mm = (s[i + 3] - '0') * 10 + (s[i + 4] - '0');
        return hh <= 14 && mm <= 59 && !(hh == 14 && mm > 0);
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
        var s = arg is Xdm.XsUntypedAtomic ua ? ua.Value.Trim()
              : arg is Xdm.XsAnyUri uri ? uri.Value.Trim()
              : arg.ToString()!.Trim();
        if (!ValidateGYearMonth(s))
            throw new XQueryRuntimeException("FORG0001", $"Invalid xs:gYearMonth value: '{s}'");
        return ValueTask.FromResult<object?>(new Xdm.XsGYearMonth(NormalizeTimezone(s)));
    }

    /// <summary>Validates xs:gYearMonth lexical form: -?YYYY-MM(timezone)?</summary>
    private static bool ValidateGYearMonth(string s)
    {
        int i = 0;
        if (i < s.Length && s[i] == '-') i++;
        if (i < s.Length && s[i] == '+') return false;
        int digitStart = i;
        while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
        int digitCount = i - digitStart;
        if (digitCount < 4) return false;
        if (digitCount > 4 && s[digitStart] == '0') return false;
        // Must have -MM
        if (i >= s.Length || s[i] != '-') return false;
        i++;
        if (i + 2 > s.Length) return false;
        if (s[i] < '0' || s[i] > '9' || s[i + 1] < '0' || s[i + 1] > '9') return false;
        int month = (s[i] - '0') * 10 + (s[i + 1] - '0');
        if (month < 1 || month > 12) return false;
        i += 2;
        if (i == s.Length) return true;
        return GYearConstructorFunction.ValidateTimezone(s, i);
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
        var s = arg is Xdm.XsUntypedAtomic ua ? ua.Value.Trim()
              : arg is Xdm.XsAnyUri uri ? uri.Value.Trim()
              : arg.ToString()!.Trim();
        if (!ValidateGMonth(s))
            throw new XQueryRuntimeException("FORG0001", $"Invalid xs:gMonth value: '{s}'");
        return ValueTask.FromResult<object?>(new Xdm.XsGMonth(NormalizeTimezone(s)));
    }

    /// <summary>Validates xs:gMonth lexical form: --MM(timezone)?</summary>
    private static bool ValidateGMonth(string s)
    {
        if (s.Length < 4) return false;
        if (s[0] != '-' || s[1] != '-') return false;
        if (s[2] < '0' || s[2] > '9' || s[3] < '0' || s[3] > '9') return false;
        int month = (s[2] - '0') * 10 + (s[3] - '0');
        if (month < 1 || month > 12) return false;
        if (s.Length == 4) return true;
        return GYearConstructorFunction.ValidateTimezone(s, 4);
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
        var s = arg is Xdm.XsUntypedAtomic ua ? ua.Value.Trim()
              : arg is Xdm.XsAnyUri uri ? uri.Value.Trim()
              : arg.ToString()!.Trim();
        if (!ValidateGMonthDay(s))
            throw new XQueryRuntimeException("FORG0001", $"Invalid xs:gMonthDay value: '{s}'");
        return ValueTask.FromResult<object?>(new Xdm.XsGMonthDay(NormalizeTimezone(s)));
    }

    /// <summary>Validates xs:gMonthDay lexical form: --MM-DD(timezone)?</summary>
    private static bool ValidateGMonthDay(string s)
    {
        if (s.Length < 7) return false;
        if (s[0] != '-' || s[1] != '-') return false;
        if (s[2] < '0' || s[2] > '9' || s[3] < '0' || s[3] > '9') return false;
        int month = (s[2] - '0') * 10 + (s[3] - '0');
        if (month < 1 || month > 12) return false;
        if (s[4] != '-') return false;
        if (s[5] < '0' || s[5] > '9' || s[6] < '0' || s[6] > '9') return false;
        int day = (s[5] - '0') * 10 + (s[6] - '0');
        if (day < 1) return false;
        // Max days per month (Feb uses 29 for leap-year-unaware gMonthDay)
        int maxDay = month switch
        {
            2 => 29,
            4 or 6 or 9 or 11 => 30,
            _ => 31
        };
        if (day > maxDay) return false;
        if (s.Length == 7) return true;
        return GYearConstructorFunction.ValidateTimezone(s, 7);
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
        var s = arg is Xdm.XsUntypedAtomic ua ? ua.Value.Trim()
              : arg is Xdm.XsAnyUri uri ? uri.Value.Trim()
              : arg.ToString()!.Trim();
        if (!ValidateGDay(s))
            throw new XQueryRuntimeException("FORG0001", $"Invalid xs:gDay value: '{s}'");
        return ValueTask.FromResult<object?>(new Xdm.XsGDay(NormalizeTimezone(s)));
    }

    /// <summary>Validates xs:gDay lexical form: ---DD(timezone)?</summary>
    private static bool ValidateGDay(string s)
    {
        if (s.Length < 5) return false;
        if (s[0] != '-' || s[1] != '-' || s[2] != '-') return false;
        if (s[3] < '0' || s[3] > '9' || s[4] < '0' || s[4] > '9') return false;
        int day = (s[3] - '0') * 10 + (s[4] - '0');
        if (day < 1 || day > 31) return false;
        if (s.Length == 5) return true;
        return GYearConstructorFunction.ValidateTimezone(s, 5);
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
        if (s.Length == 0)
            throw new Execution.XQueryRuntimeException("FORG0001", "Cannot cast empty string to xs:QName");
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
            // Built-in predeclared namespace prefixes (always in static context per XPath 3.1 §2.1.1)
            if (string.IsNullOrEmpty(nsUri))
            {
                nsUri = prefix switch
                {
                    "fn" => "http://www.w3.org/2005/xpath-functions",
                    "xs" => "http://www.w3.org/2001/XMLSchema",
                    "xsi" => "http://www.w3.org/2001/XMLSchema-instance",
                    "math" => "http://www.w3.org/2005/xpath-functions/math",
                    "map" => "http://www.w3.org/2005/xpath-functions/map",
                    "array" => "http://www.w3.org/2005/xpath-functions/array",
                    "err" => "http://www.w3.org/2005/xqt-errors",
                    "local" => "http://www.w3.org/2005/xquery-local-functions",
                    "xml" => "http://www.w3.org/XML/1998/namespace",
                    _ => null
                };
            }
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
