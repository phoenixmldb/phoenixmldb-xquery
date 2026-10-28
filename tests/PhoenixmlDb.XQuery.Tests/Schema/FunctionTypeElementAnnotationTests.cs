using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Schema;

/// <summary>
/// In a function type, element(*, U) is a subtype of element(*, V) only when U derives from V.
/// Both annotations were only required to be present, so a function taking element(*, A)
/// matched a function type over element(*, B) for any B (QT3 FunctionCall-052..054).
/// </summary>
public class FunctionTypeElementAnnotationTests
{
    private const string Xsd = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" targetNamespace="urn:u">
          <xs:simpleType name="base"><xs:restriction base="xs:integer"/></xs:simpleType>
          <xs:simpleType name="derived"><xs:restriction base="u:base" xmlns:u="urn:u"><xs:maxInclusive value="9"/></xs:restriction></xs:simpleType>
          <xs:simpleType name="other"><xs:restriction base="xs:string"/></xs:simpleType>
        </xs:schema>
        """;

    private static async Task<string> Eval(string query)
    {
        var schemas = new XsdSchemaProvider();
        schemas.AddFromString("urn:u", Xsd);
        var engine = new QueryEngine(nodeProvider: new XdmDocumentStore(), schemaProvider: schemas);
        var compiled = engine.Compile("import schema namespace u = 'urn:u'; " + query);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors));
        var items = new List<object?>();
        await foreach (var i in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
            items.Add(i);
        return string.Join(",", items.Select(i => i?.ToString() ?? ""));
    }

    [Theory]
    // Parameters are contravariant: a function over the base type accepts the derived one.
    [InlineData("function($e as element(*, u:base)) { 1 } instance of function(element(*, u:derived)) as item()*", "true")]
    [InlineData("function($e as element(*, u:derived)) { 1 } instance of function(element(*, u:base)) as item()*", "false")]
    [InlineData("function($e as element(*, u:other)) { 1 } instance of function(element(*, u:base)) as item()*", "false")]
    [InlineData("function($e as element(*, u:base)) { 1 } instance of function(element(*, u:base)) as item()*", "true")]
    // Return types are covariant.
    [InlineData("function() as element(*, u:derived)? { () } instance of function() as element(*, u:base)?", "true")]
    [InlineData("function() as element(*, u:base)? { () } instance of function() as element(*, u:derived)?", "false")]
    public async Task Element_annotations_follow_derivation(string query, string expected)
        => (await Eval($"string({query})")).Should().Be(expected);
}
