using System.IO;
using System.Threading.Tasks;
using AwesomeAssertions;
using PhoenixmlDb.XQuery;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Schema;

/// <summary>
/// A cast to a schema list type whose item type is a union with an xs:QName member: each token
/// is cast to the union with the query's prefixes, so "a b xs:integer" gives two NCNames and a
/// QName. The provider computes list items from the text alone, which a QName cannot be, so the
/// list was kept whole, and a union with such a list as a member refused the value
/// (QT3 CastAs-UnionType-28..32).
/// </summary>
public sealed class QNameListCastTests : System.IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"phoenixmldb-qname-list-{System.Guid.NewGuid():N}");
    private readonly XQueryFacade _facade = new();

    public QNameListCastTests()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "s.xsd"), """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:s="urn:s" targetNamespace="urn:s">
              <xs:simpleType name="sensitiveUnion"><xs:union memberTypes="xs:NCName xs:QName"/></xs:simpleType>
              <xs:simpleType name="sensitiveList"><xs:list itemType="s:sensitiveUnion"/></xs:simpleType>
              <xs:simpleType name="unionOfLists"><xs:union memberTypes="xs:IDREFS s:sensitiveList"/></xs:simpleType>
            </xs:schema>
            """);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { }
    }

    [Theory]
    [InlineData("s:sensitiveList")]
    [InlineData("s:unionOfLists")]
    public async Task Each_token_is_cast_with_the_query_prefixes(string type)
    {
        var result = await _facade.EvaluateAsync($"""
            import schema namespace s = 'urn:s' at 's.xsd';
            let $r := 'a b xs:integer' cast as {type}
            return string-join((count($r), $r[3] instance of xs:QName, namespace-uri-from-QName($r[3])) ! string(), ' ')
            """, inputXml: null, baseUri: null, queryBaseUri: new System.Uri(Path.Combine(_tempDir, "q.xq")));
        result.Trim().Should().Be("3 true http://www.w3.org/2001/XMLSchema");
    }

    /// <summary>
    /// Q{uri}local names a type of the schema for that namespace, as a prefix bound to it does.
    /// It was looked up by its local name among the built-in types: "not a recognized type".
    /// </summary>
    [Fact]
    public async Task A_braced_uri_type_name_is_a_schema_type()
    {
        var result = await _facade.EvaluateAsync("""
            import schema namespace s = 'urn:s' at 's.xsd';
            let $v := 'x' cast as Q{urn:s}sensitiveUnion
            return string-join(($v instance of xs:NCName, $v instance of Q{urn:s}sensitiveUnion) ! string(), ' ')
            """, inputXml: null, baseUri: null, queryBaseUri: new System.Uri(Path.Combine(_tempDir, "q.xq")));
        result.Trim().Should().Be("true true");
    }
}
