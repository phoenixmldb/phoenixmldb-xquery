using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

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
