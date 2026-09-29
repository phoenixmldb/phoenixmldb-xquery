using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Schema;

/// <summary>
/// XSD 1.1 conditional inclusion (vc:minVersion / vc:maxVersion), applied as the XSD 1.0
/// processor the engine validates with. A schema marking a 1.1-only construct
/// (xs:assert vc:minVersion="1.1") failed to load at all; the construct is now skipped, as the
/// schema instructs a 1.0 processor to do (W3C stream-107/-108/-109).
/// </summary>
public sealed class XsdVersionControlTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "phx-vc-" + Guid.NewGuid().ToString("N"));

    public XsdVersionControlTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private const string Xsd = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:vc="http://www.w3.org/2007/XMLSchema-versioning">
          <xs:element name="list">
            <xs:complexType>
              <xs:sequence><xs:element name="item" type="xs:integer" maxOccurs="unbounded"/></xs:sequence>
              <xs:assert test="count(item) le 3" vc:minVersion="1.1"/>
              <xs:attribute name="kept-10" type="xs:string" vc:minVersion="1.0"/>
              <xs:attribute name="kept-max" type="xs:string" vc:maxVersion="1.1"/>
              <xs:attribute name="dropped-max" type="xs:string" vc:maxVersion="1.0"/>
            </xs:complexType>
          </xs:element>
        </xs:schema>
        """;

    [Fact]
    public void A_schema_with_a_1_1_only_assertion_loads_from_a_string()
    {
        var schemas = new XsdSchemaProvider();
        var act = () => schemas.AddFromString("", Xsd);
        act.Should().NotThrow();
    }

    [Fact]
    public void A_schema_with_a_1_1_only_assertion_loads_from_a_location()
    {
        var path = Path.Combine(_dir, "list.xsd");
        File.WriteAllText(path, Xsd);
        var schemas = new XsdSchemaProvider();
        var act = () => schemas.ImportSchema("", [path]);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("""<list kept-10="a" kept-max="b"><item>1</item></list>""", true)]
    // Excluded by vc:maxVersion="1.0": not declared, so not allowed.
    [InlineData("""<list dropped-max="c"><item>1</item></list>""", false)]
    // The rest of the schema still applies.
    [InlineData("""<list><item>x</item></list>""", false)]
    public void The_1_0_parts_still_validate(string instance, bool valid)
    {
        var schemas = new XsdSchemaProvider();
        schemas.AddFromString("", Xsd);
        var act = () => schemas.ValidateXml(instance, ValidationMode.Strict);
        if (valid) act.Should().NotThrow();
        else act.Should().Throw<SchemaValidationException>();
    }
}

/// <summary>
/// A schema referring to an XSD 1.1 built-in type System.Xml does not know (xs:dayTimeDuration,
/// xs:yearMonthDuration, xs:dateTimeStamp) loads, with the reference mapped to the 1.0 base type,
/// instead of failing whole (QT3 validateexpr-28..42 and siblings on one shared schema).
/// </summary>
public sealed class XsdBuiltInTypeMappingTests
{
    private const string Xsd = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
          <xs:element name="t">
            <xs:complexType>
              <xs:sequence>
                <xs:element name="d" type="xs:dayTimeDuration"/>
                <xs:element name="y" type="xs:yearMonthDuration"/>
                <xs:element name="s" type="xs:dateTimeStamp"/>
              </xs:sequence>
            </xs:complexType>
          </xs:element>
        </xs:schema>
        """;

    [Fact]
    public void The_schema_loads()
    {
        var schemas = new XsdSchemaProvider();
        var act = () => schemas.AddFromString("", Xsd);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("<t><d>PT1H</d><y>P1Y</y><s>2026-09-29T10:00:00Z</s></t>", true)]
    [InlineData("<t><d>one hour</d><y>P1Y</y><s>2026-09-29T10:00:00Z</s></t>", false)]
    public void Values_still_validate_against_the_base_type(string instance, bool valid)
    {
        var schemas = new XsdSchemaProvider();
        schemas.AddFromString("", Xsd);
        var act = () => schemas.ValidateXml(instance, ValidationMode.Strict);
        if (valid) act.Should().NotThrow();
        else act.Should().Throw<SchemaValidationException>();
    }
}
