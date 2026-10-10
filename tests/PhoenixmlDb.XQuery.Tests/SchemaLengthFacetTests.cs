using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// The length facets of a string type of an imported schema count characters by code point:
/// a character outside the Basic Multilingual Plane is one, in a cast, in <c>castable</c> and
/// in validation. The message of a value that is too long names the facet.
/// </summary>
public class SchemaLengthFacetTests
{
    private const string Ns = "urn:test:lengths";

    private const string Xsd = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:t="urn:test:lengths"
                   targetNamespace="urn:test:lengths" elementFormDefault="qualified">
          <xs:simpleType name="one"><xs:restriction base="xs:string"><xs:length value="1"/></xs:restriction></xs:simpleType>
          <xs:simpleType name="upToTwo"><xs:restriction base="xs:string"><xs:maxLength value="2"/></xs:restriction></xs:simpleType>
          <xs:simpleType name="twoOrMore"><xs:restriction base="xs:string"><xs:minLength value="2"/></xs:restriction></xs:simpleType>
          <xs:element name="e" type="t:upToTwo"/>
        </xs:schema>
        """;

    private const string Face = "\U0001F600";

    private static async Task<string> Eval(string body)
    {
        var schemas = new XsdSchemaProvider();
        schemas.AddFromString(Ns, Xsd);
        var store = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store, schemaProvider: schemas);
        var compiled = engine.Compile($"import schema namespace t = \"{Ns}\";\n{body}");
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors));
        var items = new List<object?>();
        await foreach (var i in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
            items.Add(i);
        return string.Join(",", items.Select(i => i?.ToString() ?? ""));
    }

    [Theory]
    [InlineData("'" + Face + "' castable as t:one", "True")]
    [InlineData("'" + Face + Face + "' castable as t:one", "False")]
    [InlineData("'ab' castable as t:one", "False")]
    [InlineData("'" + Face + Face + "' castable as t:upToTwo", "True")]
    [InlineData("'" + Face + Face + Face + "' castable as t:upToTwo", "False")]
    [InlineData("'" + Face + "' castable as t:twoOrMore", "False")]
    [InlineData("'" + Face + Face + "' castable as t:twoOrMore", "True")]
    [InlineData("string-length(string('" + Face + "' cast as t:one))", "1")]
    [InlineData("string-length(string(validate { <t:e>" + Face + Face + "</t:e> }))", "2")]
    [InlineData("'abc' castable as t:upToTwo", "False")]
    public async Task A_length_is_a_number_of_characters(string query, string expected)
        => (await Eval(query)).Should().Be(expected);

    [Fact]
    public async Task The_message_names_the_facet()
    {
        var act = () => Eval("validate { <t:e>abc</t:e> }");
        (await act.Should().ThrowAsync<Exception>()).Which.Message.Should().Contain("MaxLength").And.NotContain("Pattern");
    }
}
