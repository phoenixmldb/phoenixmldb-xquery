using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.Parser;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Schema;

/// <summary>
/// element(*, T), attribute(*, T) and schema-element(N) against validated nodes.
/// </summary>
public class TypedNodeTestsTests
{
    private const string Xsd = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" targetNamespace="urn:u" xmlns:u="urn:u" elementFormDefault="qualified">
          <xs:simpleType name="part"><xs:restriction base="xs:string"><xs:pattern value="\d{3}-[A-Z]{2}"/></xs:restriction></xs:simpleType>
          <xs:simpleType name="partOrInt"><xs:union memberTypes="u:part xs:integer"/></xs:simpleType>
          <xs:simpleType name="outer"><xs:union memberTypes="u:partOrInt xs:boolean"/></xs:simpleType>
          <xs:element name="un" type="u:partOrInt"/>
          <xs:element name="deep" type="u:outer"/>
          <xs:element name="holder"><xs:complexType><xs:attribute name="p" type="u:part"/></xs:complexType></xs:element>
        </xs:schema>
        """;

    private static async Task<string> EvalAsync(string query, string document)
    {
        var schemas = new XsdSchemaProvider();
        schemas.AddFromString("urn:u", Xsd);
        var store = new XdmDocumentStore();
        var doc = schemas.ValidateAndAnnotate(document, store, ValidationMode.Strict);
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store, schemaProvider: schemas);
        var compiled = engine.Compile("declare namespace u = 'urn:u'; " + query);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors));
        var items = new List<object?>();
        await foreach (var i in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext(initialContextItem: doc)))
            items.Add(i);
        return string.Join(",", items.Select(i => i?.ToString() ?? ""));
    }

    /// <summary>
    /// Since XPath 3.0 an element or attribute whose annotation is a member type of a union U
    /// matches element(*, U), and an element of a union type is annotated with the member its
    /// value matched. No element of a union type matched element(*, U), nor schema-element(N)
    /// of its declaration.
    /// </summary>
    [Theory]
    [InlineData("<u:un xmlns:u='urn:u'>100</u:un>")]
    [InlineData("<u:un xmlns:u='urn:u'>123-AB</u:un>")]
    public async Task An_element_of_a_union_type_matches_the_union(string document)
        => (await EvalAsync("u:un instance of element(*, u:partOrInt), u:un instance of element(u:un, u:partOrInt), u:un instance of schema-element(u:un)", document))
            .Should().Be("True,True,True");

    [Fact]
    public async Task A_member_of_a_union_in_a_union_matches_the_outer_union()
        => (await EvalAsync("u:deep instance of element(*, u:outer), u:deep instance of schema-element(u:deep)", "<u:deep xmlns:u='urn:u'>123-AB</u:deep>"))
            .Should().Be("True,True");

    [Fact]
    public async Task An_element_of_another_type_does_not_match_the_union()
        => (await EvalAsync("u:holder instance of element(*, u:partOrInt)", "<u:holder xmlns:u='urn:u' p='123-AB'/>"))
            .Should().Be("False");

    /// <summary>
    /// A validated attribute matches attribute(*, T) for a type T it is derived from, as an
    /// element does. Only the untyped hierarchy was known for attributes.
    /// </summary>
    [Fact]
    public async Task A_validated_attribute_matches_a_type_it_derives_from()
        => (await EvalAsync("u:holder/@p instance of attribute(*, u:part), u:holder/@p instance of attribute(*, xs:string), u:holder/@p instance of attribute(p, xs:anyAtomicType), u:holder/@p instance of attribute(*, xs:integer)",
                "<u:holder xmlns:u='urn:u' p='123-AB'/>"))
            .Should().Be("True,True,True,False");

    /// <summary>
    /// schema-element(N) with no prefix, in a host language's expression, is in the default
    /// namespace for elements that the host gives (xpath-default-namespace in XSLT).
    /// </summary>
    [Fact]
    public void Schema_element_takes_the_default_namespace_of_the_host()
    {
        var parser = new XQueryParserFacade { AllowNamespaceAxis = true };
        var type = ((PhoenixmlDb.XQuery.Ast.InstanceOfExpression)parser.Parse(". instance of schema-element(un)",
            prefix => prefix.Length == 0 ? "urn:u" : null)).TargetType;
        type.SchemaElementNamespace.Should().Be("urn:u");
    }
}
