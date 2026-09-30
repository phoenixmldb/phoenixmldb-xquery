using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Schema;

/// <summary>
/// Two schemas that each import the XML namespace schema (xml.xsd) from a different location
/// must still compile together. The schema set held two copies and failed with "The global
/// attribute 'xml:lang' has already been declared" (QT3 Catalog001-014).
/// </summary>
public sealed class XmlNamespaceImportTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("xmlns-import-");

    public void Dispose() => _dir.Delete(recursive: true);

    private const string XmlXsd = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" targetNamespace="http://www.w3.org/XML/1998/namespace">
          <xs:attribute name="lang" type="xs:language"/>
        </xs:schema>
        """;

    private string Write(string relativePath, string content)
    {
        var path = Path.Combine(_dir.FullName, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static string SchemaImportingXml(string ns, string xmlXsdLocation) => $"""
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" targetNamespace="{ns}" elementFormDefault="qualified">
          <xs:import namespace="http://www.w3.org/XML/1998/namespace" schemaLocation="{xmlXsdLocation}"/>
          <xs:element name="e"><xs:complexType><xs:simpleContent><xs:extension base="xs:string">
            <xs:attribute ref="xml:lang"/></xs:extension></xs:simpleContent></xs:complexType></xs:element>
        </xs:schema>
        """;

    [Fact]
    public void Two_imports_of_xml_xsd_from_different_places_compile_together()
    {
        Write("one/xml.xsd", XmlXsd);
        Write("two/xml.xsd", XmlXsd);
        var a = Write("one/a.xsd", SchemaImportingXml("urn:a", "xml.xsd"));
        var b = Write("two/b.xsd", SchemaImportingXml("urn:b", "xml.xsd"));

        var schemas = new XsdSchemaProvider();
        schemas.ImportSchema("urn:a", [a]);
        var act = () => schemas.ImportSchema("urn:b", [b]);
        act.Should().NotThrow();
        schemas.ValidateXml("<e xmlns='urn:b' xml:lang='en'>x</e>", ValidationMode.Strict);
    }
}
