using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.Security;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// A relative URI with no base URI to resolve it against names nothing. fn:doc,
/// fn:doc-available and fn:json-doc resolved it against the process's working directory and
/// read the file found there, with or without a resource policy; fn:unparsed-text refused it.
/// Now each of them refuses it (the *-available functions answer false), and the error does
/// not name the working directory. What the host supplies under a relative name (a document
/// it loaded, its resolver's content, a registered mapping) is served as before.
/// </summary>
public sealed class RelativeUriWithoutBaseTests : IDisposable
{
    // Files of these names in the working directory: reading them is the defect.
    private readonly string _stem = "phx-nobase-" + Guid.NewGuid().ToString("N");
    private readonly string _cwd = Directory.GetCurrentDirectory();

    public RelativeUriWithoutBaseTests()
    {
        File.WriteAllText(Path.Combine(_cwd, _stem + ".xml"), "<r>7</r>");
        File.WriteAllText(Path.Combine(_cwd, _stem + ".txt"), "7");
        File.WriteAllText(Path.Combine(_cwd, _stem + ".json"), """{ "n": "7" }""");
    }

    public void Dispose()
    {
        foreach (var ext in new[] { ".xml", ".txt", ".json" })
        {
            try { File.Delete(Path.Combine(_cwd, _stem + ext)); } catch (IOException) { }
        }
    }

    private static ResourcePolicy? Policy(string kind) => kind switch
    {
        "none" => null,
        "allow-file" => ResourcePolicy.CreateBuilder().AllowReadFrom("file").Build(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private string Query(string expression) => expression.Replace("{0}", _stem, StringComparison.Ordinal);

    [Theory]
    [InlineData("none", "doc('{0}.xml')", "FODC0002")]
    [InlineData("none", "unparsed-text('{0}.txt')", "FOUT1170")]
    [InlineData("none", "unparsed-text-lines('{0}.txt')", "FOUT1170")]
    [InlineData("none", "json-doc('{0}.json')", "FOUT1170")]
    [InlineData("allow-file", "doc('{0}.xml')", "FODC0002")]
    [InlineData("allow-file", "unparsed-text('{0}.txt')", "FOUT1170")]
    [InlineData("allow-file", "unparsed-text-lines('{0}.txt')", "FOUT1170")]
    [InlineData("allow-file", "json-doc('{0}.json')", "FOUT1170")]
    public async Task The_function_refuses_it(string policy, string expression, string code)
    {
        var facade = new XQueryFacade { ResourcePolicy = Policy(policy) };

        var act = () => facade.EvaluateAsync(Query(expression));

        var error = (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which;
        error.ErrorCode.Should().Be(code);
        error.ToString().Should().NotContain(_cwd);
    }

    [Theory]
    [InlineData("none", "doc-available('{0}.xml')")]
    [InlineData("none", "unparsed-text-available('{0}.txt')")]
    [InlineData("allow-file", "doc-available('{0}.xml')")]
    [InlineData("allow-file", "unparsed-text-available('{0}.txt')")]
    public async Task The_availability_function_answers_false(string policy, string expression)
    {
        var facade = new XQueryFacade { ResourcePolicy = Policy(policy) };

        (await facade.EvaluateAsync("string(" + Query(expression) + ")")).Trim().Should().Be("false");
    }

    [Fact]
    public async Task With_a_base_uri_the_same_names_are_read()
    {
        var facade = new XQueryFacade();
        var baseUri = new Uri(Path.Combine(_cwd, "query.xq"));

        (await facade.EvaluateAsync(Query("string(doc('{0}.xml')) || json-doc('{0}.json')?n || unparsed-text('{0}.txt')"),
            queryBaseUri: baseUri)).Trim().Should().Be("777");
    }

    private static async Task<string> RunAsync(string query, XdmDocumentStore store, IDocumentResolver resolver,
        Dictionary<string, string>? mappings = null)
    {
        var engine = new QueryEngine(nodeProvider: store, documentResolver: resolver);
        var compilation = engine.Compile(query);
        compilation.Success.Should().BeTrue(string.Join("; ", compilation.Errors.Select(e => e.Message)));
        var context = engine.CreateContext();
        if (mappings != null)
            context.SetResourceMappings(mappings);
        var items = new List<string>();
        await foreach (var item in compilation.ExecutionPlan!.ExecuteAsync(context))
            items.Add(item?.ToString() ?? "");
        return string.Concat(items);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("allow-file")]
    public async Task A_document_the_host_loaded_under_the_name_is_served(string policy)
    {
        var store = new XdmDocumentStore();
        store.LoadFromString("<r>host</r>", "orders.xml");
        IDocumentResolver resolver = Policy(policy) is { } p ? new PolicyEnforcingResolver(store, p) : store;

        (await RunAsync("string(doc('orders.xml')) || doc-available('orders.xml')", store, resolver))
            .Should().Be("hosttrue");
    }

    private sealed class Supplier : ResourceResolverBase
    {
        public override ResourceContent? ResolveContent(ResourceRequest request) => request.Location switch
        {
            "orders.xml" => new ResourceContent("<r>supplied</r>", new Uri("urn:host:orders.xml")),
            "orders.json" => new ResourceContent("""{ "n": "supplied" }""", new Uri("urn:host:orders.json")),
            _ => null,
        };
    }

    [Fact]
    public async Task Content_the_host_resolver_supplies_under_the_name_is_served()
    {
        var store = new XdmDocumentStore();
        var resolver = new PolicyEnforcingResolver(store, ResourcePolicy.CreateBuilder().WithResourceResolver(new Supplier()).Build());

        (await RunAsync("string(doc('orders.xml')) || json-doc('orders.json')?n", store, resolver))
            .Should().Be("suppliedsupplied");
    }

    [Fact]
    public async Task A_name_the_host_mapped_to_a_file_is_read()
    {
        var store = new XdmDocumentStore();
        var mappings = new Dictionary<string, string>
        {
            ["orders.xml"] = Path.Combine(_cwd, _stem + ".xml"),
            ["orders.json"] = Path.Combine(_cwd, _stem + ".json"),
        };

        (await RunAsync("string(doc('orders.xml')) || json-doc('orders.json')?n", store, store, mappings))
            .Should().Be("77");
    }

    [Fact]
    public async Task A_relative_base_uri_declaration_is_not_made_absolute_against_the_working_directory()
    {
        var facade = new XQueryFacade();

        (await facade.EvaluateAsync("declare base-uri 'sub/'; string(static-base-uri())")).Trim().Should().Be("sub/");
        var act = () => facade.EvaluateAsync(Query("declare base-uri './'; doc('{0}.xml')"));
        (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be("FODC0002");
    }
}
