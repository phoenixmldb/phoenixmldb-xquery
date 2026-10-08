using FluentAssertions;
using PhoenixmlDb.Core.Schema;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Schema;

/// <summary>
/// <see cref="XsdSchemaProvider"/> reads its schema documents through the shared schema layer
/// (PhoenixmlDb.Core.Schema): the whole closure first, each document once, and a document that
/// cannot be read fails the load.
/// </summary>
public sealed class SchemaLayerLoadingTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "phx-layer-" + Guid.NewGuid().ToString("N"));

    public SchemaLayerLoadingTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private string Write(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static string Schema(string body, string? targetNamespace = null) => $"""
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"{(targetNamespace is null ? "" : $" targetNamespace=\"{targetNamespace}\" xmlns=\"{targetNamespace}\" elementFormDefault=\"qualified\"")}>
          {body}
        </xs:schema>
        """;

    private const string Part = """<xs:simpleType name="small"><xs:restriction base="xs:integer"><xs:maxInclusive value="9"/></xs:restriction></xs:simpleType>""";

    private static bool IsValid(XsdSchemaProvider schemas, string xml)
    {
        try
        {
            schemas.ValidateXml(xml, ValidationMode.Strict);
            return true;
        }
        catch (SchemaValidationException)
        {
            return false;
        }
    }

    // ── a referenced document that cannot be read ──

    [Fact]
    public void An_include_that_cannot_be_read_fails_the_load()
    {
        // System.Xml reports this as a warning and compiles what is left: the element below
        // loaded and everything in the missing document was silently not part of the schema.
        var main = Write("main.xsd", Schema("""<xs:include schemaLocation="missing.xsd"/><xs:element name="n" type="xs:integer"/>"""));
        var schemas = new XsdSchemaProvider();
        var act = () => schemas.ImportSchema("", [main]);
        act.Should().Throw<SchemaException>().Where(e => e.ErrorCode == "XQST0059")
            .WithMessage("*missing.xsd*");
        schemas.HasElementDeclaration("", "n").Should().BeFalse("nothing of a schema that failed to load is kept");
    }

    [Fact]
    public void A_failed_load_leaves_the_provider_usable()
    {
        var bad = Write("bad.xsd", Schema("""<xs:include schemaLocation="missing.xsd"/><xs:element name="n" type="xs:integer"/>"""));
        var good = Write("good.xsd", Schema("""<xs:element name="m" type="xs:integer"/>""", "urn:good"));
        var schemas = new XsdSchemaProvider();
        ((Action)(() => schemas.ImportSchema("", [bad]))).Should().Throw<SchemaException>();
        schemas.ImportSchema("urn:good", [good]);
        IsValid(schemas, """<m xmlns="urn:good">1</m>""").Should().BeTrue();
    }

    [Fact]
    public void A_second_hint_is_tried_when_the_first_fails()
    {
        var good = Write("good.xsd", Schema("""<xs:element name="n" type="xs:integer"/>"""));
        var schemas = new XsdSchemaProvider();
        schemas.ImportSchema("", [Path.Combine(_dir, "absent.xsd"), good]);
        IsValid(schemas, "<n>1</n>").Should().BeTrue();
    }

    [Fact]
    public void An_included_document_with_a_document_type_declaration_is_refused()
    {
        Write("part.xsd", "<!DOCTYPE xs:schema [<!ENTITY e 'x'>]>\n" + Schema(Part));
        var main = Write("main.xsd", Schema("""<xs:include schemaLocation="part.xsd"/><xs:element name="n" type="small"/>"""));
        var schemas = new XsdSchemaProvider();
        var act = () => schemas.ImportSchema("", [main]);
        act.Should().Throw<SchemaException>().WithMessage("*document type declaration*");
    }

    // ── two schemas that share a document ──

    [Fact]
    public void Two_imports_that_include_the_same_document_both_load()
    {
        Write("part.xsd", Schema(Part));
        var a = Write("a.xsd", Schema("""<xs:include schemaLocation="part.xsd"/><xs:element name="a" type="small"/>"""));
        var b = Write("b.xsd", Schema("""<xs:include schemaLocation="part.xsd"/><xs:element name="b" type="small"/>"""));
        var schemas = new XsdSchemaProvider();
        schemas.Add(a);
        schemas.Add(b);
        IsValid(schemas, "<a>1</a>").Should().BeTrue();
        IsValid(schemas, "<b>1</b>").Should().BeTrue();
        IsValid(schemas, "<b>10</b>").Should().BeFalse();
    }

    // ── schema text ──

    [Fact]
    public void Text_with_a_base_uri_resolves_what_it_includes_against_it()
    {
        Write("part.xsd", Schema(Part));
        var schemas = new XsdSchemaProvider();
        schemas.AddFromString("", Schema("""<xs:include schemaLocation="part.xsd"/><xs:element name="n" type="small"/>"""),
            new Uri(Path.Combine(_dir, "stylesheet.xsl")));
        IsValid(schemas, "<n>1</n>").Should().BeTrue();
        IsValid(schemas, "<n>10</n>").Should().BeFalse();
    }

    [Fact]
    public void Text_without_a_base_uri_does_not_resolve_a_relative_location_against_the_current_directory()
    {
        // A file of that name in the current directory is not what the text meant.
        var name = "phx-layer-" + Guid.NewGuid().ToString("N") + ".xsd";
        var inCurrentDirectory = Path.Combine(Directory.GetCurrentDirectory(), name);
        File.WriteAllText(inCurrentDirectory, Schema(Part));
        try
        {
            var schemas = new XsdSchemaProvider();
            var act = () => schemas.AddFromString("", Schema($"""<xs:include schemaLocation="{name}"/><xs:element name="n" type="small"/>"""));
            act.Should().Throw<SchemaException>().Where(e => e.ErrorCode == "XQST0059");
        }
        finally
        {
            File.Delete(inCurrentDirectory);
        }
    }

    [Fact]
    public void Two_texts_from_the_same_base_uri_are_two_schemas()
    {
        var baseUri = new Uri(Path.Combine(_dir, "stylesheet.xsl"));
        var schemas = new XsdSchemaProvider();
        schemas.AddFromString("urn:a", Schema("""<xs:element name="a" type="xs:integer"/>""", "urn:a"), baseUri);
        schemas.AddFromString("urn:b", Schema("""<xs:element name="b" type="xs:integer"/>""", "urn:b"), baseUri);
        IsValid(schemas, """<a xmlns="urn:a">1</a>""").Should().BeTrue();
        IsValid(schemas, """<b xmlns="urn:b">1</b>""").Should().BeTrue();
        IsValid(schemas, """<b xmlns="urn:b">x</b>""").Should().BeFalse();
    }

    [Fact]
    public void The_interface_takes_schema_text()
    {
        ISchemaProvider schemas = new XsdSchemaProvider();
        schemas.AddSchemaText("urn:a", Schema("""<xs:element name="a" type="xs:integer"/>""", "urn:a"), null, null);
        schemas.HasElementDeclaration("urn:a", "a").Should().BeTrue();
    }

    [Fact]
    public void Text_that_declares_another_namespace_than_the_one_asked_for_is_refused()
    {
        var schemas = new XsdSchemaProvider();
        var act = () => schemas.AddFromString("urn:other", Schema("""<xs:element name="a" type="xs:integer"/>""", "urn:a"));
        act.Should().Throw<SchemaException>().Where(e => e.ErrorCode == "XQST0059");
    }

    // ── a catalog ──

    [Fact]
    public void A_catalog_says_where_an_included_document_is_read_from()
    {
        var part = Write("real-part.xsd", Schema(Part));
        var main = Write("main.xsd", Schema("""<xs:include schemaLocation="http://example.invalid/schemas/part.xsd"/><xs:element name="n" type="small"/>"""));
        var catalogPath = Write("catalog.xml", $"""
            <catalog xmlns="urn:oasis:names:tc:entity:xmlns:xml:catalog">
              <uri name="http://example.invalid/schemas/part.xsd" uri="{new Uri(part).AbsoluteUri}"/>
            </catalog>
            """);
        var schemas = new XsdSchemaProvider
        {
            Catalog = XmlCatalog.Load([new Uri(catalogPath)], SchemaAccessGate.LocalFiles(_dir)),
        };
        schemas.ImportSchema("", [main]);
        IsValid(schemas, "<n>1</n>").Should().BeTrue();
        IsValid(schemas, "<n>10</n>").Should().BeFalse();
    }

    // ── a provider on a compiled schema ──

    private static CompiledSchema Compiled(TimeSpan? patternLimit = null)
    {
        var uri = new Uri("urn:test:compiled.xsd");
        return SchemaCompiler.Compile([uri], SchemaAccessGate.SuppliedOnly,
            [SchemaSource.FromText(Schema("""
                <xs:simpleType name="p"><xs:restriction base="xs:string"><xs:pattern value="(a+)+b"/></xs:restriction></xs:simpleType>
                <xs:element name="n" type="xs:integer"/>
                <xs:element name="v" type="p"/>
                """, "urn:c"), uri)],
            new SchemaCompileOptions { PatternMatchTimeout = patternLimit });
    }

    [Fact]
    public void A_provider_on_a_compiled_schema_validates_against_it()
    {
        var schemas = new XsdSchemaProvider(Compiled());
        schemas.HasElementDeclaration("urn:c", "n").Should().BeTrue();
        IsValid(schemas, """<n xmlns="urn:c">1</n>""").Should().BeTrue();
        IsValid(schemas, """<n xmlns="urn:c">x</n>""").Should().BeFalse();
        schemas.TryCastToSchemaSimpleType("urn:c", "p", "aab").Should().BeTrue();
    }

    [Fact]
    public void Two_providers_share_one_compiled_schema()
    {
        var compiled = Compiled();
        var first = new XsdSchemaProvider(compiled);
        var second = new XsdSchemaProvider(compiled);
        IsValid(first, """<n xmlns="urn:c">1</n>""").Should().BeTrue();
        IsValid(second, """<n xmlns="urn:c">x</n>""").Should().BeFalse();
    }

    [Fact]
    public void An_import_of_a_namespace_the_compiled_schema_declares_is_satisfied_by_it()
    {
        var schemas = new XsdSchemaProvider(Compiled());
        var act = () => schemas.ImportSchema("urn:c", ["/nowhere/at-all.xsd"]);
        act.Should().NotThrow();
    }

    [Fact]
    public void Nothing_can_be_added_to_a_provider_on_a_compiled_schema()
    {
        var compiled = Compiled();
        var schemas = new XsdSchemaProvider(compiled);
        var other = Write("other.xsd", Schema("""<xs:element name="o" type="xs:integer"/>""", "urn:o"));

        ((Action)(() => schemas.ImportSchema("urn:o", [other]))).Should().Throw<SchemaException>().Where(e => e.ErrorCode == "XQST0059");
        ((Action)(() => schemas.Add(other))).Should().Throw<SchemaException>();
        ((Action)(() => schemas.AddFromString("urn:o", File.ReadAllText(other)))).Should().Throw<SchemaException>();
        compiled.SchemaSet.Count.Should().Be(1, "the shared set is as it was compiled");
        compiled.SchemaSet.IsCompiled.Should().BeTrue();
    }

    [Fact]
    public void A_compiled_schemas_pattern_limit_is_the_providers()
    {
        var limit = TimeSpan.FromMilliseconds(100);
        var schemas = new XsdSchemaProvider(Compiled(limit));
        schemas.PatternMatchTimeout.Should().Be(limit);
        var act = () => schemas.TryCastToSchemaSimpleType("urn:c", "p", new string('a', 40) + "!");
        act.Should().Throw<System.Text.RegularExpressions.RegexMatchTimeoutException>();
    }

    [Fact]
    public void A_compiled_schemas_pattern_limit_can_be_lowered_and_not_raised_or_removed()
    {
        var schemas = new XsdSchemaProvider(Compiled(TimeSpan.FromSeconds(5)));
        schemas.LimitPatternMatchTime(TimeSpan.FromMilliseconds(100));
        schemas.PatternMatchTimeout.Should().Be(TimeSpan.FromMilliseconds(100));
        // A longer limit asked for by a later query changes nothing.
        schemas.LimitPatternMatchTime(TimeSpan.FromSeconds(30));
        schemas.PatternMatchTimeout.Should().Be(TimeSpan.FromMilliseconds(100));

        ((Action)(() => schemas.PatternMatchTimeout = TimeSpan.FromSeconds(30))).Should().Throw<InvalidOperationException>();
        ((Action)(() => schemas.PatternMatchTimeout = null)).Should().Throw<InvalidOperationException>();

        var act = () => schemas.TryCastToSchemaSimpleType("urn:c", "p", new string('a', 40) + "!");
        act.Should().Throw<System.Text.RegularExpressions.RegexMatchTimeoutException>();
    }

    [Fact]
    public void A_query_limit_bounds_a_compiled_schema_that_had_none()
    {
        var schemas = new XsdSchemaProvider(Compiled());
        schemas.PatternMatchTimeout.Should().BeNull();
        schemas.LimitPatternMatchTime(TimeSpan.FromMilliseconds(100));
        var act = () => schemas.TryCastToSchemaSimpleType("urn:c", "p", new string('a', 40) + "!");
        act.Should().Throw<System.Text.RegularExpressions.RegexMatchTimeoutException>();
    }
}
