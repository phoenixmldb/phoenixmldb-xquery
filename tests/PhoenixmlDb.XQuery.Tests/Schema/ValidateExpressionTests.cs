using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using PhoenixmlDb.XQuery;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Schema;

/// <summary>
/// Runtime tests for the <c>validate</c> expression. Reproduce Martin Honnen's
/// 2026-05-08 reports: a valid document was being rejected because the operator
/// passed XdmNode.StringValue (text content) to the validator instead of a
/// proper XML serialization, and relative <c>at</c> hints on <c>import schema</c>
/// were resolved against the application CWD rather than the query's base URI.
/// </summary>
public sealed class ValidateExpressionTests : System.IDisposable
{
    private readonly string _tempDir;
    private readonly XQueryFacade _facade = new();

    public ValidateExpressionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"phoenixmldb-validate-{System.Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task ValidateStrict_AcceptsConformingDocument()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "schema.xsd"), """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
              <xs:element name="root">
                <xs:complexType>
                  <xs:sequence>
                    <xs:element name="item" maxOccurs="unbounded">
                      <xs:complexType>
                        <xs:sequence>
                          <xs:element name="name" type="xs:string"/>
                          <xs:element name="value" type="xs:decimal"/>
                        </xs:sequence>
                      </xs:complexType>
                    </xs:element>
                  </xs:sequence>
                </xs:complexType>
              </xs:element>
            </xs:schema>
            """);

        var query = """
            import schema '' at 'schema.xsd';
            validate strict {
              document { <root><item><name>item 1</name><value>15</value></item></root> }
            }
            """;

        var queryBaseUri = new System.Uri(Path.Combine(_tempDir, "test.xq")).AbsoluteUri;
        var result = await _facade.EvaluateAsync(query, inputXml: null, baseUri: null,
            queryBaseUri: new System.Uri(queryBaseUri));

        result.Should().Contain("<root>");
        result.Should().Contain("<item>");
        result.Should().Contain("<name>item 1</name>");
    }

    [Fact]
    public async Task ValidateStrict_RejectsNonConformingDocument()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "schema.xsd"), """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
              <xs:element name="root">
                <xs:complexType>
                  <xs:sequence>
                    <xs:element name="item" type="xs:string"/>
                  </xs:sequence>
                </xs:complexType>
              </xs:element>
            </xs:schema>
            """);

        var query = """
            import schema '' at 'schema.xsd';
            validate strict {
              document { <root><wrong-element>oops</wrong-element></root> }
            }
            """;

        var queryBaseUri = new System.Uri(Path.Combine(_tempDir, "test.xq")).AbsoluteUri;
        var act = async () => await _facade.EvaluateAsync(query, inputXml: null, baseUri: null,
            queryBaseUri: new System.Uri(queryBaseUri));

        await act.Should().ThrowAsync<SchemaValidationException>();
    }

    [Fact]
    public async Task ImportSchema_ResolvesRelativeHintAgainstQueryBaseUri()
    {
        // Issue 2: a relative `at 'schema.xsd'` should resolve against the query's
        // base URI, not the process CWD. Place the schema beside the query and call
        // from a different CWD.
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "side-by-side.xsd"), """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
              <xs:element name="x" type="xs:string"/>
            </xs:schema>
            """);

        var query = """
            import schema '' at 'side-by-side.xsd';
            validate strict { <x>hello</x> }
            """;

        var queryBaseUri = new System.Uri(Path.Combine(_tempDir, "test.xq")).AbsoluteUri;
        // Switch CWD to somewhere that does NOT contain side-by-side.xsd so the
        // pre-fix behavior would fail with FileNotFoundException.
        var savedCwd = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(Path.GetTempPath());
        try
        {
            var result = await _facade.EvaluateAsync(query, inputXml: null, baseUri: null,
                queryBaseUri: new System.Uri(queryBaseUri));
            result.Should().Contain("<x>hello</x>");
        }
        finally
        {
            Directory.SetCurrentDirectory(savedCwd);
        }
    }
}
