using FluentAssertions;
using PhoenixmlDb.Xdm.Nodes;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Schema;

/// <summary>
/// Each validated (annotated) document must be its own tree in the store. The annotating parse
/// took one node id and numbered the tree from it without reserving the rest, and used document
/// id 0 for every tree: a second validated document reused the first one's ids, overwrote its
/// nodes, and `/` from the first resolved into the second. Surfaced when the QT3 harness began
/// validating sources into one shared store.
/// </summary>
public class AnnotatedParseIdentityTests
{
    private const string Ns = "urn:test:annotated-identity";

    private const string Xsd = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" targetNamespace="urn:test:annotated-identity"
                   elementFormDefault="qualified">
          <xs:element name="a"><xs:complexType><xs:sequence><xs:element name="n" type="xs:integer"/></xs:sequence></xs:complexType></xs:element>
          <xs:element name="b"><xs:complexType><xs:sequence><xs:element name="m" type="xs:string" maxOccurs="3"/></xs:sequence></xs:complexType></xs:element>
        </xs:schema>
        """;

    [Fact]
    public async Task A_second_validated_document_does_not_replace_the_first()
    {
        var store = new XdmDocumentStore();
        var schemas = new XsdSchemaProvider();
        schemas.AddFromString(Ns, Xsd);
        var first = schemas.ValidateAndAnnotate($"<a xmlns='{Ns}'><n>42</n></a>", store, ValidationMode.Strict);
        var second = schemas.ValidateAndAnnotate($"<b xmlns='{Ns}'><m>x</m><m>y</m></b>", store, ValidationMode.Strict);

        ((XdmDocument)first!).Document.Should().NotBe(((XdmDocument)second!).Document);

        var engine = new PhoenixmlDb.XQuery.Execution.QueryEngine(nodeProvider: store, documentResolver: store, schemaProvider: schemas);
        var compiled = engine.Compile($"declare namespace t = '{Ns}'; string(/t:a/t:n) || '/' || count(/t:b)");
        var items = new List<object?>();
        await foreach (var i in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext(initialContextItem: first)))
            items.Add(i);
        items.Should().Equal("42/0");
    }
}
