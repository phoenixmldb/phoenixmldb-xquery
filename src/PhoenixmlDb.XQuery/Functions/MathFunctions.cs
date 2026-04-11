using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// math:pi() as xs:double
/// </summary>
public sealed class MathPiFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Math, "pi");
    public override XdmSequenceType ReturnType => XdmSequenceType.Double;
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        return ValueTask.FromResult<object?>(Math.PI);
    }
}

/// <summary>
/// math:e() as xs:double — Euler's number (XPath 4.0).
/// </summary>
public sealed class MathEFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Math, "e");
    public override XdmSequenceType ReturnType => XdmSequenceType.Double;
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
        => ValueTask.FromResult<object?>(Math.E);
}

/// <summary>
/// math:exp($arg) as xs:double?
/// </summary>
public sealed class MathExpFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Math, "exp");
    public override XdmSequenceType ReturnType => XdmSequenceType.Double;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.Double }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        if (arguments[0] is null) return ValueTask.FromResult<object?>(null);
        return ValueTask.FromResult<object?>(Math.Exp(NumericParseHelper.ValidateAndConvertToDouble(arguments[0], "math:exp")));
    }
}

/// <summary>
/// math:exp10($arg) as xs:double?
/// </summary>
public sealed class MathExp10Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Math, "exp10");
    public override XdmSequenceType ReturnType => XdmSequenceType.Double;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.Double }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        if (arguments[0] is null) return ValueTask.FromResult<object?>(null);
        return ValueTask.FromResult<object?>(Math.Pow(10, NumericParseHelper.ValidateAndConvertToDouble(arguments[0], "math:exp10")));
    }
}

/// <summary>
/// math:log($arg) as xs:double?
/// </summary>
public sealed class MathLogFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Math, "log");
    public override XdmSequenceType ReturnType => XdmSequenceType.Double;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.Double }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        if (arguments[0] is null) return ValueTask.FromResult<object?>(null);
        return ValueTask.FromResult<object?>(Math.Log(NumericParseHelper.ValidateAndConvertToDouble(arguments[0], "math:log")));
    }
}

/// <summary>
/// math:log10($arg) as xs:double?
/// </summary>
public sealed class MathLog10Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Math, "log10");
    public override XdmSequenceType ReturnType => XdmSequenceType.Double;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.Double }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        if (arguments[0] is null) return ValueTask.FromResult<object?>(null);
        return ValueTask.FromResult<object?>(Math.Log10(NumericParseHelper.ValidateAndConvertToDouble(arguments[0], "math:log10")));
    }
}

/// <summary>
/// math:pow($base, $exponent) as xs:double?
/// </summary>
public sealed class MathPowFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Math, "pow");
    public override XdmSequenceType ReturnType => XdmSequenceType.Double;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [
            new() { Name = new QName(NamespaceId.None, "base"), Type = XdmSequenceType.Double },
            new() { Name = new QName(NamespaceId.None, "exponent"), Type = XdmSequenceType.Double }
        ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        if (arguments[0] is null) return ValueTask.FromResult<object?>(null);
        return ValueTask.FromResult<object?>(
            Math.Pow(NumericParseHelper.ValidateAndConvertToDouble(arguments[0], "math:pow"),
                     NumericParseHelper.ValidateAndConvertToDouble(arguments[1], "math:pow")));
    }
}

/// <summary>
/// math:sqrt($arg) as xs:double?
/// </summary>
public sealed class MathSqrtFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Math, "sqrt");
    public override XdmSequenceType ReturnType => XdmSequenceType.Double;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.Double }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        if (arguments[0] is null) return ValueTask.FromResult<object?>(null);
        return ValueTask.FromResult<object?>(Math.Sqrt(NumericParseHelper.ValidateAndConvertToDouble(arguments[0], "math:sqrt")));
    }
}

