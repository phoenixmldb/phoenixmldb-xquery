using System.Diagnostics;
using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.Functions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Security;

/// <summary>
/// An XSD <c>pattern</c> facet is a regular expression System.Xml builds with no match timeout,
/// so <see cref="QueryExecutionLimits.RegexMatchTimeout"/> did not reach it: a cast to a schema
/// type, a <c>validate</c> expression and the compiling of a schema all ran a backtracking
/// pattern for as long as the value made it. Each case here takes about five seconds unbounded
/// and ends as an ordinary "not valid"; bounded, it stops at the limit with FOER0000.
/// </summary>
public sealed class PatternFacetLimitTests
{
    private const string Backtracking = "(a+)+b";
    private static readonly string Value = new('a', 27);
    private static readonly TimeSpan Limit = TimeSpan.FromMilliseconds(300);

    private const string Types = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" targetNamespace="urn:t" xmlns="urn:t"
                   elementFormDefault="qualified">
          <xs:simpleType name="slow"><xs:restriction base="xs:string"><xs:pattern value="(a+)+b"/></xs:restriction></xs:simpleType>
          <xs:simpleType name="derived"><xs:restriction base="slow"><xs:maxLength value="100"/></xs:restriction></xs:simpleType>
          <xs:simpleType name="slowList"><xs:list itemType="slow"/></xs:simpleType>
          <xs:simpleType name="anonymousItems">
            <xs:list><xs:simpleType><xs:restriction base="xs:string"><xs:pattern value="(a+)+b"/></xs:restriction></xs:simpleType></xs:list>
          </xs:simpleType>
          <xs:simpleType name="slowUnion"><xs:union memberTypes="xs:integer slow"/></xs:simpleType>
          <xs:simpleType name="anonymousMembers">
            <xs:union memberTypes="xs:integer">
              <xs:simpleType><xs:restriction base="xs:string"><xs:pattern value="(a+)+b"/></xs:restriction></xs:simpleType>
            </xs:union>
          </xs:simpleType>
          <xs:simpleType name="restrictedList"><xs:restriction base="slowList"><xs:maxLength value="5"/></xs:restriction></xs:simpleType>
          <xs:element name="named" type="slow"/>
          <xs:element name="anonymous">
            <xs:simpleType><xs:restriction base="xs:string"><xs:pattern value="(a+)+b"/></xs:restriction></xs:simpleType>
          </xs:element>
          <xs:element name="attributed">
            <xs:complexType>
              <xs:attribute name="at">
                <xs:simpleType><xs:restriction base="xs:string"><xs:pattern value="(a+)+b"/></xs:restriction></xs:simpleType>
              </xs:attribute>
            </xs:complexType>
          </xs:element>
          <xs:element name="simpleContent">
            <xs:complexType>
              <xs:simpleContent>
                <xs:restriction base="extended"><xs:pattern value="(a+)+b"/></xs:restriction>
              </xs:simpleContent>
            </xs:complexType>
          </xs:element>
          <xs:complexType name="extended">
            <xs:simpleContent><xs:extension base="xs:string"><xs:attribute name="x" type="xs:string"/></xs:extension></xs:simpleContent>
          </xs:complexType>
          <xs:element name="nested">
            <xs:complexType>
              <xs:sequence>
                <xs:element name="local">
                  <xs:simpleType><xs:restriction base="xs:string"><xs:pattern value="(a+)+b"/></xs:restriction></xs:simpleType>
                </xs:element>
              </xs:sequence>
            </xs:complexType>
          </xs:element>
          <xs:element name="plain" type="xs:string"/>
          <xs:element name="listed" type="restrictedList"/>
          <xs:element name="united" type="anonymousMembers"/>
          <xs:attribute name="global" type="slow"/>
          <xs:element name="open"><xs:complexType><xs:anyAttribute processContents="strict"/></xs:complexType></xs:element>
        </xs:schema>
        """;

    private static XsdSchemaProvider Provider()
    {
        var provider = new XsdSchemaProvider();
        provider.AddFromString("urn:t", Types);
        return provider;
    }

    /// <summary>Runs a query under the regex limit and returns what it threw, with how long it took.</summary>
    private static async Task<(Exception? Error, TimeSpan Elapsed)> RunAsync(string query, XsdSchemaProvider provider,
        CancellationToken token = default)
    {
        var store = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store, schemaProvider: provider);
        var compiled = engine.Compile("import schema namespace t = 'urn:t'; " + query);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors));
        var context = engine.CreateContext(limits: new QueryExecutionLimits { RegexMatchTimeout = Limit }, cancellationToken: token);
        var clock = Stopwatch.StartNew();
        try
        {
            await foreach (var _ in compiled.ExecutionPlan!.ExecuteAsync(context)) { }
            return (null, clock.Elapsed);
        }
        catch (Exception ex) when (ex is XQueryException or OperationCanceledException or System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            return (ex, clock.Elapsed);
        }
    }

    public static TheoryData<string> Casts => new()
    {
        "'{0}' cast as t:slow",
        "'{0}' castable as t:slow",
        "t:slow('{0}')",
        "'{0}' cast as t:derived",
        "'ab {0}' cast as t:slowList",
        "'ab {0}' cast as t:anonymousItems",
        "'{0}' cast as t:slowUnion",
        "'{0}' castable as t:anonymousMembers",
        "'ab {0}' castable as t:restrictedList",
    };

    [Theory]
    [MemberData(nameof(Casts))]
    public async Task A_cast_stops_at_the_regex_limit(string query)
    {
        var (error, _) = await RunAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture, query, Value), Provider());
        error.Should().BeAssignableTo<XQueryException>().Which.ErrorCode.Should().Be("FOER0000");
    }

    public static TheoryData<string> Documents => new()
    {
        "<t:named>{0}</t:named>",
        "<t:anonymous>{0}</t:anonymous>",
        "<t:attributed at='{0}'/>",
        "<t:simpleContent>{0}</t:simpleContent>",
        "<t:nested><t:local>{0}</t:local></t:nested>",
        "<t:listed>ab {0}</t:listed>",
        "<t:united>{0}</t:united>",
        "<t:open t:global='{0}'/>",
        "<t:plain xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance' xsi:type='t:slow'>{0}</t:plain>",
    };

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task Validation_stops_at_the_regex_limit(string document)
    {
        var xml = string.Format(System.Globalization.CultureInfo.InvariantCulture, document, Value);
        var (error, _) = await RunAsync($"validate strict {{ {xml} }}", Provider());
        error.Should().BeAssignableTo<XQueryException>().Which.ErrorCode.Should().Be("FOER0000");
    }

    [Fact]
    public async Task Lax_validation_stops_at_the_regex_limit()
    {
        var (error, _) = await RunAsync($"validate lax {{ <t:named>{Value}</t:named> }}", Provider());
        error.Should().BeAssignableTo<XQueryException>().Which.ErrorCode.Should().Be("FOER0000");
    }

    [Fact]
    public async Task A_cancelled_query_reports_cancellation_not_FOER0000()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var (error, _) = await RunAsync($"'{Value}' cast as t:slow", Provider(), cts.Token);
        error.Should().BeAssignableTo<OperationCanceledException>();
    }

    [Fact]
    public async Task A_value_the_pattern_accepts_is_still_accepted()
    {
        var (error, _) = await RunAsync("('aab' cast as t:slow, validate strict { <t:named>aab</t:named> })", Provider());
        error.Should().BeNull();
    }

    /// <summary>
    /// Compiling the set again rebuilds every type's patterns; the limit has to be put back on
    /// the ones a later schema did not touch.
    /// </summary>
    [Fact]
    public async Task The_limit_survives_a_later_schema()
    {
        var provider = Provider();
        (await RunAsync("'aab' cast as t:slow", provider)).Error.Should().BeNull();
        provider.AddFromString("urn:other", """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" targetNamespace="urn:other">
              <xs:element name="other" type="xs:string"/>
            </xs:schema>
            """);
        var (error, _) = await RunAsync($"'{Value}' cast as t:slow", provider);
        error.Should().BeAssignableTo<XQueryException>().Which.ErrorCode.Should().Be("FOER0000");
    }

    [Fact]
    public void The_provider_alone_stops_at_its_own_limit()
    {
        var provider = Provider();
        provider.PatternMatchTimeout = Limit;
        var cast = () => provider.TryCastToSchemaSimpleType("urn:t", "slow", Value);
        cast.Should().Throw<System.Text.RegularExpressions.RegexMatchTimeoutException>();
        var validate = () => provider.ValidateXml($"<named xmlns='urn:t'>{Value}</named>", ValidationMode.Strict);
        validate.Should().Throw<System.Text.RegularExpressions.RegexMatchTimeoutException>();
    }

    /// <summary>The engine only ever tightens the limit a host set.</summary>
    [Fact]
    public void A_looser_request_does_not_loosen_the_limit()
    {
        var provider = Provider();
        provider.PatternMatchTimeout = Limit;
        provider.LimitPatternMatchTime(TimeSpan.FromMinutes(1));
        provider.PatternMatchTimeout.Should().Be(Limit);
        provider.LimitPatternMatchTime(TimeSpan.FromMilliseconds(100));
        provider.PatternMatchTimeout.Should().Be(TimeSpan.FromMilliseconds(100));
    }

    public static TheoryData<string> SchemaSuppliedValues => new()
    {
        """<xs:simpleType name="e"><xs:restriction base="slow"><xs:enumeration value="{0}"/></xs:restriction></xs:simpleType>""",
        """<xs:element name="d" type="slow" default="{0}"/>""",
        """<xs:element name="f" type="slow" fixed="{0}"/>""",
        """<xs:attribute name="a" type="slow" default="{0}"/>""",
        """<xs:element name="l" type="slowList" default="ab {0}"/>""",
    };

    /// <summary>
    /// Compiling a schema matches its own enumeration, default and fixed values against its
    /// patterns, before any type exists to put a limit on.
    /// </summary>
    [Theory]
    [MemberData(nameof(SchemaSuppliedValues))]
    public void A_schema_whose_own_values_run_past_the_limit_is_refused(string declaration)
    {
        var xsd = $"""
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" targetNamespace="urn:c" xmlns="urn:c">
              <xs:simpleType name="slow"><xs:restriction base="xs:string"><xs:pattern value="{Backtracking}"/></xs:restriction></xs:simpleType>
              <xs:simpleType name="slowList"><xs:list itemType="slow"/></xs:simpleType>
              {string.Format(System.Globalization.CultureInfo.InvariantCulture, declaration, Value)}
            </xs:schema>
            """;
        var provider = new XsdSchemaProvider { PatternMatchTimeout = Limit };
        var clock = Stopwatch.StartNew();
        var add = () => provider.AddFromString("urn:c", xsd);
        add.Should().Throw<SchemaException>().Which.ErrorCode.Should().Be("XQST0059");
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3), "unbounded, compiling this schema takes about five seconds");

        // The refused schema is gone: the next load compiles without it.
        provider.AddFromString("urn:t", Types);
        provider.HasSchemaType("urn:c", "slow").Should().BeFalse();
        provider.HasSchemaType("urn:t", "slow").Should().BeTrue();
    }

    /// <summary>
    /// The compile-time check builds each pattern itself, and is only a bound on what System.Xml
    /// then does if it builds the same expression.
    /// </summary>
    [Theory]
    [InlineData(@"(a+)+b")]
    [InlineData(@"\i\c*")]
    [InlineData(@"\d{3}-\D\w\W")]
    [InlineData(@"[\i-[:]][\c-[:]]*")]
    [InlineData(@"a\\c|\\\d")]
    [InlineData(@"\I\C")]
    public void The_compile_time_check_builds_the_expression_System_Xml_builds(string pattern)
    {
        var set = new System.Xml.Schema.XmlSchemaSet();
        var xsd = $"""
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
              <xs:simpleType name="p"><xs:restriction base="xs:string"><xs:pattern value="{pattern}"/></xs:restriction></xs:simpleType>
            </xs:schema>
            """;
        using (var reader = System.Xml.XmlReader.Create(new StringReader(xsd)))
            set.Add(null, reader);
        set.Compile();
        var type = (System.Xml.Schema.XmlSchemaSimpleType)set.GlobalTypes[new System.Xml.XmlQualifiedName("p")]!;
        const System.Reflection.BindingFlags any = System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        var facets = type.Datatype!.GetType().GetProperty("Restriction", any)!.GetValue(type.Datatype)!;
        var patterns = (System.Collections.IList)facets.GetType().GetField("Patterns", any)!.GetValue(facets)!;

        XsdPatternGuard.ToNetPattern(pattern).Should().Be(patterns[0]!.ToString());
    }
}
