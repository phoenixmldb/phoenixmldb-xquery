using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// The "next" function in a random-number-generator map — returns a new RNG map.
/// </summary>
internal sealed class RngNextFunction : XQueryFunction
{
    private readonly int _seed;
    public RngNextFunction(int seed) => _seed = seed;

    public override QName Name => new(FunctionNamespaces.Fn, "random-number-generator-next");
    public override bool IsAnonymous => true;
    public override XdmSequenceType ReturnType => new()
    {
        ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne,
        MapKeyType = ItemType.String,
        MapValueSequenceType = new XdmSequenceType { ItemType = ItemType.Item, Occurrence = Occurrence.ExactlyOne }
    };
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];


    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        return ValueTask.FromResult<object?>(RandomNumberGeneratorFunction.BuildRngMap(_seed));
    }
}