/// <summary>
/// math:sin($arg) as xs:double?
/// </summary>
public sealed class MathSinFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Math, "sin");
    public override XdmSequenceType ReturnType => XdmSequenceType.Double;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.Double }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        if (arguments[0] is null) return ValueTask.FromResult<object?>(null);
        return ValueTask.FromResult<object?>(Math.Sin(NumericParseHelper.ValidateAndConvertToDouble(arguments[0], "math:sin")));
    }
}

/// <summary>
/// math:cos($arg) as xs:double?
/// </summary>
public sealed class MathCosFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Math, "cos");
    public override XdmSequenceType ReturnType => XdmSequenceType.Double;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.Double }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        if (arguments[0] is null) return ValueTask.FromResult<object?>(null);
        return ValueTask.FromResult<object?>(Math.Cos(NumericParseHelper.ValidateAndConvertToDouble(arguments[0], "math:cos")));
    }
}

/// <summary>
/// math:tan($arg) as xs:double?
/// </summary>
public sealed class MathTanFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Math, "tan");
    public override XdmSequenceType ReturnType => XdmSequenceType.Double;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.Double }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        if (arguments[0] is null) return ValueTask.FromResult<object?>(null);
        return ValueTask.FromResult<object?>(Math.Tan(NumericParseHelper.ValidateAndConvertToDouble(arguments[0], "math:tan")));
    }
}

/// <summary>
/// math:asin($arg) as xs:double?
/// </summary>
public sealed class MathAsinFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Math, "asin");
    public override XdmSequenceType ReturnType => XdmSequenceType.Double;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.Double }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        if (arguments[0] is null) return ValueTask.FromResult<object?>(null);
        return ValueTask.FromResult<object?>(Math.Asin(NumericParseHelper.ValidateAndConvertToDouble(arguments[0], "math:asin")));
    }
}

/// <summary>
/// math:acos($arg) as xs:double?
/// </summary>
public sealed class MathAcosFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Math, "acos");
    public override XdmSequenceType ReturnType => XdmSequenceType.Double;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.Double }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        if (arguments[0] is null) return ValueTask.FromResult<object?>(null);
        return ValueTask.FromResult<object?>(Math.Acos(NumericParseHelper.ValidateAndConvertToDouble(arguments[0], "math:acos")));
    }
}

/// <summary>
/// math:atan($arg) as xs:double?
/// </summary>
public sealed class MathAtanFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Math, "atan");
    public override XdmSequenceType ReturnType => XdmSequenceType.Double;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.Double }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        if (arguments[0] is null) return ValueTask.FromResult<object?>(null);
        return ValueTask.FromResult<object?>(Math.Atan(NumericParseHelper.ValidateAndConvertToDouble(arguments[0], "math:atan")));
    }
}

/// <summary>
/// math:atan2($y, $x) as xs:double
/// </summary>
public sealed class MathAtan2Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Math, "atan2");
    public override XdmSequenceType ReturnType => XdmSequenceType.Double;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [
            new() { Name = new QName(NamespaceId.None, "y"), Type = XdmSequenceType.Double },
            new() { Name = new QName(NamespaceId.None, "x"), Type = XdmSequenceType.Double }
        ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        return ValueTask.FromResult<object?>(
            Math.Atan2(NumericParseHelper.ValidateAndConvertToDouble(arguments[0], "math:atan2"),
                       NumericParseHelper.ValidateAndConvertToDouble(arguments[1], "math:atan2")));
    }
}

