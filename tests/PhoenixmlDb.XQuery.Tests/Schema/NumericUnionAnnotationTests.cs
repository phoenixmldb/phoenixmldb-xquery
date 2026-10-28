using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Schema;

/// <summary>
/// xs:numeric is the union of xs:double, xs:float and xs:decimal, so element(*, xs:numeric) and
/// attribute(*, xs:numeric) match nodes of those types and of types derived from them. It
/// matched none (QT3 xs-numeric-021, -022).
/// </summary>
public class NumericUnionAnnotationTests
{
    private const string Xsd = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
          <xs:element name="r"><xs:complexType><xs:sequence>
            <xs:element name="f" type="xs:float"/>
            <xs:element name="i" type="xs:integer"/>
            <xs:element name="s" type="xs:string"/>
            <xs:element name="d"><xs:complexType><xs:attribute name="a" type="xs:decimal"/></xs:complexType></xs:element>
          </xs:sequence></xs:complexType></xs:element>
        </xs:schema>
        """;

    private static async Task<string> Eval(string query)
    {
        var schemas = new XsdSchemaProvider();
        schemas.AddFromString("", Xsd);
        var store = new XdmDocumentStore();
        var doc = schemas.ValidateAndAnnotate("<r><f>1.5</f><i>2</i><s>x</s><d a='3.5'/></r>", store, ValidationMode.Strict);
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store, schemaProvider: schemas);
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors));
        var items = new List<object?>();
        await foreach (var i in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext(initialContextItem: doc)))
            items.Add(i);
        return string.Join(",", items.Select(i => i?.ToString() ?? ""));
    }

    [Theory]
    [InlineData("string(/r/f instance of element(*, xs:numeric))", "true")]
    [InlineData("string(/r/i instance of element(*, xs:numeric))", "true")]
    [InlineData("string(/r/s instance of element(*, xs:numeric))", "false")]
    [InlineData("string(/r/d/@a instance of attribute(*, xs:numeric))", "true")]
    public async Task Numeric_annotations_match_xs_numeric(string query, string expected)
        => (await Eval(query)).Should().Be(expected);
}
