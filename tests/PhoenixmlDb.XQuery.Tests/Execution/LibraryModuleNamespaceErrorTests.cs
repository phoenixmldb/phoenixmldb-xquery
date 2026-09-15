using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// Importing a library module raises the static errors XQuery 3.1 §4.2 requires: XQST0048 when a function (not only a
/// variable) is declared outside the module's target namespace, and XQST0046 when the module namespace is not a valid
/// URI. Both compiled and ran (xquery#63; QT3 misc/CombinedErrorCodes XQST0048, XQST0046_02). The last test is a guard.
/// </summary>
public sealed class LibraryModuleNamespaceErrorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "xq-libns-" + Guid.NewGuid().ToString("N"));

    public LibraryModuleNamespaceErrorTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
    }

    private QueryCompilationResult CompileImport(string moduleText, string moduleUri)
    {
        var path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".xq").Replace("\\", "/");
        File.WriteAllText(path, moduleText);
        var store = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store);
        return engine.Compile($"import module namespace m = \"{moduleUri}\" at \"{path}\"; 1");
    }

    private static string Describe(QueryCompilationResult r) => string.Join("; ", r.Errors.Select(e => $"{e.Code} {e.Message}"));

    [Fact]
    public void A_function_outside_the_target_namespace_is_XQST0048()
    {
        var compiled = CompileImport("""
            module namespace foo = "http://www.example.org/foo";
            declare namespace bar = "http://www.example.org/bar";
            declare function bar:foo() { 1 };
            """, "http://www.example.org/foo");
        compiled.Errors.Should().Contain(e => e.Code == "XQST0048", Describe(compiled));
    }

    [Fact]
    public void A_module_namespace_with_a_malformed_percent_escape_is_XQST0046()
    {
        var compiled = CompileImport("""module namespace test = "%gg";""", "%gg");
        compiled.Errors.Should().Contain(e => e.Code == "XQST0046", Describe(compiled));
    }

    [Fact]
    public void A_module_whose_declarations_are_in_its_namespace_imports()
    {
        var compiled = CompileImport("""
            module namespace foo = "http://www.example.org/foo";
            declare variable $foo:v := 1;
            declare function foo:f() { 2 };
            """, "http://www.example.org/foo");
        compiled.Success.Should().BeTrue(Describe(compiled));
    }
}
