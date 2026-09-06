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

    /// <summary>
    /// Runs the constructor and translates the CLR conversion exceptions into the XQuery error
    /// codes the spec assigns. A constructor function is defined as equivalent to a cast, so it
    /// must report the same codes — but only the CAST path was wrapped (TypeCastHelper.CastValue),
    /// leaving `xs:nonNegativeInteger("--0")` to surface .NET's "The input string '--0' was not in
    /// a correct format." The two halves of one spec rule disagreed; this makes them symmetric.
    /// <para>
    /// Mapping matches CastValue, and was checked against the corpus rather than assumed: in
    /// cast/constructor context FormatException is FORG0001 in 57 of 57 cases, and
    /// OverflowException is FOCA0002 in 78 against FORG0001 in 29 — so FOCA0002 is the majority
    /// answer, not a certainty, and the 29 stay wrong (with a proper code) until the split is
    /// understood.
    /// </para>
    /// <para>
    /// The wrapper is here and not at the call site because every call site is an
    /// `async IAsyncEnumerable` iterator, where C# forbids `yield return` inside a `try`/`catch`.
    /// </para>
    /// </summary>
    public sealed override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        try
        {
            return await InvokeCoreAsync(arguments, context).ConfigureAwait(false);
        }
        catch (FormatException ex)
        {
            throw new Execution.XQueryRuntimeException("FORG0001",
                $"Invalid lexical value for xs:{_typeName}: {ex.Message}", ex);
        }
        catch (OverflowException ex)
        {
            throw new Execution.XQueryRuntimeException("FOCA0002",
                $"Value out of range for xs:{_typeName}: {ex.Message}", ex);
        }
        catch (InvalidCastException ex)
        {
            throw new Execution.XQueryRuntimeException("XPTY0004",
                $"Cannot construct xs:{_typeName} from this operand type", ex);
        }
    }

    /// <summary>The constructor's own logic. Wrapped by <see cref="InvokeAsync"/>.</summary>
    protected abstract ValueTask<object?> InvokeCoreAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context);

    public override QName Name => new(FunctionNamespaces.Xs, _typeName);
    public override XdmSequenceType ReturnType => XdmSequenceType.Item;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.Item }];

    /// <summary>
    /// Atomizes a constructor argument and unwraps any XsTypedInteger so the
    /// receiving constructor can pattern-match on the underlying CLR <c>long</c>.
    /// Without this, casting a wrapped value via another constructor (e.g.
    /// <c>xs:double(xs:long(3))</c>) hits Convert.ToDouble(XsTypedInteger)
    /// which throws — the wrapper carries a subtype tag for instance-of identity
    /// but is value-transparent for cast/arithmetic.
    /// </summary>
    protected static object? AtomizeArg(object? value, Ast.ExecutionContext context)
    {
        // Route through the execution context's node provider so storage-deserialized
        // elements (NULL precomputed StringValue, lazily-resolved children) atomize
        // correctly via descendant text-node walking, mirroring fn:string() (#160).
        var nodeProvider = (context as QueryExecutionContext)?.NodeProvider;
        var atomized = QueryExecutionContext.Atomize(value, nodeProvider);
        if (atomized is Xdm.XsTypedInteger ti) return ti.Value;
        return atomized;
    }

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
