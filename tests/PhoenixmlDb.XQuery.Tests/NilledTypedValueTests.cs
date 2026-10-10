using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// A nilled element (validated, with xsi:nil="true") has no typed value: atomizing it gives the
/// empty sequence (XDM 3.1 §6.2.2). It was given the value of its type built from the empty
/// string, or an error where the empty string is not a value of the type.
/// </summary>
public class NilledTypedValueTests
{
    private const string Xsd = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
          <xs:element name="r">
            <xs:complexType>
              <xs:sequence>
                <xs:element name="s" type="xs:string" nillable="true" maxOccurs="unbounded"/>
                <xs:element name="n" type="xs:integer" nillable="true" maxOccurs="unbounded"/>
                <xs:element name="c" nillable="true" maxOccurs="unbounded">
                  <xs:complexType>
                    <xs:simpleContent>
                      <xs:extension base="xs:decimal"><xs:attribute name="unit" type="xs:string"/></xs:extension>
                    </xs:simpleContent>
                  </xs:complexType>
                </xs:element>
              </xs:sequence>
            </xs:complexType>
          </xs:element>
        </xs:schema>
        """;

    private const string Document = """
        <r xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"><s>a</s><s xsi:nil="true"/><n>7</n><n xsi:nil="true"/><c unit="m">1.5</c><c unit="m" xsi:nil="true"/></r>
        """;

    private static async Task<string> Eval(string query)
    {
        var schemas = new XsdSchemaProvider();
        schemas.AddFromString("", Xsd);
        var store = new XdmDocumentStore();
        var doc = schemas.ValidateAndAnnotate(Document, store, ValidationMode.Strict);
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store, schemaProvider: schemas);
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors));
        var items = new List<object?>();
        await foreach (var i in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext(initialContextItem: doc)))
            items.Add(i);
        return string.Join(",", items.Select(i => i?.ToString() ?? ""));
    }

    [Theory]
    // a nilled element of a simple type, of a numeric type, and of a complex type with simple content
    [InlineData("count(data(/r/s[2]))", "0")]
    [InlineData("count(data(/r/n[2]))", "0")]
    [InlineData("count(data(/r/c[2]))", "0")]
    [InlineData("empty(/r/n[2] ! data(.))", "True")]
    [InlineData("count(/r/n/data())", "1")]
    [InlineData("sum(/r/n)", "7")]
    [InlineData("/r/n[2] = 7", "False")]
    // its string value and its attributes are as before
    [InlineData("string(/r/s[2])", "")]
    [InlineData("string(/r/c[2]/@unit)", "m")]
    [InlineData("nilled(/r/n[2])", "True")]
    // an element that is not nilled keeps its typed value
    [InlineData("data(/r/n[1]) instance of xs:integer", "True")]
    [InlineData("data(/r/c[1]) + 1", "2.5")]
    [InlineData("nilled(/r/n[1])", "False")]
    public async Task A_nilled_element_atomizes_to_the_empty_sequence(string query, string expected)
        => (await Eval(query)).Should().Be(expected);
}
