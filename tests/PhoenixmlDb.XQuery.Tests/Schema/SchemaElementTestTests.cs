using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using PhoenixmlDb.XQuery;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Schema;

/// <summary>
/// <c>schema-element(N)</c> and <c>schema-attribute(N)</c> against a schema with a target
/// namespace. A prefixed name was looked up in no namespace, so every such test failed XPST0008
/// for a declaration that exists; as the node test of a step it matched nothing at all; and as a
/// sequence type the declaration's namespace was dropped before the provider saw it
/// (QT3 cbcl-schema-element-3..8, cbcl-schema-attribute-1/2).
/// </summary>
public sealed class SchemaElementTestTests : System.IDisposable
{
    private const string Prolog = """
        import schema namespace t = 'urn:t' at 'schema.xsd';
        declare default element namespace 'urn:t';
        declare variable $raw := <t:root t:a="7"><t:head>h</t:head><t:member>m</t:member><t:deep>d</t:deep><t:other>o</t:other></t:root>;
        declare variable $valid := validate strict { $raw };

        """;

    private readonly string _tempDir;
    private readonly XQueryFacade _facade = new();

    public SchemaElementTestTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"phoenixmldb-schema-element-{System.Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "schema.xsd"), """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:t="urn:t"
                       targetNamespace="urn:t" elementFormDefault="qualified">
              <xs:element name="head" type="xs:string"/>
              <xs:element name="member" type="xs:string" substitutionGroup="t:head"/>
              <xs:element name="deep" type="xs:string" substitutionGroup="t:member"/>
              <xs:element name="other" type="xs:string"/>
              <xs:attribute name="a" type="xs:integer"/>
              <xs:element name="root">
                <xs:complexType>
                  <xs:sequence>
                    <xs:element ref="t:head" maxOccurs="unbounded"/>
                    <xs:element ref="t:other"/>
                  </xs:sequence>
                  <xs:attribute ref="t:a"/>
                </xs:complexType>
              </xs:element>
            </xs:schema>
            """);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { }
    }

    private async Task<string> Run(string body)
    {
        var queryBaseUri = new System.Uri(Path.Combine(_tempDir, "test.xq"));
        var result = await _facade.EvaluateAsync(Prolog + body, inputXml: null, baseUri: null,
            queryBaseUri: queryBaseUri);
        return result.Trim();
    }

    [Fact]
    public async Task Step_SelectsTheDeclarationAndItsSubstitutionGroup()
    {
        // t:deep substitutes for t:member, which substitutes for t:head; t:other does neither.
        (await Run("string-join($valid/schema-element(t:head) ! local-name(), ' ')"))
            .Should().Be("head member deep");
    }

    [Fact]
    public async Task Step_SelectsNothingFromAnUnvalidatedTree()
    {
        // Same names, but xs:untyped is not derived from the declared type.
        (await Run("count($raw/schema-element(t:head))")).Should().Be("0");
    }

    [Fact]
    public async Task InstanceOf_NeedsTheNameAndTheTypeAnnotation()
    {
        (await Run("""
            string-join((
              $valid/t:head instance of schema-element(t:head),
              $raw/t:head instance of schema-element(t:head),
              $valid/t:member instance of schema-element(t:head),
              $valid/t:other instance of schema-element(t:head)) ! string(), ' ')
            """)).Should().Be("true false true false");
    }

    [Fact]
    public async Task AnonymousDeclaredType_MatchesAValidatedElement()
    {
        // t:root's type has no name of its own; the provider gives it one, which only a
        // validated element carries.
        (await Run("string-join(($valid instance of schema-element(t:root), $raw instance of schema-element(t:root)) ! string(), ' ')"))
            .Should().Be("true false");
    }

    [Fact]
    public async Task SchemaAttribute_AsAStep_UsesTheAttributeAxis()
    {
        (await Run("string-join((count($valid/schema-attribute(t:a)), count($raw/schema-attribute(t:a))) ! string(), ' ')"))
            .Should().Be("1 0");
    }

    [Fact]
    public async Task UnprefixedName_TakesTheDefaultElementNamespace()
    {
        (await Run("count($valid/schema-element(member))")).Should().Be("2");
    }

    [Fact]
    public async Task UndeclaredName_IsStillAStaticError()
    {
        var act = () => Run("count($valid/schema-element(t:nosuch))");
        (await act.Should().ThrowAsync<System.Exception>()).Which.Message.Should().Contain("not found in in-scope schema definitions");
    }
}
