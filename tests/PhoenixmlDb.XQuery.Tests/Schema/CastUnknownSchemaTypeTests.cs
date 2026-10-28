using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Schema;

/// <summary>
/// The target of cast or castable must be a simple type of the in-scope schema types; an
/// unknown name is the static error XQST0052. It surfaced only when the cast ran, as an
/// unhandled SchemaException (QT3 K2-DefaultNamespaceProlog-12a).
/// </summary>
public class CastUnknownSchemaTypeTests
{
    private const string Xsd = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" targetNamespace="urn:u">
          <xs:simpleType name="size"><xs:restriction base="xs:integer"/></xs:simpleType>
        </xs:schema>
        """;

    private static IReadOnlyList<string> CompileErrorCodes(string query)
    {
        var schemas = new XsdSchemaProvider();
        schemas.AddFromString("urn:u", Xsd);
        var engine = new QueryEngine(nodeProvider: new XdmDocumentStore(), schemaProvider: schemas);
        return engine.Compile(query).Errors.Select(e => e.Code).ToList();
    }

    [Theory]
    [InlineData("1 cast as Q{urn:x}byte")]
    [InlineData("1 castable as Q{urn:x}byte")]
    [InlineData("<b xmlns='http://www.w3.org/1999/XSL/Transform'>{2 cast as byte}</b>")]
    [InlineData("import schema namespace u = 'urn:u'; 1 cast as u:nosuch")]
    public void An_unknown_cast_target_is_XQST0052(string query)
        => CompileErrorCodes(query).Should().Contain("XQST0052");

    [Theory]
    [InlineData("import schema namespace u = 'urn:u'; 1 cast as u:size")]
    [InlineData("1 cast as xs:byte")]
    public void A_known_cast_target_compiles(string query)
        => CompileErrorCodes(query).Should().BeEmpty();
}
