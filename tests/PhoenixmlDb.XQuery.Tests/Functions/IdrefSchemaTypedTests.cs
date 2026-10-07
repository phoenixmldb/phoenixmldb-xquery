using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Functions;

/// <summary>
/// fn:idref returns the nodes with the is-idrefs property whose value names one of the given
/// IDs (F&amp;O 3.1 §14.5.5). A schema gives a node that property as well as a DTD does: only
/// DTD-declared attributes were recognised, so in a schema-validated document it found nothing.
/// </summary>
public sealed class IdrefSchemaTypedTests
{
    private const string Schema = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" targetNamespace="urn:r" xmlns="urn:r"
                   elementFormDefault="qualified">
          <xs:simpleType name="shortRef"><xs:restriction base="xs:IDREF"><xs:maxLength value="8"/></xs:restriction></xs:simpleType>
          <xs:simpleType name="word"><xs:restriction base="xs:NCName"><xs:enumeration value="none"/></xs:restriction></xs:simpleType>
          <xs:simpleType name="wordOrRef"><xs:union memberTypes="word xs:IDREF"/></xs:simpleType>
          <xs:simpleType name="wordsOrRefs"><xs:list itemType="wordOrRef"/></xs:simpleType>
          <xs:element name="doc">
            <xs:complexType>
              <xs:sequence>
                <xs:element name="target" maxOccurs="unbounded">
                  <xs:complexType><xs:attribute name="id" type="xs:ID"/></xs:complexType>
                </xs:element>
                <xs:element name="ref" type="xs:IDREF" minOccurs="0"/>
                <xs:element name="refs" type="xs:IDREFS" minOccurs="0"/>
                <xs:element name="short" type="shortRef" minOccurs="0"/>
                <xs:element name="mixed" type="wordsOrRefs" minOccurs="0"/>
                <xs:element name="words" type="wordsOrRefs" minOccurs="0"/>
                <xs:element name="text" type="xs:string" minOccurs="0"/>
                <xs:element name="holder" minOccurs="0">
                  <xs:complexType><xs:attribute name="to" type="xs:IDREF"/></xs:complexType>
                </xs:element>
              </xs:sequence>
            </xs:complexType>
          </xs:element>
        </xs:schema>
        """;

    private const string Document = """
        document { <r:doc>
          <r:target id="a"/><r:target id="b"/><r:target id="none"/>
          <r:ref>a</r:ref>
          <r:refs>a b</r:refs>
          <r:short>b</r:short>
          <r:mixed>none b</r:mixed>
          <r:words>none none</r:words>
          <r:text>a</r:text>
          <r:holder to="a"/>
        </r:doc> }
        """;

    private static async Task<(List<object?> Items, string? Error)> RunAsync(string body)
    {
        var provider = new XsdSchemaProvider();
        provider.AddFromString("urn:r", Schema);
        var store = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store, schemaProvider: provider);
        var compiled = engine.Compile(
            "import schema namespace r = 'urn:r'; let $d := validate strict { " + Document + " } return " + body);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors));
        var items = new List<object?>();
        try
        {
            await foreach (var item in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
                items.Add(item);
            return (items, null);
        }
        catch (XQueryRuntimeException ex)
        {
            return (items, ex.ErrorCode);
        }
    }

    [Theory]
    // An element typed xs:IDREF, one typed xs:IDREFS, and an attribute typed xs:IDREF. Not the
    // xs:string element holding the same text.
    [InlineData("'a'", "ref refs to")]
    // A type derived from xs:IDREF, and a list of a union whose second value validated as an
    // IDREF. In such a node every token is a candidate, the one validated as a word too.
    [InlineData("'b'", "refs short mixed")]
    [InlineData("'none'", "mixed")]
    [InlineData("'missing'", "")]
    [InlineData("('a', 'b')", "ref refs short mixed to")]
    public async Task Schema_typed_IDREF_nodes_are_found(string ids, string expectedNames)
    {
        var (items, error) = await RunAsync($"string-join(idref({ids}, $d) ! local-name(), ' ')");
        error.Should().BeNull();
        items.Should().ContainSingle().Which.Should().Be(expectedNames);
    }

    /// <summary>
    /// A list whose values all validated as the non-IDREF member has no IDREF in its typed
    /// value, so its tokens are not candidates although the type could hold one.
    /// </summary>
    [Fact]
    public async Task A_list_with_no_IDREF_value_is_not_a_candidate()
    {
        var (items, _) = await RunAsync("exists(idref('none', $d)[local-name() = 'words'])");
        items.Should().ContainSingle().Which.Should().Be(false);
    }

    [Theory]
    [InlineData("(1, 2, 3)[idref('a', .)]")]
    [InlineData("(1, 2, 3)[idref('a')]")]
    public async Task An_atomic_value_where_the_node_belongs_is_a_type_error(string body)
    {
        (await RunAsync(body)).Error.Should().Be("XPTY0004");
    }
}
