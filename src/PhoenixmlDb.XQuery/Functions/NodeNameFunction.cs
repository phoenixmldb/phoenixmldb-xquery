using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:node-name($arg) as xs:QName?
/// </summary>
public sealed class NodeNameFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "node-name");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.QName, Occurrence = Occurrence.ZeroOrOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalNode }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg != null && arg is not XdmNode)
            throw new Execution.XQueryRuntimeException("XPTY0004",
                $"fn:node-name expects a node, got {arg.GetType().Name}");
        return ValueTask.FromResult<object?>(NodeNameOf(arg, context));
    }

    internal static QName? NodeNameOf(object? node, Ast.ExecutionContext context)
    {
        var qec = context as Execution.QueryExecutionContext;
        var resolver = qec?.NamespaceResolver;
        // A node of another store (xslt BUGS.md #124): its namespace id is that store's, so take
        // the URI from there and give the name this context's id for it.
        if (qec != null && node is XdmNode { Store: { } own } foreign && !qec.IsContextStore(own)
            && foreign is XdmElement or XdmAttribute)
        {
            var (ns, local, prefix) = foreign is XdmElement fe
                ? (fe.Namespace, fe.LocalName, fe.Prefix)
                : (((XdmAttribute)foreign).Namespace, ((XdmAttribute)foreign).LocalName, ((XdmAttribute)foreign).Prefix);
            var uri = ns == NamespaceId.None ? "" : own.GetNamespaceUri(ns) ?? "";
            return new QName(Execution.QueryExecutionContext.RuntimeNamespaceId(qec, uri), local, prefix) { RuntimeNamespace = uri };
        }
        return node switch
        {
            XdmElement elem => new QName(elem.Namespace, elem.LocalName, elem.Prefix)
                { RuntimeNamespace = resolver?.Invoke(elem.Namespace) ?? "" },
            XdmAttribute attr => new QName(attr.Namespace, attr.LocalName, attr.Prefix)
                { RuntimeNamespace = resolver?.Invoke(attr.Namespace) ?? "" },
            XdmProcessingInstruction pi => new QName(NamespaceId.None, pi.Target)
                { RuntimeNamespace = "" },
            XdmNamespace ns => !string.IsNullOrEmpty(ns.Prefix)
                ? new QName(NamespaceId.None, ns.Prefix) { RuntimeNamespace = "" }
                : null, // default namespace node has no name
            _ => null
        };
    }
}
