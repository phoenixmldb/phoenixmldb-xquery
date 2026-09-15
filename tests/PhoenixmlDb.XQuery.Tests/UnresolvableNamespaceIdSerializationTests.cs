using System.Xml.Linq;
using FluentAssertions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// A namespace id that the node store cannot resolve used to be serialized as "" or
/// "urn:unresolved:p", silently putting the node in another namespace or printing xmlns:p="".
/// It now fails loudly. The well-known ids are permanent and must still resolve in a store that
/// never registered them.
/// </summary>
public class UnresolvableNamespaceIdSerializationTests
{
    private static readonly NamespaceId Unregistered = new(9999);

    private static XdmElement Element(XdmDocumentStore store, string? prefix, string localName, NamespaceId ns,
        IReadOnlyList<NamespaceBinding>? declarations = null)
    {
        INodeBuilder builder = store;
        var element = new XdmElement
        {
            Id = builder.AllocateId(),
            Document = new DocumentId(0),
            Namespace = ns,
            LocalName = localName,
            Prefix = prefix,
            Attributes = new List<NodeId>(),
            Children = new List<NodeId>(),
            NamespaceDeclarations = declarations ?? new List<NamespaceBinding>(),
        };
        builder.RegisterNode(element);
        return element;
    }

    private static void AddAttribute(XdmDocumentStore store, XdmElement owner, string? prefix, string localName, NamespaceId ns, string value)
    {
        INodeBuilder builder = store;
        var attribute = new XdmAttribute
        {
            Id = builder.AllocateId(),
            Document = new DocumentId(0),
            Namespace = ns,
            LocalName = localName,
            Prefix = prefix,
            Value = value,
        };
        attribute.Parent = owner.Id;
        builder.RegisterNode(attribute);
        ((List<NodeId>)owner.Attributes).Add(attribute.Id);
    }

    private static string Xml(XdmDocumentStore store, XdmNode node)
        => new XQueryResultSerializer(store, new SerializationOptions { Method = OutputMethod.Xml, OmitXmlDeclaration = true })
            .Serialize(node);

    [Fact]
    public void Declaration_with_unregistered_id_fails_loudly()
    {
        var store = new XdmDocumentStore();
        var element = Element(store, null, "e", NamespaceId.None, new List<NamespaceBinding> { new("p", Unregistered) });

        FluentActions.Invoking(() => Xml(store, element))
            .Should().Throw<InvalidOperationException>().WithMessage("*'xmlns:p'*9999*");
    }

    [Fact]
    public void Element_in_unregistered_namespace_fails_loudly()
    {
        var store = new XdmDocumentStore();
        var element = Element(store, "p", "e", Unregistered);

        FluentActions.Invoking(() => Xml(store, element))
            .Should().Throw<InvalidOperationException>().WithMessage("*'p:e'*9999*");
    }

    [Fact]
    public void Attribute_in_unregistered_namespace_fails_loudly()
    {
        var store = new XdmDocumentStore();
        var element = Element(store, null, "e", NamespaceId.None);
        AddAttribute(store, element, "p", "a", Unregistered, "v");

        FluentActions.Invoking(() => Xml(store, element))
            .Should().Throw<InvalidOperationException>().WithMessage("*'p:a'*9999*");
    }

    [Fact]
    public void Fn_serialize_node_writer_fails_loudly_on_unregistered_declaration()
    {
        var store = new XdmDocumentStore();
        var element = Element(store, null, "e", NamespaceId.None, new List<NamespaceBinding> { new("p", Unregistered) });

        FluentActions.Invoking(() => PhoenixmlDb.XQuery.Functions.SerializeFunction.SerializeNodeToXml(element, store))
            .Should().Throw<InvalidOperationException>().WithMessage("*'xmlns:p'*9999*");
    }

    // XdmDocumentStore allocates from 100 and never registers the well-known ids, so these used to
    // come out as urn:unresolved:xs and an xml:lang in no namespace.
    [Fact]
    public void Well_known_ids_resolve_without_being_registered()
    {
        var store = new XdmDocumentStore();
        var element = Element(store, "xs", "e", NamespaceId.Xsd, new List<NamespaceBinding> { new("xs", NamespaceId.Xsd) });
        AddAttribute(store, element, "xml", "lang", NamespaceId.Xml, "en");

        var parsed = XElement.Parse(Xml(store, element));

        parsed.Name.NamespaceName.Should().Be("http://www.w3.org/2001/XMLSchema");
        parsed.Attribute(XNamespace.Xml + "lang")!.Value.Should().Be("en");
    }
}
