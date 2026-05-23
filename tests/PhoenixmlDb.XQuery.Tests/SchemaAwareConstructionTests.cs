using System.Xml.Schema;
using FluentAssertions;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

public sealed class SchemaAwareConstructionTests
{
    private const string SimpleSchema = """
        <?xml version="1.0"?>
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"
                   targetNamespace="http://test.example/types"
                   xmlns="http://test.example/types"
                   elementFormDefault="qualified">
          <xs:element name="amount">
            <xs:complexType>
              <xs:attribute name="value" type="xs:decimal"/>
            </xs:complexType>
          </xs:element>
        </xs:schema>
        """;

    private const string SimpleSource = """
        <amount xmlns="http://test.example/types" value="42.5"/>
        """;

    [Fact]
    public void SchemaValidatedLoad_FillsAttributeTypeAnnotation()
    {
        var schemaSet = new XmlSchemaSet();
        using (var schemaReader = new System.IO.StringReader(SimpleSchema))
        using (var xmlReader = System.Xml.XmlReader.Create(schemaReader))
        {
            schemaSet.Add(null, xmlReader);
        }
        schemaSet.Compile();

        var store = new XdmDocumentStore();
        var doc = store.LoadFromStringWithSchema(SimpleSource, "test://source", schemaSet);

        var root = doc.Children
            .Select(id => store.GetNode(id))
            .OfType<XdmElement>()
            .Single();

        var attr = root.Attributes
            .Select(id => store.GetNode(id))
            .OfType<XdmAttribute>()
            .Single();

        attr.LocalName.Should().Be("value");
        attr.Value.Should().Be("42.5");

        // The whole point of this plan: schema validation must populate TypeAnnotation.
        // If this fails, the bug is in PhoenixmlDb.Core's parser, not the XQuery engine.
        // XdmTypeName.XsDecimal is the static property for xs:decimal.
        attr.TypeAnnotation.Should().Be(XdmTypeName.XsDecimal,
            "schema validation should annotate value='42.5' as xs:decimal per the XSD definition");
    }
}
