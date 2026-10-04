using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// Element construction and element names in library modules, imported with
/// <c>import module</c> or loaded with <c>fn:load-xquery-module</c>.
/// </summary>
/// <remarks>
/// Three defects, all found from QT3 fn-load-xquery-module:
/// <list type="bullet">
/// <item>load-xquery-module ran the module in an engine with no node store, so any module
/// function with an element constructor failed ("requires a node store implementing
/// INodeBuilder").</item>
/// <item><c>import schema default element namespace "u"</c> never made u the default element
/// namespace, in a main query or a module.</item>
/// <item>A library module's element names were resolved in the IMPORTING query's context, so
/// its own default element namespace never applied: <c>&lt;abf/&gt;</c> in its functions, and
/// unprefixed name tests, were in no namespace.</item>
/// </list>
/// </remarks>
public sealed class ModuleElementNamespaceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "phx-men-" + Guid.NewGuid().ToString("N"));

    public ModuleElementNamespaceTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private const string Schema = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" targetNamespace="urn:abf"
                   elementFormDefault="qualified">
          <xs:element name="abf"><xs:complexType><xs:sequence>
            <xs:element name="a" minOccurs="0"/>
          </xs:sequence></xs:complexType></xs:element>
        </xs:schema>
        """;

    private async Task<string> RunAsync(string module, string query)
    {
        var modulePath = Path.Combine(_dir, "m.xqm");
        var schemaPath = Path.Combine(_dir, "abf.xsd");
        await File.WriteAllTextAsync(schemaPath, Schema);
        await File.WriteAllTextAsync(modulePath, module.Replace("{xsd}", schemaPath, StringComparison.Ordinal));
        var store = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store);
        var compiled = engine.Compile(query.Replace("{xsd}", schemaPath, StringComparison.Ordinal), new CompilationOptions
        {
            ExternalModules = new Dictionary<string, List<string>> { ["urn:m"] = [modulePath] },
        });
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors));
        var results = new List<object?>();
        await foreach (var item in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
            results.Add(item);
        return string.Join(" ", results);
    }

    private const string DefaultNsModule = """
        module namespace m = "urn:m";
        declare default element namespace "urn:dflt";
        declare function m:make() { <e><c/></e> };
        declare function m:count($n) { count($n/c) };
        """;

    [Fact]
    public async Task ImportedModule_BuildsItsElementsInItsDefaultNamespace()
        => (await RunAsync(DefaultNsModule,
                "import module namespace m = 'urn:m'; namespace-uri(m:make()), namespace-uri(m:make()/*)"))
            .Should().Be("urn:dflt urn:dflt");

    [Fact]
    public async Task ImportedModule_NameTestsUseItsDefaultNamespace()
        => (await RunAsync(DefaultNsModule, "import module namespace m = 'urn:m'; m:count(m:make())"))
            .Should().Be("1");

    [Fact]
    public async Task ImportingQuerysDefaultNamespace_DoesNotReplaceTheModules()
        => (await RunAsync(DefaultNsModule,
                "declare default element namespace 'urn:main'; import module namespace m = 'urn:m'; namespace-uri(m:make())"))
            .Should().Be("urn:dflt");

    [Fact]
    public async Task ImportingQuerysDefaultNamespace_DoesNotReplaceTheModulesNameTests()
        => (await RunAsync(DefaultNsModule,
                "declare default element namespace 'urn:main'; import module namespace m = 'urn:m'; m:count(m:make())"))
            .Should().Be("1");

    [Fact]
    public async Task LoadedModule_ConstructsElements()
        => (await RunAsync(DefaultNsModule,
                "namespace-uri(load-xquery-module('urn:m')('functions')(QName('urn:m', 'make'))(0)())"))
            .Should().Be("urn:dflt");

    [Fact]
    public async Task SchemaImportDefaultElementNamespace_AppliesInAMainQuery()
        => (await RunAsync("module namespace m = 'urn:m';",
                "import schema default element namespace 'urn:abf' at '{xsd}'; namespace-uri(<abf/>)"))
            .Should().Be("urn:abf");

    [Fact]
    public async Task SchemaImportDefaultElementNamespace_AppliesInAModule()
        => (await RunAsync("""
                module namespace m = "urn:m";
                import schema default element namespace "urn:abf" at "{xsd}";
                declare function m:make() { validate strict { <abf><a/></abf> } };
                """,
                "import module namespace m = 'urn:m'; namespace-uri(m:make()), namespace-uri(m:make()/*)"))
            .Should().Be("urn:abf urn:abf");
}
