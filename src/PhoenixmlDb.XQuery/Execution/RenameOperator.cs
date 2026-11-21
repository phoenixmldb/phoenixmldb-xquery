using System.Numerics;
using System.Text;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.Xdm.Serialization;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Functions;
using PhoenixmlDb.XQuery.Optimizer;

namespace PhoenixmlDb.XQuery.Execution;

/// <summary>
/// Rename a node.
/// Collects a <see cref="Ast.RenamePrimitive"/> into the context's PUL.
/// </summary>
public sealed class RenameOperator : PhysicalOperator
{
    public required PhysicalOperator Target { get; init; }
    public required PhysicalOperator NewName { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        object? targetNode = null;
        await foreach (var item in Target.ExecuteGuarded(context))
        {
            targetNode = item;
            break;
        }

        if (targetNode == null)
            throw new XQueryRuntimeException("XUDY0027", "rename expression: target is empty.");

        object? nameValue = null;
        await foreach (var item in NewName.ExecuteGuarded(context))
        {
            nameValue = item;
            break;
        }

        // The new name is atomized; xs:untypedAtomic is treated as a string (XQUF 3.0 §3.3).
        nameValue = context.AtomizeWithNodes(nameValue) switch
        {
            Xdm.XsUntypedAtomic ua => ua.Value,
            var atomized => atomized,
        };

        PhoenixmlDb.Core.QName qname;
        if (nameValue is PhoenixmlDb.Core.QName q)
            qname = q;
        else if (nameValue is string s && s.Contains(':', StringComparison.Ordinal))
        {
            // XQUF 3.0: a string new name is cast to xs:QName with the statically known
            // namespaces; an unbound prefix is XQDY0074. It was kept as one local name with a
            // colon, which the serializer rejected later (BUGS.md #129).
            try
            {
                qname = (PhoenixmlDb.Core.QName)(await new Functions.QNameConstructorFunction()
                    .InvokeAsync([s], context).ConfigureAwait(false))!;
            }
            catch (XQueryRuntimeException e) when (e.ErrorCode == "FONS0004")
            {
                throw new XQueryRuntimeException("XQDY0074",
                    $"rename expression: the prefix of '{s}' is not bound to a namespace.");
            }
        }
        else if (nameValue is string s2)
            qname = new PhoenixmlDb.Core.QName(PhoenixmlDb.Core.NamespaceId.None, s2);
        else
            throw new XQueryRuntimeException("XPTY0004",
                "rename expression: new name must be a QName or string.");

        // The new name's namespace id must be one the query's node store has interned, as for an
        // element constructor: fn:QName builds an id from the URI's hash that no store knows,
        // and the URI itself (RuntimeNamespace) was dropped here, so the renamed node lost its
        // namespace and could not be serialized (BUGS.md #129).
        var uri = qname.ResolvedNamespace;
        if (uri != null)
        {
            var nsId = uri.Length == 0
                ? PhoenixmlDb.Core.NamespaceId.None
                : context.NodeStore is INodeBuilder builder ? builder.InternNamespace(uri) : qname.Namespace;
            qname = new PhoenixmlDb.Core.QName(nsId, qname.LocalName, qname.Prefix) { RuntimeNamespace = uri };
        }

        context.PendingUpdates.AddRename(targetNode, qname);

        yield break;
    }
}
