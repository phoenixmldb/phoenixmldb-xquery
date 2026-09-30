using FluentAssertions;
using PhoenixmlDb.Xdm.Nodes;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Schema;

/// <summary>
/// The nilled property (XDM 3.1 §6.2.2) is true for a validated element carrying xsi:nil="true",
/// and element(N, T) excludes nilled elements while element(N, T?) admits them (XQuery 3.1
/// §2.5.5.3). fn:nilled always answered false, which was right only while nothing was validated.
/// </summary>
public class NilledTests
{
    private const string Ns = "urn:test:nilled";

    private const string Xsd = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" targetNamespace="urn:test:nilled" elementFormDefault="qualified">
          <xs:element name="r"><xs:complexType><xs:sequence>
            <xs:element name="v" type="xs:int" nillable="true" maxOccurs="2"/>
          </xs:sequence></xs:complexType></xs:element>
        </xs:schema>
        """;

    private const string Doc = """<r xmlns="urn:test:nilled" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"><v>5</v><v xsi:nil="true"/></r>""";

    private static async Task<string> Eval(string query, bool validate, string? documentUri = null)
    {
        var store = new XdmDocumentStore();
        var schemas = new XsdSchemaProvider();
        schemas.AddFromString(Ns, Xsd);
        var doc = validate
            ? schemas.ValidateAndAnnotate(Doc, store, ValidationMode.Strict, null, null, documentUri)
            : store.LoadFromString(Doc, "urn:doc");
        var engine = new PhoenixmlDb.XQuery.Execution.QueryEngine(nodeProvider: store, documentResolver: store, schemaProvider: schemas);
        var compiled = engine.Compile($"declare namespace t = '{Ns}'; {query}");
        var items = new List<object?>();
        await foreach (var i in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext(initialContextItem: doc)))
            items.Add(i);
        return string.Join(",", items);
    }

    [Theory]
    [InlineData("string-join((/t:r/t:v ! string(nilled(.))), ',')", true, "false,true")]
    [InlineData("string-join((/t:r/t:v ! string(nilled(.))), ',')", false, "false,false")]
    [InlineData("count(/t:r/t:v[. instance of element(t:v, xs:int)])", true, "1")]
    [InlineData("count(/t:r/t:v[. instance of element(t:v, xs:int?)])", true, "2")]
    public async Task Nilled_follows_validation_and_xsi_nil(string query, bool validate, string expected) =>
        (await Eval(query, validate)).Should().Be(expected);

    [Fact]
    public async Task A_validated_document_keeps_its_uri() =>
        (await Eval("base-uri(/)", validate: true, documentUri: "file:///tmp/nilled.xml")).Should().Be("file:///tmp/nilled.xml");
}
