using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Schema;

/// <summary>
/// A schema type derived from xs:QName (or a union with such a member) is namespace-sensitive:
/// its lexical form has a prefix to resolve in the static context. The facet check parsed it
/// with no namespace resolver and the built-in cast had no context, so every such cast crashed
/// with a NullReferenceException (QT3 qname-cast-*, CastAs-UnionType-10..33).
/// </summary>
public class NamespaceSensitiveSchemaCastTests
{
    private const string Ns = "urn:test:qname-types";

    private const string Xsd = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:t="urn:test:qname-types"
                   targetNamespace="urn:test:qname-types">
          <xs:simpleType name="qnameBased"><xs:restriction base="xs:QName"/></xs:simpleType>
          <xs:simpleType name="qnameOrInt"><xs:union memberTypes="xs:QName xs:integer"/></xs:simpleType>
        </xs:schema>
        """;

    private static async Task<string> Eval(string body)
    {
        var schemas = new XsdSchemaProvider();
        schemas.AddFromString(Ns, Xsd);
        var store = new XdmDocumentStore();
        var engine = new PhoenixmlDb.XQuery.Execution.QueryEngine(nodeProvider: store, documentResolver: store, schemaProvider: schemas);
        var compiled = engine.Compile($"import schema namespace t = '{Ns}';\n{body}");
        if (!compiled.Success)
            throw new InvalidOperationException(string.Join("; ", compiled.Errors));
        var items = new List<object?>();
        await foreach (var i in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
            items.Add(i);
        return string.Join(",", items);
    }

    [Theory]
    [InlineData("string(t:qnameBased('local'))", "local")]
    [InlineData("namespace-uri-from-QName(t:qnameBased('xs:integer'))", "http://www.w3.org/2001/XMLSchema")]
    [InlineData("declare namespace p = 'urn:p'; namespace-uri-from-QName('p:x' cast as t:qnameBased)", "urn:p")]
    [InlineData("namespace-uri-from-QName('xs:integer' cast as t:qnameOrInt)", "http://www.w3.org/2001/XMLSchema")]
    [InlineData("('12' cast as t:qnameOrInt) instance of xs:integer", "True")]
    [InlineData("'nope:x' castable as t:qnameBased", "False")]
    [InlineData("'xs:integer' castable as t:qnameBased", "True")]
    public async Task A_qname_based_schema_type_resolves_its_prefix(string query, string expected) =>
        (await Eval(query)).Should().Be(expected);
}
