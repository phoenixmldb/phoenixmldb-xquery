using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// schema-element(), schema-attribute() and namespace-node() are node types: a value of such a
/// type is passed as the node it is. The operators each held a list of the node kinds that left
/// these out, so a parameter declared <c>as schema-element(dict)</c> was taken for an atomic
/// type and its argument was atomized, which is FOTY0012 for an element with element-only
/// content (QT3 itunes).
/// </summary>
public class SchemaNodeTypeArgumentTests
{
    private const string Xsd = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
          <xs:element name="dict">
            <xs:complexType>
              <xs:sequence minOccurs="0" maxOccurs="unbounded">
                <xs:element ref="key"/>
                <xs:choice><xs:element ref="dict"/><xs:element ref="integer"/></xs:choice>
              </xs:sequence>
              <xs:attribute ref="id"/>
            </xs:complexType>
          </xs:element>
          <xs:element name="key" type="xs:string"/>
          <xs:element name="integer" type="xs:integer"/>
          <xs:attribute name="id" type="xs:integer"/>
        </xs:schema>
        """;

    private const string Document =
        """<dict id="7"><key>Year</key><integer>2005</integer><key>Inner</key><dict><key>Year</key><integer>1999</integer></dict></dict>""";

    private static async Task<string> Eval(string body)
    {
        var schemas = new XsdSchemaProvider();
        schemas.AddFromString("", Xsd);
        var store = new XdmDocumentStore();
        var doc = schemas.ValidateAndAnnotate(Document, store, ValidationMode.Strict);
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store, schemaProvider: schemas);

        var compiled = engine.Compile("import schema default element namespace \"\";\n" + body);
        if (!compiled.Success)
            throw new InvalidOperationException(string.Join("; ", compiled.Errors));

        var ctx = engine.CreateContext(initialContextItem: doc);
        var items = new List<object?>();
        await foreach (var i in compiled.ExecutionPlan!.ExecuteAsync(ctx))
            items.Add(i);
        return string.Join(",", items.Select(i => i?.ToString() ?? ""));
    }

    [Theory]
    // A parameter: the element arrives as a node, and the body can navigate from it.
    [InlineData("declare function local:year($d as schema-element(dict)) as xs:integer { $d/key[. = 'Year']/following-sibling::schema-element(integer)[1] }; local:year(/dict)", "2005")]
    [InlineData("declare function local:year($d as schema-element(dict)) as xs:integer { $d/key[. = 'Year']/following-sibling::schema-element(integer)[1] }; string-join(//dict/local:year(.) ! string(), ' ')", "2005 1999")]
    // A sequence of them, and an optional one.
    [InlineData("declare function local:n($d as schema-element(dict)*) as xs:integer { count($d/key) }; local:n(//dict)", "3")]
    [InlineData("declare function local:n($d as schema-element(dict)?) as xs:integer { count($d/key) }; local:n(())", "0")]
    // An inline function.
    [InlineData("let $f := function($d as schema-element(dict)) as xs:integer { count($d/dict) } return $f(/dict)", "1")]
    // A return type: the nodes come back as nodes.
    [InlineData("declare function local:inner($d as element()) as schema-element(dict)* { $d/dict }; count(local:inner(/dict)/key)", "1")]
    // schema-attribute().
    [InlineData("declare function local:a($a as schema-attribute(id)) as xs:string { name($a) }; local:a(/dict/@id)", "id")]
    // A for binding with a declared type.
    [InlineData("for $d as schema-element(dict) in //dict return count($d/key)", "2,1")]
    // A let binding and a typeswitch, which were right before.
    [InlineData("let $d as schema-element(dict) := /dict return count($d/key)", "2")]
    public async Task A_node_of_a_schema_node_type_is_passed_as_a_node(string query, string expected)
        => (await Eval(query)).Should().Be(expected);

    /// <summary>
    /// An element that was not validated is not an instance of schema-element(N), whatever its
    /// name: the declared return type and the declared parameter type refuse it.
    /// </summary>
    [Theory]
    [InlineData("declare function local:f() as schema-element(dict) { <dict/> }; local:f()")]
    [InlineData("declare function local:f($d as schema-element(dict)) as xs:integer { 1 }; local:f(<dict/>)")]
    [InlineData("declare function local:f($d as element()) as schema-element(key) { $d }; local:f(/dict)")]
    public async Task A_node_that_is_not_an_instance_of_the_schema_node_type_is_XPTY0004(string query)
    {
        var act = () => Eval(query);
        (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be("XPTY0004");
    }

    [Fact]
    public async Task An_atomic_parameter_still_atomizes_a_typed_element()
        => (await Eval("declare function local:f($i as xs:integer) as xs:integer { $i + 1 }; local:f(/dict/integer)")).Should().Be("2006");

    [Fact]
    public async Task Atomizing_an_element_with_element_only_content_is_still_FOTY0012()
    {
        var act = () => Eval("declare function local:f($s as xs:string) as xs:string { $s }; local:f(/dict)");
        (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be("FOTY0012");
    }
}
