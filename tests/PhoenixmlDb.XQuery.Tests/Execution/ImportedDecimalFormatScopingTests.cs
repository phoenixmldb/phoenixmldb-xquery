using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// XQuery 3.1 §4.18 — decimal-format declared inside an imported library module
/// must remain visible to that module's own functions when invoked from the
/// importing module. Prior to this fix the library-module parser silently dropped
/// decimal-format declarations, so format-number(..., "lib:euro") inside a lib:*
/// function raised FODF1280.
/// </summary>
public sealed class ImportedDecimalFormatScopingTests
{
    [Fact]
    public async System.Threading.Tasks.Task LibraryDecimalFormat_VisibleToOwnFunctionFromImporter()
    {
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "xq-decfmt-" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tempDir);
        var libPath = System.IO.Path.Combine(tempDir, "lib-decfmt.xqm");
        await System.IO.File.WriteAllTextAsync(libPath,
            """
            xquery version "3.1";
            module namespace lib = "http://example.com/lib";

            declare decimal-format lib:euro decimal-separator = "," grouping-separator = ".";

            declare function lib:format($x as xs:double) as xs:string {
              format-number($x, "#.##0,00", "lib:euro")
            };
            """);

        var query = $$"""
            import module namespace lib = "http://example.com/lib" at "{{libPath.Replace("\\", "/")}}";
            lib:format(1234.5)
            """;

        var env = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: env, documentResolver: env);
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue(
            string.Join("; ", compiled.Errors.Select(e => e.Message)));

        using var ctx = engine.CreateContext();
        var items = new System.Collections.Generic.List<object?>();
        await foreach (var item in compiled.ExecutionPlan!.ExecuteAsync(ctx))
            items.Add(item);

        items.Should().ContainSingle().Which.Should().Be("1.234,50");

        try { System.IO.Directory.Delete(tempDir, recursive: true); } catch (System.IO.IOException) { }
    }
}
