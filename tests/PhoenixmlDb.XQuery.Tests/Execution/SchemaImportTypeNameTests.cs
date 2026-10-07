using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// Type names that reach an imported schema's types through a namespace the prolog binds without
/// a <c>declare namespace</c>: the default element/type namespace of
/// <c>import schema default element namespace</c> (XQuery 3.1 §2.5.5.3: an unprefixed type name
/// takes the default element/type namespace), and the prefix of a library module's own
/// <c>module namespace</c> declaration. The first was XPST0051 "use xs:…", the second XPST0081.
/// </summary>
public sealed class SchemaImportTypeNameTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "phx-sitn-" + Guid.NewGuid().ToString("N"));

    public SchemaImportTypeNameTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private const string Schema = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" targetNamespace="urn:h" xmlns="urn:h">
          <xs:simpleType name="size">
            <xs:restriction base="xs:integer"><xs:minInclusive value="1"/><xs:maxInclusive value="9"/></xs:restriction>
          </xs:simpleType>
        </xs:schema>
        """;

    private const string Module = """
        module namespace h = "urn:h";
        import schema "urn:h";
        declare function h:fits($x as xs:integer) as xs:boolean { $x castable as h:size };
        declare function h:sized($x as h:size) { $x };
        """;

    private async Task<string> RunAsync(string query)
    {
        var path = Path.Combine(_dir, "h.xqm");
        await File.WriteAllTextAsync(path, Module);
        var provider = new XsdSchemaProvider();
        provider.AddFromString("urn:h", Schema);
        var engine = new QueryEngine(schemaProvider: provider);
        QueryCompilationResult compiled;
        try
        {
            compiled = engine.Compile(query, new CompilationOptions
            {
                ExternalModules = new Dictionary<string, List<string>> { ["urn:h"] = [path] },
            });
        }
        catch (PhoenixmlDb.XQuery.Parser.XQueryParseException ex)
        {
            return ex.Message;
        }
        if (!compiled.Success)
            return string.Join("; ", compiled.Errors.Select(e => $"{e.Code}: {e.Message}"));
        var results = new List<object?>();
        try
        {
            await foreach (var item in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
                results.Add(item);
        }
        catch (XQueryRuntimeException ex)
        {
            return ex.ErrorCode;
        }
        catch (SchemaException ex)
        {
            return ex.Message;
        }
        return string.Join(",", results);
    }

    [Theory]
    [InlineData("8 cast as size", "8")]
    [InlineData("8 castable as size", "True")]
    [InlineData("12 castable as size", "False")]
    [InlineData("12 cast as size", "FORG0001")]
    public async Task An_unprefixed_type_name_takes_the_imported_default_element_namespace(string body, string expected) =>
        (await RunAsync("import schema default element namespace 'urn:h'; " + body)).Should().Be(expected);

    [Fact]
    public async Task An_unprefixed_type_name_the_schema_does_not_declare_is_an_error() =>
        (await RunAsync("import schema default element namespace 'urn:h'; 8 cast as nosuch"))
            .Should().Contain("{urn:h}nosuch");

    [Fact]
    public async Task Without_a_default_namespace_an_unprefixed_type_name_is_still_XPST0051() =>
        (await RunAsync("import schema namespace h = 'urn:h'; 8 cast as size")).Should().StartWith("XPST0051");

    [Theory]
    [InlineData("h:fits(8)", "True")]
    [InlineData("h:fits(12)", "False")]
    public async Task A_library_module_names_schema_types_by_its_own_module_prefix(string call, string expected) =>
        (await RunAsync("declare namespace h = 'urn:h'; import module 'urn:h'; " + call)).Should().Be(expected);
}
