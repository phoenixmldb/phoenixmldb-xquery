using AwesomeAssertions;
using PhoenixmlDb.Xdm.Nodes;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Schema;

/// <summary>
/// An element validated against a type is annotated with that type even when the schema also
/// declares an element of its name with another type: validation by type does not use the
/// element's declaration. The annotating parse assessed it by the declaration, which refused
/// the xsi:type it was given, so the element stayed untyped.
/// </summary>
public sealed class ValidationByTypeOfDeclaredElementTests
{
    private const string Xsd = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
          <xs:simpleType name="small"><xs:restriction base="xs:decimal"><xs:maxExclusive value="10"/></xs:restriction></xs:simpleType>
          <xs:element name="e" type="small"/>
        </xs:schema>
        """;

    [Theory]
    [InlineData("<e>DECOY</e>", "string", "string")]
    [InlineData("<e>25</e>", "decimal", "decimal")]
    [InlineData("<e/>", "string", "string")]
    public void The_element_has_the_type_it_was_validated_against(string xml, string type, string annotation)
    {
        var schemas = new XsdSchemaProvider();
        schemas.AddFromString("", Xsd);
        var store = new XdmDocumentStore();
        var validated = schemas.ValidateElementAndAnnotate(xml, store, ValidationMode.Strict,
            "http://www.w3.org/2001/XMLSchema", type);
        var element = ((XdmDocument)validated!).Children.Select(store.GetNode).OfType<XdmElement>().Single();
        element.LocalName.Should().Be("e");
        element.TypeAnnotation.LocalName.Should().Be(annotation);
    }
}
