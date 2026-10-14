using AwesomeAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Schema;

/// <summary>
/// XML Schema allows a date with a negative year, or a year of more than four digits
/// (XSD 1.0 Part 2 §3.2.9.1). System.Xml reads a date into a DateTime, which has neither, and
/// reported <c>-0055-10-10</c> as an invalid xs:date, so a document with such a date did not
/// validate. 62 cases of the W3C XSLT suite have one in their source document.
/// </summary>
public class DatesSystemXmlCannotReadTests
{
    private const string Xsd = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
          <xs:element name="d" type="xs:date"/>
          <xs:element name="dt" type="xs:dateTime"/>
          <xs:element name="y" type="xs:gYear"/>
          <xs:element name="ym" type="xs:gYearMonth"/>
          <xs:element name="e"><xs:complexType><xs:attribute name="on" type="xs:date"/></xs:complexType></xs:element>
        </xs:schema>
        """;

    private static void Validate(string xml)
    {
        var schemas = new XsdSchemaProvider();
        schemas.AddFromString("", Xsd);
        schemas.ValidateXml(xml, ValidationMode.Strict);
    }

    [Theory]
    [InlineData("<d>-0055-10-10</d>")]
    [InlineData("<d>-0012-12-03-05:00</d>")]
    [InlineData("<d>12345-01-31Z</d>")]
    [InlineData("<dt>-0055-10-10T12:30:00</dt>")]
    [InlineData("<dt>-0055-10-10T12:30:00.5+01:00</dt>")]
    [InlineData("<y>-0055</y>")]
    [InlineData("<y>-0055Z</y>")]
    [InlineData("<ym>-0055-10</ym>")]
    [InlineData("<ym>20000-02+14:00</ym>")]
    [InlineData("<e on='-0055-10-10'/>")]
    public void A_date_with_a_year_outside_the_range_of_DateTime_is_valid(string xml)
        => FluentActions.Invoking(() => Validate(xml)).Should().NotThrow();

    [Theory]
    // not dates at all
    [InlineData("<d>2.1</d>")]
    [InlineData("<d>-55-10-10</d>")]
    [InlineData("<y>-55</y>")]
    // a month, a day, an hour or a timezone that does not exist
    [InlineData("<d>-0055-13-10</d>")]
    [InlineData("<d>-0055-04-31</d>")]
    [InlineData("<d>-0055-10-10+15:00</d>")]
    [InlineData("<dt>-0055-10-10T25:00:00</dt>")]
    [InlineData("<ym>-0055-00</ym>")]
    // year 0000 is not a year of XSD 1.0
    [InlineData("<d>0000-01-01</d>")]
    [InlineData("<d>-0000-01-01</d>")]
    // a year in range, refused for another reason
    [InlineData("<d>2024-02-30</d>")]
    [InlineData("<e on='2024-13-01'/>")]
    public void What_is_not_a_date_is_still_refused(string xml)
        => FluentActions.Invoking(() => Validate(xml)).Should().Throw<SchemaValidationException>();
}
