using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// A node of a list type atomizes to one value per token, and a general comparison compares each
/// of them. The items of a list of xs:QName are QNames, resolved through the node's namespaces.
/// The items were xs:untypedAtomic, and the comparison took the whole list as one value:
/// <c>@name = @use</c> raised XPTY0004 for a list of QNames (W3C validation-0401), and a token of
/// an xs:NMTOKENS attribute matched nothing.
/// </summary>
public class SchemaListComparisonTests
{
    private const string Ns = "urn:test:lists";

    private const string Xsd = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" targetNamespace="urn:test:lists">
          <xs:element name="s">
            <xs:complexType>
              <xs:attribute name="name" type="xs:QName"/>
              <xs:attribute name="use"><xs:simpleType><xs:list itemType="xs:QName"/></xs:simpleType></xs:attribute>
              <xs:attribute name="tokens" type="xs:NMTOKENS"/>
            </xs:complexType>
          </xs:element>
        </xs:schema>
        """;

    private static async Task<string> Eval(string body)
    {
        var schemas = new XsdSchemaProvider();
        schemas.AddFromString(Ns, Xsd);
        var store = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store, schemaProvider: schemas);
        var compiled = engine.Compile($$"""
            import schema namespace t = "{{Ns}}";
            let $e := validate { <t:s name="t:B" use="t:A t:B" tokens="a b"/> }
            return {{body}}
            """);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors));
        var items = new List<object?>();
        await foreach (var i in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
            items.Add(i);
        return string.Join(",", items.Select(i => i?.ToString() ?? ""));
    }

    [Theory]
    [InlineData("every $i in data($e/@use) satisfies $i instance of xs:QName", "True")]
    [InlineData("$e/@name = $e/@use", "True")]
    [InlineData("$e/@use = xs:QName('t:A')", "True")]
    [InlineData("$e/@use = xs:QName('t:C')", "False")]
    [InlineData("$e/@tokens = 'b'", "True")]
    [InlineData("$e/@tokens = 'c'", "False")]
    public async Task Each_item_of_a_list_is_compared(string query, string expected)
        => (await Eval(query)).Should().Be(expected);
}
