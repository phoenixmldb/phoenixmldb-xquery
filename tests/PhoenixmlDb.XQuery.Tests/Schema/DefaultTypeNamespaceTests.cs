using System.IO;
using System.Threading.Tasks;
using AwesomeAssertions;
using PhoenixmlDb.XQuery;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Schema;

/// <summary>
/// An unprefixed type name in element() and attribute() tests is in the default namespace for
/// elements and types (XQuery 3.1 §3.1.1), which <c>import schema default element namespace</c>
/// declares. It was read as an xs type: XPST0008 "Unknown schema type 'restrictedType'" in a
/// sequence type, and a step or instance-of test that matched nothing (QT3 ForExprType026..041,
/// step-expr-si-1..3). A built-in type name keeps reading as one.
/// </summary>
public sealed class DefaultTypeNamespaceTests : System.IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"phoenixmldb-default-type-ns-{System.Guid.NewGuid():N}");
    private readonly XQueryFacade _facade = new();

    public DefaultTypeNamespaceTests()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "d.xsd"), """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:d="urn:d" targetNamespace="urn:d" elementFormDefault="qualified">
              <xs:simpleType name="code"><xs:restriction base="xs:string"><xs:enumeration value="ok"/></xs:restriction></xs:simpleType>
              <xs:complexType name="item"><xs:sequence><xs:element name="v" type="xs:integer"/></xs:sequence><xs:attribute name="c" type="d:code"/></xs:complexType>
              <xs:element name="r"><xs:complexType><xs:sequence><xs:element name="i" type="d:item" maxOccurs="unbounded"/></xs:sequence></xs:complexType></xs:element>
            </xs:schema>
            """);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { }
    }

    private async Task<string> Run(string body)
        => (await _facade.EvaluateAsync("import schema default element namespace 'urn:d' at 'd.xsd';\n"
            + "let $r := validate strict { <r xmlns='urn:d'><i c='ok'><v>1</v></i><i><v>2</v></i></r> }\nreturn " + body,
            inputXml: null, baseUri: null, queryBaseUri: new System.Uri(Path.Combine(_tempDir, "q.xq")))).Trim();

    [Theory]
    [InlineData("count($r//element(*, item))", "2")]
    [InlineData("count($r/*[. instance of element(i, item)])", "2")]
    [InlineData("count($r//@*[. instance of attribute(c, code)])", "1")]
    [InlineData("string-join(for $i as element(*, item) in $r/i return string($i/v), ' ')", "1 2")]
    [InlineData("count($r//element(*, integer))", "2")]
    public async Task An_unprefixed_type_is_in_the_default_namespace(string query, string expected)
        => (await Run(query)).Should().Be(expected);
}