/// <summary>
/// fn:function-lookup($name as xs:QName, $arity as xs:integer) as function(*)?
/// </summary>
public sealed class FunctionLookupFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "function-lookup");
    public override XdmSequenceType ReturnType => XdmSequenceType.Item;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [
            new() { Name = new QName(NamespaceId.None, "name"), Type = XdmSequenceType.Item },
            new() { Name = new QName(NamespaceId.None, "arity"), Type = XdmSequenceType.Integer }
        ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var name = arguments[0];
        var arity = Convert.ToInt32(arguments[1]);

        QName qname;
        if (name is QName qn)
        {
            qname = qn;
        }
        else
        {
            // String form: try to parse as Clark notation or local name
            var nameStr = name?.ToString() ?? "";
            qname = new QName(FunctionNamespaces.Fn, nameStr);
        }

        if (context is QueryExecutionContext qec)
        {
            var func = qec.Functions.Resolve(qname, arity);
            if (func == null)
                return ValueTask.FromResult<object?>(null);
            // Per XPath 3.1 §3.1.6, if function-lookup resolves to a context-dependent
            // function (e.g., fn:static-base-uri#0, fn:name#0, fn:position#0), the
            // dynamic context in force at the lookup call site must be captured so that
            // later invocation of the returned function uses that context — not the
            // caller's current context at the time of invocation.
            if (arity == 0
                && PhoenixmlDb.XQuery.Execution.NamedFunctionRefOperator.IsContextCaptureFunction(qname))
            {
                object? capturedItem;
                try { capturedItem = qec.ContextItem; }
                catch (Execution.XQueryRuntimeException) { capturedItem = Execution.QueryExecutionContext.AbsentFocus; }
                return ValueTask.FromResult<object?>(
                    new Execution.ContextBoundFunctionRef(func, capturedItem, qec.StaticBaseUri));
            }
            return ValueTask.FromResult<object?>(func);
        }
        return ValueTask.FromResult<object?>(null);
    }
}

/// <summary>
/// fn:function-name($func as function(*)) as xs:QName?
/// </summary>
public sealed class FunctionNameFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "function-name");
    public override XdmSequenceType ReturnType => XdmSequenceType.Item;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "func"), Type = XdmSequenceType.Item }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        if (arguments[0] is XQueryFunction func)
        {
            // Anonymous functions (inline functions, closures) have no name per XPath spec
            if (func.IsAnonymous)
                return ValueTask.FromResult<object?>(null);
            var name = func.Name;
            // Synthesize a conventional prefix for well-known namespaces if missing,
            // so the returned QName round-trips through serialization as e.g. "fn:function-name".
            string? effectivePrefix = name.Prefix;
            if (effectivePrefix == null && name.Namespace != NamespaceId.None)
            {
                if (name.Namespace == FunctionNamespaces.Fn) effectivePrefix = "fn";
                else if (name.Namespace == FunctionNamespaces.Xs) effectivePrefix = "xs";
                else if (name.Namespace == FunctionNamespaces.Math) effectivePrefix = "math";
                else if (name.Namespace == FunctionNamespaces.Map) effectivePrefix = "map";
                else if (name.Namespace == FunctionNamespaces.Array) effectivePrefix = "array";
                else if (name.Namespace == FunctionNamespaces.Local) effectivePrefix = "local";
            }
            // Ensure the namespace URI is resolvable for namespace-uri-from-QName()
            string? nsUri = null;
            if (name.ResolvedNamespace == null && name.Namespace != NamespaceId.None)
            {
                nsUri = FunctionNamespaces.ResolveNamespace(name.Namespace);
                if (nsUri == null)
                {
                    var resolver = (context as PhoenixmlDb.XQuery.Execution.QueryExecutionContext)?.NamespaceResolver;
                    nsUri = resolver?.Invoke(name.Namespace);
                }
            }
            if (effectivePrefix != name.Prefix || nsUri != null)
            {
                name = new QName(name.Namespace, name.LocalName, effectivePrefix)
                {
                    RuntimeNamespace = nsUri ?? name.RuntimeNamespace
                };
            }
            return ValueTask.FromResult<object?>(name);
        }
        // Maps and arrays are also callable (function items per XPath 3.1)
        if (arguments[0] is IDictionary<object, object?> || arguments[0] is List<object?>)
            return ValueTask.FromResult<object?>(null); // anonymous
        throw new Execution.XQueryRuntimeException("XPTY0004",
            $"Argument to fn:function-name is not a function (got {arguments[0]?.GetType().Name ?? "empty sequence"})");
    }
}

