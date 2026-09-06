using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:zero-or-one($arg) as item()?
/// </summary>
public sealed class ZeroOrOneFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "zero-or-one");
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalItem;
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
                return ValueTask.FromResult<object?>(null);
            var first = enumerator.Current;
            if (enumerator.MoveNext())
                throw new Execution.XQueryRuntimeException("FORG0003",
                    "fn:zero-or-one called with a sequence of more than one item");
            return ValueTask.FromResult<object?>(first);
        }
        return ValueTask.FromResult<object?>(arg);
    }
}
