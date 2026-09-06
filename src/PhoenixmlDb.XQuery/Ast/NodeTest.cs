using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Base for node tests in step expressions.
/// </summary>
public abstract class NodeTest
{
    /// <summary>
    /// Tests if a node matches this test.
    /// </summary>
    public abstract bool Matches(XdmNodeKind kind, NamespaceId? ns, string? localName);
}
