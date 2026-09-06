using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:random-number-generator() as map(xs:string, item())
/// fn:random-number-generator($seed as xs:anyAtomicType?) as map(xs:string, item())
/// Returns a map with keys: "number" (random double), "next" (function returning next RNG map),
/// "permute" (function that randomly permutes a sequence).
/// </summary>
public sealed class RandomNumberGeneratorFunction : XQueryFunction
{
    /// <summary>Default seed used when no seed is provided or the seed is the empty sequence.</summary>
    internal const int DefaultSeed = 0;

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
        var rngSeed = seed != null ? seed.GetHashCode() : DefaultSeed;
        return ValueTask.FromResult<object?>(BuildRngMap(rngSeed));
    }

    internal static IDictionary<object, object?> BuildRngMap(int seed)
    {
#pragma warning disable CA5394 // Deterministic PRNG required by XPath spec for reproducible results
        var rng = new Random(seed);
        var number = rng.NextDouble();
        var nextSeed = rng.Next();

        var map = new OrderedXdmMap(XdmMapKeyComparer.Instance)
        {
            ["number"] = number,
            ["next"] = new RngNextFunction(nextSeed),
            ["permute"] = new RngPermuteFunction(seed)
        };
#pragma warning restore CA5394
        return map;
    }
}
