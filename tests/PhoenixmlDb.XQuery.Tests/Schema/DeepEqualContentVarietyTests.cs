using System.IO;
using System.Threading.Tasks;
using AwesomeAssertions;
using PhoenixmlDb.XQuery;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Schema;

/// <summary>
/// fn:deep-equal of two validated elements (F&amp;O 3.1 §14.2.1): types of different content
/// varieties (simple, element-only, mixed, empty) are never equal, and two elements of simple
/// content compare their typed values. Their text and children were compared, so xs:integer
/// 2012 equalled xs:gYear 2012, and a mixed element a simple one of the same text
/// (QT3 K2-SeqDeepEqualFunc-36..42).
/// </summary>
public sealed class DeepEqualContentVarietyTests : System.IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"phoenixmldb-deep-equal-{System.Guid.NewGuid():N}");
    private readonly XQueryFacade _facade = new();

    public DeepEqualContentVarietyTests()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "p.xsd"), """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:p="urn:p" targetNamespace="urn:p" elementFormDefault="qualified">
              <xs:complexType name="elementOnly"><xs:sequence><xs:element name="e" minOccurs="0"/></xs:sequence></xs:complexType>
              <xs:complexType name="mixed" mixed="true"><xs:sequence><xs:element name="e" minOccurs="0"/></xs:sequence></xs:complexType>
              <xs:complexType name="simple"><xs:simpleContent><xs:extension base="xs:string"/></xs:simpleContent></xs:complexType>
              <xs:element name="elementOnly"><xs:complexType><xs:sequence><xs:element name="e" type="p:elementOnly"/></xs:sequence></xs:complexType></xs:element>
              <xs:element name="mixed"><xs:complexType><xs:sequence><xs:element name="e" type="p:mixed"/></xs:sequence></xs:complexType></xs:element>
              <xs:element name="simple"><xs:complexType><xs:sequence><xs:element name="e" type="p:simple"/></xs:sequence></xs:complexType></xs:element>
            </xs:schema>
            """);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { }
    }

    private async Task<string> Run(string body)
        => (await _facade.EvaluateAsync("import schema namespace p = 'urn:p' at 'p.xsd';\n" + body,
            inputXml: null, baseUri: null, queryBaseUri: new System.Uri(Path.Combine(_tempDir, "q.xq")))).Trim();

    [Fact]
    public async Task Different_content_varieties_are_not_deep_equal()
        => (await Run("""
            let $o := validate strict { <p:elementOnly><p:e><p:e/></p:e></p:elementOnly> }
            let $m := validate strict { <p:mixed><p:e><p:e/></p:e></p:mixed> }
            let $mt := validate strict { <p:mixed><p:e>x</p:e></p:mixed> }
            let $s := validate strict { <p:simple><p:e>x</p:e></p:simple> }
            let $m2 := validate strict { <p:mixed><p:e><p:e/></p:e></p:mixed> }
            return string-join((deep-equal($o/p:e, $m/p:e), deep-equal($mt/p:e, $s/p:e), deep-equal($m/p:e, $m2/p:e)) ! string(), ' ')
            """)).Should().Be("false false true");

    [Fact]
    public async Task Simple_content_compares_typed_values()
        => (await Run("""
            string-join((
              deep-equal(validate type xs:integer { <e>2012</e> }, validate type xs:gYear { <e>2012</e> }),
              deep-equal(validate type xs:integer { <e>07</e> }, validate type xs:integer { <e>7</e> })) ! string(), ' ')
            """)).Should().Be("false true");
}
