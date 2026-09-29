using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Schema;

/// <summary>
/// ISchemaProvider.Validate(node) validated an element's start tag wrapped around its STRING
/// VALUE: children and attributes never reached the validator, so a schema requiring a child
/// rejected a valid instance and structure went unchecked (#40). The overload taking the node
/// provider serializes the node with its markup; the one without refuses a node it cannot see.
/// </summary>
public sealed class ValidateNodeMarkupTests
{
    private const string Xsd = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
          <xs:element name="e">
            <xs:complexType>
              <xs:sequence><xs:element name="c" type="xs:string"/></xs:sequence>
              <xs:attribute name="id" type="xs:integer" use="required"/>
            </xs:complexType>
          </xs:element>
        </xs:schema>
        """;

    private static (XsdSchemaProvider Schemas, XdmDocumentStore Store) Setup()
    {
        var schemas = new XsdSchemaProvider();
        schemas.AddFromString("", Xsd);
        return (schemas, new XdmDocumentStore());
    }

    [Fact]
    public void A_valid_instance_with_a_required_child_validates()
    {
        var (schemas, store) = Setup();
        var doc = store.LoadFromString("""<e id="1"><c>x</c></e>""");
        var act = () => schemas.Validate(doc, store, ValidationMode.Strict);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("""<e id="1"/>""")]                 // required child missing
    [InlineData("""<e id="x"><c>x</c></e>""")]      // attribute of the wrong type
    [InlineData("""<e><c>x</c></e>""")]             // required attribute missing
    public void Invalid_structure_is_rejected(string xml)
    {
        var (schemas, store) = Setup();
        var doc = store.LoadFromString(xml);
        var act = () => schemas.Validate(doc, store, ValidationMode.Strict);
        act.Should().Throw<SchemaValidationException>();
    }

    [Fact]
    public void The_interface_default_serializes_the_node_too()
    {
        var (schemas, store) = Setup();
        ISchemaProvider provider = schemas;
        var doc = store.LoadFromString("""<e id="1"/>""");
        var act = () => provider.Validate(doc, store, ValidationMode.Strict);
        act.Should().Throw<SchemaValidationException>("the child is missing, and only the markup shows it");
    }

    [Fact]
    public void Without_a_provider_a_node_with_structure_is_refused_not_misvalidated()
    {
        var (schemas, store) = Setup();
        var doc = store.LoadFromString("""<e id="1"><c>x</c></e>""");
        var act = () => schemas.Validate(doc, ValidationMode.Strict);
        act.Should().Throw<InvalidOperationException>().WithMessage("*node provider*");
    }
}
