using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:one-or-more($arg) as item()+
/// </summary>
public sealed class OneOrMoreFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "one-or-more");
    public override XdmSequenceType ReturnType => XdmSequenceType.OneOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg == null)
            throw new Execution.XQueryRuntimeException("FORG0004",
                "fn:one-or-more called with empty sequence");
        if (arg is IEnumerable<object?> seq)
        {
            using var enumerator = seq.GetEnumerator();
            if (!enumerator.MoveNext())
                throw new Execution.XQueryRuntimeException("FORG0004",
                    "fn:one-or-more called with empty sequence");
            // Safe to return the original arg — FunctionCallOperator already materializes arguments
            return ValueTask.FromResult<object?>(arg);
        }
        return ValueTask.FromResult<object?>(arg);
    }
}
