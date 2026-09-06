using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

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
        return ValueTask.FromResult<object?>(RandomNumberGeneratorFunction.BuildRngMap(RandomNumberGeneratorFunction.DefaultSeed));
    }
}
