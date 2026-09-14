using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:namespace-uri($arg) as xs:anyURI
/// </summary>
public sealed class NamespaceUriFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "namespace-uri");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.AnyUri, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalNode }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0] is object[] arr ? (arr.Length > 0 ? arr[0] : null) : arguments[0];
        if (arg == null)
            return ValueTask.FromResult<object?>("");

        var qec = context as PhoenixmlDb.XQuery.Execution.QueryExecutionContext;
        return arg switch
        {
            XdmElement elem => ValueTask.FromResult<object?>(ResolveNsId(elem.Namespace, qec)),
            XdmAttribute attr => ValueTask.FromResult<object?>(ResolveNsId(attr.Namespace, qec)),
            XdmNamespace => ValueTask.FromResult<object?>(""),
            _ => ValueTask.FromResult<object?>("")
        };
    }

    internal static string ResolveNsId(NamespaceId id, Execution.QueryExecutionContext? qec)
    {
        if (id == NamespaceId.None) return "";
        // Try explicit namespace resolver first
        if (qec?.NamespaceResolver != null)
        {
            var result = qec.NamespaceResolver(id);
            if (result != null) return result;
        }
        // Fall back to the node provider's own namespace resolution. Any INodeStore can do
        // this — the XSLT engine's in-memory store as much as the database's document store.
        if (qec?.NodeProvider is INodeStore store)
        {
            var result = store.GetNamespaceUri(id);
            if (result != null) return result;
        }
        return "";
    }
}
