using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// Validation of an element against a named type: <c>validate type T { … }</c>, and
/// <see cref="ISchemaProvider.ValidateXml"/> and <see cref="ISchemaProvider.ValidateAndAnnotate"/>
/// with a type. The type was not used: the element was validated by its declaration, and an
/// element with no declaration passed with any content and stayed untyped.
/// </summary>
public class ValidateAgainstTypeTests
{
    private const string Xsd = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" targetNamespace="urn:o" xmlns:o="urn:o" elementFormDefault="qualified">
          <xs:element name="order" type="o:orderType"/>
          <xs:complexType name="orderType">
            <xs:sequence><xs:element name="qty" type="xs:integer"/></xs:sequence>
            <xs:attribute name="id" type="xs:ID"/>
          </xs:complexType>
          <xs:simpleType name="small"><xs:restriction base="xs:integer"><xs:maxInclusive value="9"/></xs:restriction></xs:simpleType>
        </xs:schema>
        """;

    private static XsdSchemaProvider Schemas()
    {
        var schemas = new XsdSchemaProvider();
        schemas.AddFromString("urn:o", Xsd);
        return schemas;
    }

    [Theory]
    // an element with no declaration, with the content of the type
    [InlineData("<any xmlns:o='urn:o'><o:qty>1</o:qty></any>", "urn:o", "orderType")]
    [InlineData("<any xmlns:o='urn:o' id='a'><o:qty>1</o:qty></any>", "urn:o", "orderType")]
    // a simple type, of the schema and built in
    [InlineData("<n>7</n>", "urn:o", "small")]
    [InlineData("<n> 12 </n>", "http://www.w3.org/2001/XMLSchema", "integer")]
    // the declared element, against its own type and against another
    [InlineData("<o:order xmlns:o='urn:o'><o:qty>1</o:qty></o:order>", "urn:o", "orderType")]
    [InlineData("<o:order xmlns:o='urn:o'>5</o:order>", "urn:o", "small")]
    public void Content_that_is_valid_for_the_type_passes(string xml, string ns, string type)
        => FluentActions.Invoking(() => Schemas().ValidateXml(xml, ValidationMode.Strict, ns, type)).Should().NotThrow();

    [Theory]
    [InlineData("<any xmlns:o='urn:o'><o:qty>x</o:qty></any>", "urn:o", "orderType")]
    [InlineData("<any xmlns:o='urn:o'/>", "urn:o", "orderType")]
    [InlineData("<any xmlns:o='urn:o' other='1'><o:qty>1</o:qty></any>", "urn:o", "orderType")]
    [InlineData("<any xmlns:o='urn:o'><o:qty>1</o:qty><o:qty>2</o:qty></any>", "urn:o", "orderType")]
    [InlineData("<n>70</n>", "urn:o", "small")]
    [InlineData("<n>x</n>", "http://www.w3.org/2001/XMLSchema", "integer")]
    // valid by its declaration, not by the type asked for
    [InlineData("<o:order xmlns:o='urn:o'><o:qty>1</o:qty></o:order>", "urn:o", "small")]
    // no such type
    [InlineData("<n>7</n>", "urn:o", "nope")]
    public void Content_that_is_not_valid_for_the_type_is_refused(string xml, string ns, string type)
        => FluentActions.Invoking(() => Schemas().ValidateXml(xml, ValidationMode.Strict, ns, type))
            .Should().Throw<SchemaValidationException>();

    [Fact]
    public void Each_element_of_a_fragment_is_validated_against_the_type()
    {
        const string Good = "<a xmlns:o='urn:o'><o:qty>1</o:qty></a>";
        const string Bad = "<b xmlns:o='urn:o'><o:qty>x</o:qty></b>";
        FluentActions.Invoking(() => Schemas().ValidateXmlFragment(Good + Good, ValidationMode.Strict, "urn:o", "orderType")).Should().NotThrow();
        FluentActions.Invoking(() => Schemas().ValidateXmlFragment(Good + Bad, ValidationMode.Strict, "urn:o", "orderType"))
            .Should().Throw<SchemaValidationException>();
        FluentActions.Invoking(() => Schemas().ValidateXmlFragment(Bad + Good, ValidationMode.Strict, "urn:o", "orderType"))
            .Should().Throw<SchemaValidationException>();
    }

    [Fact]
    public void The_validated_tree_has_the_annotations_of_the_type_and_nothing_added()
    {
        var store = new XdmDocumentStore();
        var document = (PhoenixmlDb.Xdm.Nodes.XdmDocument)Schemas().ValidateAndAnnotate(
            "<any xmlns:o='urn:o' id='a'><o:qty>1</o:qty></any>", store, ValidationMode.Strict, "urn:o", "orderType")!;
        var root = (PhoenixmlDb.Xdm.Nodes.XdmElement)store.GetNode(document.Children[0])!;

        root.LocalName.Should().Be("any");
        root.TypeAnnotation.LocalName.Should().Be("orderType");
        root.Attributes.Should().HaveCount(1, "the attribute that told the parse the type is not in the tree");
        ((PhoenixmlDb.Xdm.Nodes.XdmAttribute)store.GetNode(root.Attributes[0])!).LocalName.Should().Be("id");
        root.NamespaceDeclarations.Select(b => b.Prefix).Should().Equal("o");
        var qty = (PhoenixmlDb.Xdm.Nodes.XdmElement)store.GetNode(root.Children[0])!;
        qty.TypeAnnotation.LocalName.Should().Be("integer");
        root.StringValue.Should().Be("1");
    }

    private static async Task<string> Eval(string query)
    {
        var schemas = Schemas();
        var store = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store, schemaProvider: schemas);
        var compiled = engine.Compile("declare namespace o = 'urn:o'; " + query);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors));
        var items = new List<object?>();
        await foreach (var i in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
            items.Add(i);
        return string.Join(",", items.Select(i => i?.ToString() ?? ""));
    }

    [Theory]
    [InlineData("validate type o:orderType { <any><o:qty>1</o:qty></any> } instance of element(*, o:orderType)", "True")]
    [InlineData("(validate type o:orderType { <any><o:qty>1</o:qty></any> })/o:qty instance of element(*, xs:integer)", "True")]
    [InlineData("data((validate type o:orderType { <any><o:qty>1</o:qty></any> })/o:qty) + 1", "2")]
    [InlineData("count((validate type o:orderType { <any><o:qty>1</o:qty></any> })/@*)", "0")]
    [InlineData("data(validate type o:small { <n>7</n> }) instance of xs:integer", "True")]
    public async Task Validate_type_gives_an_element_of_that_type(string query, string expected)
        => (await Eval(query)).Should().Be(expected);

    [Theory]
    [InlineData("validate type o:orderType { <any><o:qty>x</o:qty></any> }")]
    [InlineData("validate type o:orderType { <any/> }")]
    [InlineData("validate type o:small { <n>70</n> }")]
    public async Task Validate_type_refuses_content_that_is_not_valid_for_the_type(string query)
    {
        var run = () => Eval(query);
        (await run.Should().ThrowAsync<Exception>()).Which.Message.Should().Contain("alid");
    }

    private const string IdXsd = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
          <xs:element name="list"><xs:complexType><xs:sequence>
            <xs:element name="item" maxOccurs="unbounded"><xs:complexType>
              <xs:attribute name="id" type="xs:ID"/><xs:attribute name="ref" type="xs:IDREF"/></xs:complexType></xs:element>
          </xs:sequence></xs:complexType></xs:element>
        </xs:schema>
        """;

