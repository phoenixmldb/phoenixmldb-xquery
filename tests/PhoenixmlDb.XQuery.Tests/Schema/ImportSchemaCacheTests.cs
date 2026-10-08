using FluentAssertions;
using PhoenixmlDb.XQuery.Security;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Schema;

/// <summary>
/// A schema that <c>import schema</c> reads from files is compiled once and shared by every
/// provider that imports it, until one of its documents changes. Each query used to compile
/// its schemas again.
/// </summary>
public sealed class ImportSchemaCacheTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("phx-import-cache").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string Write(string name, string content, DateTime? time = null)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        if (time is { } t) File.SetLastWriteTimeUtc(path, t);
        return path;
    }

    private static string Schema(string body, string ns = "urn:s") => $"""
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" targetNamespace="{ns}" xmlns="{ns}" elementFormDefault="qualified">
          {body}
        </xs:schema>
        """;

    private const string Small = """<xs:simpleType name="small"><xs:restriction base="xs:integer"><xs:maxInclusive value="9"/></xs:restriction></xs:simpleType>""";

    private static XsdSchemaProvider Import(string path, string ns = "urn:s", ResourcePolicy? policy = null)
    {
        var provider = new XsdSchemaProvider();
        provider.ImportSchema(ns, [path], policy);
        return provider;
    }

    private static bool Accepts(XsdSchemaProvider provider, string value, string type = "small", string ns = "urn:s")
        => provider.TryCastToSchemaSimpleType(ns, type, value);

    [Fact]
    public void Two_providers_that_import_the_same_schema_share_one_compiled_set()
    {
        var main = Write("main.xsd", Schema("""<xs:include schemaLocation="part.xsd"/><xs:element name="n" type="small"/>"""));
        Write("part.xsd", Schema(Small));

        var first = Import(main);
        var second = Import(main);

        first.UsesSharedSet.Should().BeTrue();
        second.SchemaSetForTest.Should().BeSameAs(first.SchemaSetForTest);
        Accepts(second, "7").Should().BeTrue();
        Accepts(second, "70").Should().BeFalse();
    }

    [Fact]
    public void A_change_to_an_included_document_is_seen_by_the_next_import()
    {
        var old = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var main = Write("main.xsd", Schema("""<xs:include schemaLocation="part.xsd"/>"""), old);
        Write("part.xsd", Schema(Small), old);
        var before = Import(main);

        Write("part.xsd", Schema(Small.Replace("\"9\"", "\"99\"", StringComparison.Ordinal)));
        var after = Import(main);

        Accepts(before, "70").Should().BeFalse("a provider keeps the schema it imported");
        Accepts(after, "70").Should().BeTrue();
        after.SchemaSetForTest.Should().NotBeSameAs(before.SchemaSetForTest);
    }

    [Fact]
    public void A_policy_that_refuses_an_included_document_gets_nothing_from_the_cache()
    {
        var inside = Directory.CreateDirectory(Path.Combine(_dir, "inside")).FullName;
        var outside = Directory.CreateDirectory(Path.Combine(_dir, "outside")).FullName;
        File.WriteAllText(Path.Combine(outside, "part.xsd"), Schema(Small));
        var main = Path.Combine(inside, "main.xsd");
        File.WriteAllText(main, Schema($"""<xs:include schemaLocation="{new Uri(Path.Combine(outside, "part.xsd")).AbsoluteUri}"/>"""));

        // Compiled and kept for a caller with no policy, and for one whose policy allows both.
        Accepts(Import(main), "7").Should().BeTrue();
        var both = ResourcePolicy.CreateBuilder().AllowImportFrom("file", pathPrefix: _dir).Build();
        Accepts(Import(main, policy: both), "7").Should().BeTrue();

        var insideOnly = ResourcePolicy.CreateBuilder().AllowImportFrom("file", pathPrefix: inside).Build();
        var act = () => Import(main, policy: insideOnly);

        act.Should().Throw<SchemaException>().Where(e => e.ErrorCode == "XQST0059").WithMessage("*part.xsd*");
    }

    [Fact]
    public void A_second_import_adds_to_what_the_first_gave()
    {
        var s = Write("s.xsd", Schema(Small));
        var t = Write("t.xsd", Schema("""<xs:simpleType name="tiny"><xs:restriction base="xs:integer"><xs:maxInclusive value="3"/></xs:restriction></xs:simpleType>""", "urn:t"));

        var provider = Import(s);
        provider.ImportSchema("urn:t", [t]);
        var again = Import(s);
        again.ImportSchema("urn:t", [t]);

        Accepts(provider, "7").Should().BeTrue();
        Accepts(provider, "2", "tiny", "urn:t").Should().BeTrue();
        Accepts(provider, "7", "tiny", "urn:t").Should().BeFalse();
        again.SchemaSetForTest.Should().BeSameAs(provider.SchemaSetForTest);
    }

    [Fact]
    public void Schema_text_added_after_an_import_does_not_change_the_shared_set()
    {
        var s = Write("s.xsd", Schema(Small));
        var other = Import(s);
        var provider = Import(s);

        provider.AddFromString("urn:t", Schema("""<xs:simpleType name="tiny"><xs:restriction base="xs:integer"><xs:maxInclusive value="3"/></xs:restriction></xs:simpleType>""", "urn:t"));

        provider.UsesSharedSet.Should().BeFalse();
        Accepts(provider, "7").Should().BeTrue();
        Accepts(provider, "2", "tiny", "urn:t").Should().BeTrue();
        other.HasSchemaType("urn:t", "tiny").Should().BeFalse();
        Import(s).HasSchemaType("urn:t", "tiny").Should().BeFalse();
    }

    [Fact]
    public void A_pattern_time_limit_on_one_provider_is_not_put_on_another()
    {
        var s = Write("s.xsd", Schema("""<xs:simpleType name="code"><xs:restriction base="xs:string"><xs:pattern value="(a+)+b"/></xs:restriction></xs:simpleType>"""));
        var unlimited = Import(s);
        var limited = Import(s);

        limited.LimitPatternMatchTime(TimeSpan.FromMilliseconds(50));

        limited.PatternMatchTimeout.Should().Be(TimeSpan.FromMilliseconds(50));
        unlimited.PatternMatchTimeout.Should().BeNull();
        limited.SchemaSetForTest.Should().NotBeSameAs(unlimited.SchemaSetForTest);
        var act = () => Accepts(limited, new string('a', 40) + "c", "code");
        act.Should().Throw<System.Text.RegularExpressions.RegexMatchTimeoutException>();
        Accepts(unlimited, "aab", "code").Should().BeTrue();
    }

    [Fact]
    public void A_schema_that_does_not_load_fails_as_it_did()
    {
        var main = Write("main.xsd", Schema("""<xs:include schemaLocation="missing.xsd"/>"""));

        var act = () => Import(main);

        act.Should().Throw<SchemaException>().Where(e => e.ErrorCode == "XQST0059").WithMessage("*missing.xsd*");
    }

    [Fact]
    public async Task A_query_sees_a_schema_that_changed_since_the_query_before()
    {
        var old = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var main = Write("q.xsd", Schema(Small + """<xs:element name="n" type="small"/>"""), old);
        var query = $"import schema namespace s = 'urn:s' at '{new Uri(main).AbsoluteUri}'; '70' castable as s:small";

        (await new XQueryFacade().EvaluateAsync(query)).Trim().Should().Be("false");
        (await new XQueryFacade().EvaluateAsync(query)).Trim().Should().Be("false");
        Write("q.xsd", Schema(Small.Replace("\"9\"", "\"99\"", StringComparison.Ordinal) + """<xs:element name="n" type="small"/>"""));

        (await new XQueryFacade().EvaluateAsync(query)).Trim().Should().Be("true");
    }
}
