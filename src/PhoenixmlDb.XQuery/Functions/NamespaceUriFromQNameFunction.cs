using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:namespace-uri-from-QName($arg) as xs:anyURI?
/// </summary>
public sealed class NamespaceUriFromQNameFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "namespace-uri-from-QName");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.AnyUri, Occurrence = Occurrence.ZeroOrOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = new XdmSequenceType { ItemType = ItemType.QName, Occurrence = Occurrence.ZeroOrOne } }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        // The parameter is xs:QName?, so a node argument is atomized: a validated xs:QName
        // element supplies its typed value. A node was rejected outright.
        var arg = arguments[0] is Xdm.Nodes.XdmNode node
            ? Execution.QueryExecutionContext.AtomizeTyped(node, (context as Execution.QueryExecutionContext)?.NodeProvider)
            : arguments[0];
        if (arg == null) return ValueTask.FromResult<object?>(null);
        // An untyped value is not cast to xs:QName, a namespace-sensitive type: XPTY0117
        // (XPath 3.1 §3.1.5.2). It was reported as XPTY0004 (QT3 FunctionCall-018).
        if (arg is Xdm.XsUntypedAtomic)
            throw context.Error("XPTY0117", $"fn:namespace-uri-from-QName(): an xs:untypedAtomic value is not cast to xs:QName");
        if (arg is not QName qn)
            throw context.Error("XPTY0004", $"fn:namespace-uri-from-QName() requires xs:QName, got {arg.GetType().Name}");

        var nsUri = qn.ResolvedNamespace;
        if (nsUri != null)
            return ValueTask.FromResult<object?>(new PhoenixmlDb.Xdm.XsAnyUri(nsUri));
        var qec = context as PhoenixmlDb.XQuery.Execution.QueryExecutionContext;
        return ValueTask.FromResult<object?>(new PhoenixmlDb.Xdm.XsAnyUri(NamespaceUriFunction.ResolveNsId(qn.Namespace, qec)));
    }
}
