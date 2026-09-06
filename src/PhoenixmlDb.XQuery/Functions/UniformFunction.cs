using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:uniform($seq as xs:anyAtomicType*) as xs:boolean
/// Tests whether all values in a sequence are the same (using deep-equal) (XPath 4.0).
/// </summary>
public sealed class UniformFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "uniform");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var seq = arguments[0];
        if (seq == null) return ValueTask.FromResult<object?>(true);
        var items = seq is object?[] arr ? arr : new[] { seq };
        if (items.Length <= 1) return ValueTask.FromResult<object?>(true);

        var first = QueryExecutionContext.Atomize(items[0]);
        for (var i = 1; i < items.Length; i++)
        {
            var item = QueryExecutionContext.Atomize(items[i]);
            if (!Equals(first, item) && !Equals(first?.ToString(), item?.ToString()))
                return ValueTask.FromResult<object?>(false);
        }
        return ValueTask.FromResult<object?>(true);
    }
}
