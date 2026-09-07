using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:while-do($input, $predicate, $action) as item()* — XPath 4.0.
/// </summary>
/// <remarks>
/// Applies <c>$action</c> to <c>$input</c> repeatedly for as long as <c>$predicate</c> holds,
/// returning the first value for which it does not. The predicate is tested BEFORE each
/// application, so an input that already fails it is returned untouched.
///
/// Missing until real 4.0 code asked for it: the Generators library builds its whole
/// to-array/iteration machinery on while-do.
/// </remarks>
public sealed class WhileDoFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "while-do");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "input"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "predicate"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } },
        new() { Name = new QName(NamespaceId.None, "action"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } }
    ];

    // A generator-driven loop is unbounded by nature, so a runaway predicate would hang the
    // process rather than fail. This bound turns that into a diagnosable error.
    private const int MaxIterations = 1_000_000;

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var current = arguments[0];
        var predicate = arguments[1];
        var action = arguments[2];

        for (var i = 0; i < MaxIterations; i++)
        {
            var verdict = await CallableCoercion.InvokeUnaryAsync(predicate, current, context).ConfigureAwait(false);
            if (!QueryExecutionContext.EffectiveBooleanValue(verdict))
                return current;
            current = await CallableCoercion.InvokeUnaryAsync(action, current, context).ConfigureAwait(false);
        }

        throw new XQueryRuntimeException("FOAR0002",
            $"fn:while-do exceeded {MaxIterations} iterations — the predicate never became false.");
    }
}
