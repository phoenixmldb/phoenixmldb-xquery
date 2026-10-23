using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// A string is cast after whitespace collapse for every atomic type other than xs:string and
/// xs:untypedAtomic (XPath 3.1 §19.2). The number parsers skip surrounding whitespace on their own;
/// the boolean and the INF/NaN matches did not. So the typed value of an element of a type derived
/// from xs:boolean holding "  true  " stayed xs:untypedAtomic, and comparing it with an
/// xs:boolean raised XPTY0004 (W3C type-expr-0201).
/// </summary>
public class CastWhitespaceTests
{
    private const string Xsd = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" targetNamespace="urn:cast-ws" xmlns:p="urn:cast-ws">
          <xs:simpleType name="flag"><xs:restriction base="xs:boolean"><xs:whiteSpace value="collapse"/></xs:restriction></xs:simpleType>
          <xs:element name="r"><xs:complexType><xs:sequence>
            <xs:element name="f" type="p:flag"/><xs:element name="b" type="xs:boolean"/>
          </xs:sequence></xs:complexType></xs:element>
        </xs:schema>
        """;

    private static async Task<string> Eval(string body)
    {
        var schemas = new XsdSchemaProvider();
        schemas.AddFromString("urn:cast-ws", Xsd);
        var store = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store, schemaProvider: schemas);
        var compiled = engine.Compile($$"""
            import schema namespace p = "urn:cast-ws";
            let $r := validate { <p:r><f>  true  </f><b>
              false </b></p:r> }
            return {{body}}
            """);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors));
        var items = new List<object?>();
        await foreach (var i in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
            items.Add(i);
        return string.Join(",", items.Select(i => i?.ToString() ?? ""));
    }

    [Theory]
    [InlineData("data($r/f) instance of xs:boolean", "True")]
    [InlineData("data($r/f) eq true()", "True")]
    [InlineData("data($r/b) instance of xs:boolean", "True")]
    [InlineData("data($r/b) eq data($r/f)", "False")]
    [InlineData("xs:untypedAtomic(' true ') cast as xs:boolean", "True")]
    [InlineData("string(xs:untypedAtomic(' INF ') cast as xs:double)", "INF")]
    [InlineData("string-length(xs:untypedAtomic(' a ') cast as xs:string)", "3")]
    public async Task Whitespace_is_collapsed_before_a_non_string_cast(string body, string expected)
        => (await Eval(body)).Should().Be(expected);
}
