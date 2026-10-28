using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// A decimal-format declaration is local to its module (XQuery 3.1 §4.10). A library function
/// naming its module's format got the importing module's format of the same name instead
/// (QT3 decimal-format-21).
/// </summary>
public sealed class ModuleDecimalFormatTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("phoenixmldb-dfd-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private async Task<string> RunAsync(string query)
    {
        var path = Path.Combine(_dir, "m.xqm");
        await File.WriteAllTextAsync(path, """
            module namespace m = "urn:m";
            declare decimal-format df001 grouping-separator="'";
            declare function m:do() as xs:string { format-number(123456.789, "#'###'###.###", 'df001') };
            """);
        var engine = new QueryEngine(nodeProvider: new XdmDocumentStore());
        var compiled = engine.Compile(query, new CompilationOptions
        {
            ExternalModules = new Dictionary<string, List<string>> { ["urn:m"] = [path] },
        });
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors));
        var results = new List<string>();
        await foreach (var item in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
            results.Add(item?.ToString() ?? "");
        return string.Join(" ", results);
    }

    [Fact]
    public async Task The_module_format_shadows_the_importer_format_of_the_same_name()
        => (await RunAsync("""
            import module namespace m = "urn:m";
            declare decimal-format df001 grouping-separator="!";
            format-number(123456.789, '#!###!###.###', 'df001') || '-' || m:do()
            """)).Should().Be("123!456.789-123'456.789");

    [Fact]
    public async Task The_module_format_is_found_when_the_importer_has_none_of_that_name()
        => (await RunAsync("import module namespace m = \"urn:m\"; m:do()")).Should().Be("123'456.789");
}
