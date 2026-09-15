using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using Xunit;

namespace PhoenixmlDb.XQuery.Cli.Tests;

/// <summary>
/// The CLI's ResultSerializer wrote an unresolvable namespace id as "", printing xmlns:p="" or putting
/// the element in no namespace. It fails loudly instead, like the engine's serializer.
/// </summary>
public sealed class CliUnresolvableNamespaceIdTests
{
    [Fact]
    public void Declaration_with_unregistered_id_fails_loudly()
    {
        var store = new XdmDocumentStore();
        INodeBuilder builder = store;
        var element = new XdmElement
        {
            Id = builder.AllocateId(),
            Document = new DocumentId(0),
            Namespace = NamespaceId.None,
            LocalName = "e",
            Attributes = new List<NodeId>(),
            Children = new List<NodeId>(),
            NamespaceDeclarations = new List<NamespaceBinding> { new("p", new NamespaceId(9999)) },
        };
        builder.RegisterNode(element);

        using var writer = new StringWriter();
        FluentActions.Invoking(() => new ResultSerializer(store, writer, OutputMethod.Xml).Serialize(element))
            .Should().Throw<InvalidOperationException>().WithMessage("*'xmlns:p'*9999*");
    }
}
