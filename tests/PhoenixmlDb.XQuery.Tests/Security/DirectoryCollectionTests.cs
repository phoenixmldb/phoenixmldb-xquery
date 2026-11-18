using System.Globalization;
using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.Security;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Security;

/// <summary>
/// fn:collection and fn:uri-collection on a local directory, with Saxon's query parameters
/// (select, match, recurse, on-error, content-type). A JSON file gives its map or array, as
/// json-doc() does. Under a resource policy the directory is authorised before anything is listed,
/// every file is judged on its own, and nothing leads out of the directory through ".." or a link.
/// </summary>
public sealed class DirectoryCollectionTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("phx-dircoll").FullName;
    private readonly string _data;
    private readonly string _outside;

    public DirectoryCollectionTests()
    {
        _data = Directory.CreateDirectory(Path.Combine(_root, "data")).FullName;
        _outside = Directory.CreateDirectory(Path.Combine(_root, "outside")).FullName;
        var sub = Directory.CreateDirectory(Path.Combine(_data, "sub")).FullName;
        File.WriteAllText(Path.Combine(_data, "a.json"), """{"items":[{"name":"alpha"}]}""");
        File.WriteAllText(Path.Combine(_data, "b.json"), """{"items":[{"name":"beta"},{"name":"gamma"}]}""");
        File.WriteAllText(Path.Combine(_data, "c.xml"), "<c>xml-content</c>");
        File.WriteAllText(Path.Combine(_data, "notes.txt"), "text-content");
        File.WriteAllText(Path.Combine(sub, "d.json"), """[1, 2, 3]""");
        File.WriteAllText(Path.Combine(_outside, "secret.json"), """{"secret":"outside-content"}""");
        File.WriteAllText(Path.Combine(_outside, "secret.xml"), "<s>outside-content</s>");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static string Uri(string path) => new Uri(path).AbsoluteUri;

    private string DataUri => Uri(_data);

    private static async Task<string> Eval(string query, ResourcePolicy? policy = null, Uri? baseUri = null)
    {
        try
        {
            return await new XQueryFacade { ResourcePolicy = policy }.EvaluateAsync(query, queryBaseUri: baseUri);
        }
#pragma warning disable CA1031 // the failure is the outcome under test
        catch (Exception e)
#pragma warning restore CA1031
        {
            return "error: " + e.Message;
        }
    }

    private static string Names(string collectionUri)
        => $"string-join(uri-collection('{collectionUri}') ! tokenize(string(.), '/')[last()], ' ')";

    private ResourcePolicy DataOnly(int maxDocumentLoads = 0)
    {
        var builder = ResourcePolicy.CreateBuilder().AllowReadFrom("file", pathPrefix: _data);
        return (maxDocumentLoads > 0 ? builder.WithMaxDocumentLoads(maxDocumentLoads) : builder).Build();
    }

    // ── what the parameters select ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("", "a.json b.json c.xml notes.txt")]
    [InlineData("?select=*.json", "a.json b.json")]
    [InlineData("?select=*.json;recurse=yes", "a.json b.json d.json")]
    [InlineData("?select=*.json&amp;recurse=yes", "a.json b.json d.json")]
    [InlineData("?match=[ab]%5C.json", "a.json b.json")]
    [InlineData("?match=json", "")]
    [InlineData("?select=?.xml", "c.xml")]
    public async Task The_parameters_select_the_files(string query, string expected)
        => (await Eval(Names(DataUri + query))).Should().Be(expected);

    [Fact]
    public async Task A_json_file_gives_its_map_as_json_doc_does()
        => (await Eval($"string-join(for $x in collection('{DataUri}?select=*.json') return $x?items?*?name, ' ')"))
            .Should().Be("alpha beta gamma");

    [Fact]
    public async Task An_xml_file_gives_its_document_and_a_text_file_its_text()
        => (await Eval($"string-join((string(collection('{DataUri}?select=c.xml') instance of document-node()), string(collection('{DataUri}?select=c.xml')), collection('{DataUri}?select=notes.txt')), ' ')"))
            .Should().Be("true xml-content text-content");

    [Fact]
    public async Task Content_type_overrides_the_extension()
        => (await Eval($"collection('{DataUri}?select=a.json;content-type=text/plain') instance of xs:string"))
            .Should().Be("true");

    [Fact]
    public async Task A_relative_directory_resolves_against_the_static_base_uri()
        => (await Eval(Names("data?select=*.json"), baseUri: new Uri(Uri(_root) + "/")))
            .Should().Be("a.json b.json");

    [Fact]
    public async Task A_relative_directory_with_no_base_uri_is_an_error()
        => (await Eval("collection('data?select=*.json')")).Should().Contain("no base URI");

    [Fact]
    public async Task A_collection_the_host_registered_wins_over_the_directory()
    {
        var store = new XdmDocumentStore();
        store.RegisterCollection(DataUri, ["registered"]);
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store);
        var items = new List<object?>();
        await foreach (var item in engine.Compile($"collection('{DataUri}')").ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
            items.Add(item);

        items.Should().Equal("registered");
    }

    private sealed class HostResolver(XdmDocumentStore store) : IDocumentResolver
    {
        public List<string?> Asked { get; } = [];
        public Xdm.Nodes.XdmDocument? ResolveDocument(string uri) => null;
        public bool IsDocumentAvailable(string uri) => false;
        public IEnumerable<Xdm.Nodes.XdmNode> ResolveCollection(string? uri)
        {
            Asked.Add(uri);
            return [store.LoadFromString("<host/>", "urn:host")];
        }
    }

    [Fact]
    public async Task A_host_with_its_own_resolver_keeps_deciding_what_a_directory_uri_means()
    {
        // The XSLT engine, or a database, resolves documents itself: the engine does not list
        // the directory in its place.
        var store = new XdmDocumentStore();
        var host = new HostResolver(store);
        var engine = new QueryEngine(nodeProvider: store, documentResolver: host);
        var items = new List<object?>();
        await foreach (var item in engine.Compile($"collection('{DataUri}?select=*.json') ! name(*)").ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
            items.Add(item);

        items.Should().Equal("host");
        host.Asked.Should().Equal(DataUri + "?select=*.json");
    }

    // ── on-error ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("", "error")]
    [InlineData(";on-error=fail", "error")]
    [InlineData(";on-error=ignore", "2")]
    [InlineData(";on-error=warning", "2")]
    public async Task On_error_decides_what_a_file_that_does_not_parse_does(string onError, string expected)
    {
        await File.WriteAllTextAsync(Path.Combine(_data, "broken.json"), "{broken");
        await File.WriteAllTextAsync(Path.Combine(_data, "broken.xml"), "<broken");

        var json = await Eval($"count(collection('{DataUri}?select=*.json{onError}'))");
        var xml = await Eval($"count(collection('{DataUri}?select=*.xml{onError}')) + 1");

        (expected == "error" ? json.StartsWith("error", StringComparison.Ordinal) : json == expected).Should().BeTrue(json);
        (expected == "error" ? xml.StartsWith("error", StringComparison.Ordinal) : xml == expected).Should().BeTrue(xml);
    }

    [Fact]
    public async Task On_error_does_not_hide_an_exhausted_load_budget()
    {
        await File.WriteAllTextAsync(Path.Combine(_data, "e.xml"), "<e/>");

        var result = await Eval($"count(collection('{DataUri}?select=*.xml;on-error=ignore'))", DataOnly(maxDocumentLoads: 1));

        result.Should().StartWith("error").And.Contain("budget");
    }

    // ── the db-engine conditions ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("collection")]
    [InlineData("uri-collection")]
    public async Task ServerDefault_refuses_before_anything_is_listed(string function)
    {
        // A directory that does not exist: the refusal, not "not a directory", shows that the
        // policy was asked before the file system was.
        var missing = Uri(Path.Combine(_root, "no-such-dir"));

        (await Eval($"{function}('{missing}?select=*.json')", ResourcePolicy.ServerDefault))
            .Should().StartWith("error").And.NotContain("not a directory");
        (await Eval($"string-join({function}('{DataUri}?select=*') ! string(.), ' ')", ResourcePolicy.ServerDefault))
            .Should().StartWith("error").And.NotContain("a.json").And.NotContain("alpha");
    }

    [Fact]
    public async Task A_policy_that_does_not_allow_the_directory_refuses_it()
    {
        var otherOnly = ResourcePolicy.CreateBuilder().AllowReadFrom("file", pathPrefix: _outside).Build();

        (await Eval(Names(DataUri + "?select=*"), otherOnly)).Should().StartWith("error").And.NotContain("a.json");
    }

    [Fact]
    public async Task A_policy_that_allows_the_directory_lists_it()
        => (await Eval($"string-join(for $x in collection('{DataUri}?select=*.json') return $x?items?*?name, ' ')", DataOnly()))
            .Should().Be("alpha beta gamma");

    [Fact]
    public async Task Dot_dot_does_not_lead_out_of_the_allowed_directory()
    {
        var escape = DataUri + "/../outside?select=*";

        (await Eval($"string-join(collection('{escape}') ! string(.), ' ')", DataOnly()))
            .Should().StartWith("error").And.NotContain("outside-content");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_linked_file_that_leads_out_of_the_directory_is_left_out(bool underPolicy)
    {
        File.CreateSymbolicLink(Path.Combine(_data, "link.json"), Path.Combine(_outside, "secret.json"));

        var result = await Eval($"string-join(collection('{DataUri}?select=*.json') ! serialize(., map{{'method':'json'}}), ' ')",
            underPolicy ? DataOnly() : null);

        result.Should().NotContain("outside-content").And.Contain("alpha");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_linked_directory_is_not_descended_into(bool underPolicy)
    {
        Directory.CreateSymbolicLink(Path.Combine(_data, "linked"), _outside);

        var result = await Eval(Names(DataUri + "?select=*;recurse=yes"), underPolicy ? DataOnly() : null);

        result.Should().NotContain("secret").And.Contain("d.json");
    }

    [Fact]
    public async Task A_link_back_into_the_directory_does_not_recurse_for_ever()
    {
        Directory.CreateSymbolicLink(Path.Combine(_data, "sub", "loop"), _data);

        (await Eval(Names(DataUri + "?select=*.json;recurse=yes"))).Should().Be("a.json b.json d.json");
    }

    [Fact]
    public async Task A_linked_file_that_stays_inside_the_directory_is_read()
    {
        File.CreateSymbolicLink(Path.Combine(_data, "again.json"), Path.Combine(_data, "a.json"));

        (await Eval($"count(collection('{DataUri}?select=*.json'))", DataOnly())).Should().Be("3");
    }

    private sealed class OnlySource : ResourceResolverBase
    {
        public override bool SuppliesAllContent => true;
    }

    [Theory]
    [InlineData("collection")]
    [InlineData("uri-collection")]
    public async Task A_resolver_that_supplies_all_content_means_no_directory_is_listed(string function)
    {
        var policy = ResourcePolicy.CreateBuilder().AllowReadFrom("file", pathPrefix: _data)
            .WithResourceResolver(new OnlySource()).Build();

        var result = await Eval(string.Format(CultureInfo.InvariantCulture,
            "try {{ string-join({0}('{1}?select=*') ! string(.), ' ') }} catch * {{ 'refused' }}", function, DataUri), policy);

        result.Should().NotContain("a.json").And.NotContain("alpha");
    }
}
