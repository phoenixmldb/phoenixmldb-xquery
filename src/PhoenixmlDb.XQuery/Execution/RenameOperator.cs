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
        await foreach (var item in Target.ExecuteAsync(context))
        {
            targetNode = item;
            break;
        }

        if (targetNode == null)
            throw new XQueryRuntimeException("XUDY0027", "rename expression: target is empty.");

        object? nameValue = null;
        await foreach (var item in NewName.ExecuteAsync(context))
        {
            nameValue = item;
            break;
        }

        PhoenixmlDb.Core.QName qname;
        if (nameValue is PhoenixmlDb.Core.QName q)
            qname = q;
        else if (nameValue is string s)
            qname = new PhoenixmlDb.Core.QName(PhoenixmlDb.Core.NamespaceId.None, s);
        else
            throw new XQueryRuntimeException("XPTY0004",
                "rename expression: new name must be a QName or string.");

        context.PendingUpdates.AddRename(targetNode, qname);

        yield break;
    }
}
