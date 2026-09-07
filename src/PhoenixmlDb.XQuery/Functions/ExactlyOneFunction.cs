using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:exactly-one($arg) as item()
/// </summary>
public sealed class ExactlyOneFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "exactly-one");
    public override XdmSequenceType ReturnType => XdmSequenceType.Item;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is IEnumerable<object?> seq)
        {
            using var enumerator = seq.GetEnumerator();
            if (!enumerator.MoveNext())
                throw new Execution.XQueryRuntimeException("FORG0005",
                    "fn:exactly-one called with a sequence of 0 items");
            var first = enumerator.Current;
            if (enumerator.MoveNext())
                throw new Execution.XQueryRuntimeException("FORG0005",
                    "fn:exactly-one called with a sequence of more than one item");
            return ValueTask.FromResult(first);
        }
        if (arg == null)
        {
            throw new Execution.XQueryRuntimeException("FORG0005",
                "fn:exactly-one called with empty sequence");
        }
        return ValueTask.FromResult<object?>(arg);
    }
}
