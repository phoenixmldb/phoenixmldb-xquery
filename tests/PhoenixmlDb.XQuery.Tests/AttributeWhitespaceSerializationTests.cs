using FluentAssertions;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// Verifies that attribute-value whitespace (tab, newline, carriage return) is
/// serialized as numeric character references. Without this, XML attribute-value
/// normalization collapses those characters to spaces when the output is re-parsed.
/// </summary>
public class AttributeWhitespaceSerializationTests
{
    [Fact]
    public void FreeStandingAttribute_TabAndNewline_EmittedAsCharacterReferences()
    {
        var store = new XdmDocumentStore();
        var attr = new XdmAttribute
        {
            Id = new NodeId(1),
            Document = new DocumentId(1),
            Namespace = NamespaceId.None,
            LocalName = "x",
            // value contains a tab, a newline, and a carriage return
            Value = "a\tb\nc\rd"
        };

        // Adaptive method serializes a free-standing attribute as name="value"
        // through the EscapeXmlAttribute path.
        var result = XQueryResultSerializer.Serialize(attr, store, OutputMethod.Adaptive);

        result.Should().Contain("&#x9;");
        result.Should().Contain("&#xA;");
        result.Should().Contain("&#xD;");
        result.Should().NotContain("\t");
        result.Should().NotContain("\n");
        result.Should().NotContain("\r");
    }
}