/// <summary>
/// fn:function-arity($func as function(*)) as xs:integer
/// </summary>
public sealed class FunctionArityFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "function-arity");
    public override XdmSequenceType ReturnType => XdmSequenceType.Integer;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "func"), Type = XdmSequenceType.Item }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        if (arguments[0] is XQueryFunction func)
        {
            return ValueTask.FromResult<object?>((long)func.Arity);
        }
        // Maps and arrays are callable as functions — arity is 1
        if (arguments[0] is IDictionary<object, object?> || arguments[0] is List<object?>)
            return ValueTask.FromResult<object?>(1L);
        throw new XQueryRuntimeException("XPTY0004", "Argument is not a function");
    }
}

/// <summary>
/// fn:random-number-generator() as map(xs:string, item())
/// fn:random-number-generator($seed as xs:anyAtomicType?) as map(xs:string, item())
/// Returns a map with keys: "number" (random double), "next" (function returning next RNG map),
/// "permute" (function that randomly permutes a sequence).
/// </summary>
public sealed class RandomNumberGeneratorFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "random-number-generator");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "seed"), Type = new() { ItemType = ItemType.AnyAtomicType, Occurrence = Occurrence.ZeroOrOne } }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var seed = arguments.Count > 0 ? arguments[0] : null;
        var rngSeed = seed != null ? seed.GetHashCode() : Environment.TickCount;
        return ValueTask.FromResult<object?>(BuildRngMap(rngSeed));
    }

    internal static IDictionary<object, object?> BuildRngMap(int seed)
    {
#pragma warning disable CA5394 // Deterministic PRNG required by XPath spec for reproducible results
        var rng = new Random(seed);
        var number = rng.NextDouble();
        var nextSeed = rng.Next();

        var map = new Dictionary<object, object?>
        {
            ["number"] = number,
            ["next"] = new RngNextFunction(nextSeed),
            ["permute"] = new RngPermuteFunction(seed)
        };
#pragma warning restore CA5394
        return map;
    }
}

/// <summary>
/// Zero-arity fn:random-number-generator() (no seed — uses system randomness).
/// </summary>
public sealed class RandomNumberGenerator0Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "random-number-generator");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];


    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        return ValueTask.FromResult<object?>(RandomNumberGeneratorFunction.BuildRngMap(Environment.TickCount));
    }
}

/// <summary>
/// The "next" function in a random-number-generator map — returns a new RNG map.
/// </summary>
internal sealed class RngNextFunction : XQueryFunction
{
    private readonly int _seed;
    public RngNextFunction(int seed) => _seed = seed;

    public override QName Name => new(FunctionNamespaces.Fn, "random-number-generator-next");
    public override bool IsAnonymous => true;
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];


    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        return ValueTask.FromResult<object?>(RandomNumberGeneratorFunction.BuildRngMap(_seed));
    }
}

/// <summary>
/// The "permute" function in a random-number-generator map — randomly permutes a sequence.
/// </summary>
internal sealed class RngPermuteFunction : XQueryFunction
{
    private readonly int _seed;
    public RngPermuteFunction(int seed) => _seed = seed;

    public override QName Name => new(FunctionNamespaces.Fn, "random-number-generator-permute");
    public override bool IsAnonymous => true;
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "input"), Type = XdmSequenceType.ZeroOrMoreItems }
    ];


    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var input = arguments[0];
        var items = new List<object?>();

        if (input is object?[] arr)
            items.AddRange(arr);
        else if (input != null)
            items.Add(input);

        if (items.Count <= 1)
            return ValueTask.FromResult<object?>(input);

        // Fisher-Yates shuffle with deterministic seed
#pragma warning disable CA5394 // Deterministic PRNG required by XPath spec for reproducible results
        var rng = new Random(_seed);
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
#pragma warning restore CA5394

        return ValueTask.FromResult<object?>(items.ToArray());
    }
}