    /// <summary>
    /// The uniqueness of IDs, and that an IDREF has a target, are constraints on a document.
    /// They are not applied when an element is validated (XQuery 3.1 §3.21.2, XSLT 3.0
    /// §25.4.1); they were, so an element with the same ID twice was refused.
    /// </summary>
    [Theory]
    [InlineData("<list><item id='a'/><item id='a'/></list>")]
    [InlineData("<list><item ref='nowhere'/></list>")]
    public void An_element_is_validated_without_the_ID_constraints_of_a_document(string xml)
    {
        var schemas = new XsdSchemaProvider();
        schemas.AddFromString("", IdXsd);
        var store = new XdmDocumentStore();

        FluentActions.Invoking(() => schemas.ValidateElementAndAnnotate(xml, store, ValidationMode.Strict)).Should().NotThrow();
        FluentActions.Invoking(() => schemas.ValidateXmlFragment(xml, ValidationMode.Strict)).Should().NotThrow();
        // a document is still held to them
        FluentActions.Invoking(() => schemas.ValidateAndAnnotate(xml, store, ValidationMode.Strict)).Should().Throw<SchemaValidationException>();
        FluentActions.Invoking(() => schemas.ValidateXml(xml, ValidationMode.Strict)).Should().Throw<SchemaValidationException>();
    }

    [Fact]
    public void An_element_with_other_errors_is_still_refused()
    {
        var schemas = new XsdSchemaProvider();
        schemas.AddFromString("", IdXsd);
        FluentActions.Invoking(() => schemas.ValidateElementAndAnnotate("<list><item id='a'/><item id='a' other='1'/></list>", new XdmDocumentStore(), ValidationMode.Strict))
            .Should().Throw<SchemaValidationException>();
    }
}
