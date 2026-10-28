using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Schema;

/// <summary>
/// Each simple type of an imported schema has a constructor function of its name, arity 1; a
/// function declared with that name and arity is XQST0034 (XQuery 3.1 §3.18.4). The declaration
/// was accepted and hid the constructor (QT3 user-defined-11).
/// </summary>
public class SchemaConstructorCollisionTests
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
        var store = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store, schemaProvider: schemas);
        return engine.Compile(query).Errors.Select(e => e.Code).ToList();
    }

    [Fact]
    public void A_function_named_like_a_schema_type_constructor_is_XQST0034()
        => CompileErrorCodes("import schema namespace u = 'urn:u'; declare function u:size($a as xs:integer) { $a + 1 }; u:size(16)")
            .Should().Contain("XQST0034");

    [Theory]
    [InlineData("import schema namespace u = 'urn:u'; declare function u:size($a, $b) { $a + $b }; u:size(1, 2)")]
    [InlineData("import schema namespace u = 'urn:u'; declare function u:other($a) { $a }; u:other(1)")]
    [InlineData("declare namespace u = 'urn:u'; declare function u:size($a) { $a }; u:size(1)")]
    public void Another_arity_name_or_unimported_namespace_is_allowed(string query)
        => CompileErrorCodes(query).Should().BeEmpty();
}
