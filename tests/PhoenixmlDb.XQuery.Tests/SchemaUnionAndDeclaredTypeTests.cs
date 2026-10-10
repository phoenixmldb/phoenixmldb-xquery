using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// Three places where a schema type was not carried through: an item of a list of unions had
/// no typed value of its own, a union whose member is a built-in list type did not cast to it,
/// and a variable declared <c>as schema-element(N)</c> refused a validated element of that
/// declaration (QT3 validateexpr-24, CastAs-UnionType-27, extvardef-025).
/// </summary>
public class SchemaUnionAndDeclaredTypeTests
{
    private const string Ns = "urn:test:unions";

    private const string Xsd = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:t="urn:test:unions"
                   targetNamespace="urn:test:unions" elementFormDefault="qualified">
          <xs:simpleType name="number"><xs:union memberTypes="xs:integer xs:float"/></xs:simpleType>
          <xs:simpleType name="numbers"><xs:list itemType="t:number"/></xs:simpleType>
          <xs:simpleType name="negative">
            <xs:restriction base="t:number"><xs:pattern value="-.*"/></xs:restriction>
          </xs:simpleType>
          <xs:simpleType name="negatives"><xs:list itemType="t:negative"/></xs:simpleType>
          <xs:simpleType name="dateOrWord"><xs:union memberTypes="xs:date xs:NCName"/></xs:simpleType>
          <xs:simpleType name="refsOrNumber"><xs:union memberTypes="xs:IDREFS xs:integer"/></xs:simpleType>
          <xs:simpleType name="numberOrTokens"><xs:union memberTypes="xs:integer xs:NMTOKENS"/></xs:simpleType>
          <xs:element name="e" type="t:numbers"/>
          <xs:element name="n" type="t:negatives"/>
          <xs:element name="w"><xs:simpleType><xs:list itemType="t:dateOrWord"/></xs:simpleType></xs:element>
          <xs:element name="box">
            <xs:complexType><xs:sequence><xs:element name="item" type="xs:string" maxOccurs="unbounded"/></xs:sequence></xs:complexType>
          </xs:element>
        </xs:schema>
        """;

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
    // each item of a list of unions is a value of the member type its form is valid for
    [InlineData("let $d := data(validate { <t:e>1 1.0e0</t:e> }) return ($d[1] instance of xs:integer, $d[2] instance of xs:float)", "True,True")]
    [InlineData("count(data(validate { <t:e>1 2 3.5</t:e> }))", "3")]
    [InlineData("sum(data(validate { <t:e>1 2 3.5</t:e> }))", "6.5")]
    [InlineData("data(validate { <t:n>-1 -2.5e0</t:n> })[1] instance of xs:integer", "True")]
    [InlineData("let $d := data(validate { <t:w>2020-01-02 word</t:w> }) return ($d[1] instance of xs:date, $d[2] instance of xs:NCName)", "True,True")]
    // a union with a built-in list type as a member
    [InlineData("string-join('a b c' cast as t:refsOrNumber, '|')", "a|b|c")]
    [InlineData("('a b c' cast as t:refsOrNumber) instance of xs:IDREF*", "True")]
    [InlineData("count('a b c' cast as t:refsOrNumber)", "3")]
    [InlineData("('7' cast as t:numberOrTokens) instance of xs:integer", "True")]
    [InlineData("('x y' cast as t:numberOrTokens) instance of xs:NMTOKEN*", "True")]
    [InlineData("'1 2' castable as t:refsOrNumber", "False")]
    public async Task The_schema_type_is_carried_through(string query, string expected)
        => (await Eval(query)).Should().Be(expected);

    [Theory]
    [InlineData("declare variable $x as schema-element(t:box) := validate { <t:box><t:item>a</t:item></t:box> }; count($x/t:item)")]
    [InlineData("declare variable $x as schema-element(t:box) external := validate { <t:box><t:item>a</t:item></t:box> }; count($x/t:item)")]
    [InlineData("declare variable $x as schema-element(t:box)+ := (validate { <t:box><t:item>a</t:item></t:box> }); count($x/t:item)")]
    public async Task A_variable_declared_as_a_schema_element_takes_a_validated_element(string query)
        => (await Eval(query)).Should().Be("1");

    [Fact]
    public async Task And_refuses_one_that_was_not_validated()
    {
        var act = () => Eval("declare variable $x as schema-element(t:box) := <t:box><t:item>a</t:item></t:box>; count($x)");
        (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be("XPTY0004");
    }
}
