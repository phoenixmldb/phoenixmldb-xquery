using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:count($arg) as xs:integer
/// </summary>
public sealed class CountFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "count");
    public override XdmSequenceType ReturnType => XdmSequenceType.Integer;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        if (arguments[0] is IEnumerable<object?> items)
            return ValueTask.FromResult<object?>((long)items.Count());
        if (arguments[0] == null)
            return ValueTask.FromResult<object?>((long)0);
        return ValueTask.FromResult<object?>((long)1);
    }
}
