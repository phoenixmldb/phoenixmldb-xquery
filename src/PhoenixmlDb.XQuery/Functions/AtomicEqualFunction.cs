using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:atomic-equal($a as xs:anyAtomicType, $b as xs:anyAtomicType) as xs:boolean
/// Tests atomic value equality without type promotion (XPath 4.0).
/// </summary>
public sealed class AtomicEqualFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "atomic-equal");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "a"), Type = XdmSequenceType.Item },
        new() { Name = new QName(NamespaceId.None, "b"), Type = XdmSequenceType.Item }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var a = QueryExecutionContext.Atomize(arguments[0]);
        var b = QueryExecutionContext.Atomize(arguments[1]);
        if (a == null && b == null) return ValueTask.FromResult<object?>(true);
        if (a == null || b == null) return ValueTask.FromResult<object?>(false);
        // Strict equality: same type and same value
        if (a.GetType() != b.GetType()) return ValueTask.FromResult<object?>(false);
        return ValueTask.FromResult<object?>(Equals(a, b));
    }
}
