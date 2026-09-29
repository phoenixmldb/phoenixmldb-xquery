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
        var provider = (context as QueryExecutionContext)?.NodeProvider;
        var a = QueryExecutionContext.AtomizeTyped(arguments[0], provider);
        var b = QueryExecutionContext.AtomizeTyped(arguments[1], provider);
        if (a == null && b == null) return ValueTask.FromResult<object?>(true);
        if (a == null || b == null) return ValueTask.FromResult<object?>(false);
        // fn:atomic-equal is op:same-key (F&O 4.0 §14.2.1), the map-key equality: xs:string,
        // xs:anyURI and xs:untypedAtomic compare by codepoints, numerics by value across types.
        // It used to require the same CLR type, so atomic-equal(xs:untypedAtomic('a'), 'a') and
        // atomic-equal(1, 1.0) were false.
        return ValueTask.FromResult<object?>(XdmMapKeyComparer.Instance.Equals(a, b));
    }
}
