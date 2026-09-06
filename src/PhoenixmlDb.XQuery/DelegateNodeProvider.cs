using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery;

public sealed class DelegateNodeProvider : INodeProvider
{
    private readonly Func<NodeId, XdmNode?> _loader;

    public DelegateNodeProvider(Func<NodeId, XdmNode?> loader)
    {
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
    }

    public XdmNode? GetNode(NodeId nodeId) => _loader(nodeId);
}
