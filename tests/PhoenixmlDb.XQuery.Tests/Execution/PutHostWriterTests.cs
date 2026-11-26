using AwesomeAssertions;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.Security;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// <c>fn:put</c> through the host (the database engine's request R4): the policy's
/// <see cref="ResourcePolicy.ResourceResolver"/> is given each put when the list is applied
/// (<see cref="IResourceResolver.OpenResultDocument"/>), after the other updates and never while the
/// query runs; <c>null</c> from it means the library writes the file. Each write is authorised by
/// the policy when the list is applied, before any store is changed.
/// </summary>
public sealed class PutHostWriterTests : IDisposable
{
    private const string Doc = "urn:test:d";
    private const string HostUri = "phoenixml:container/c/d.xml";
    private readonly string _root = Directory.CreateTempSubdirectory("phx-put-host").FullName;
    private readonly string _allowed;

    public PutHostWriterTests()
    {
        _allowed = Directory.CreateDirectory(Path.Combine(_root, "allowed")).FullName;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    /// <summary>A host writer: it takes the URIs <see cref="Takes"/> accepts and keeps what it was given.</summary>
    private sealed class HostWriter : ResourceResolverBase
    {
        public List<string> Asked { get; } = [];
        public Dictionary<string, StringWriter> Written { get; } = [];
        public Func<string, bool> Takes { get; set; } = _ => true;

        public override TextWriter? OpenResultDocument(string href)
        {
            Asked.Add(href);
            if (!Takes(href))
                return null;
            var writer = new StringWriter();
            Written[href] = writer;
            return writer;
        }
    }

    private static (XdmDocumentStore Store, QueryEngine Engine) Setup(ResourcePolicy policy)
    {
        var store = new XdmDocumentStore();
        store.LoadFromString("<r><a>a</a></r>", Doc);
        return (store, new QueryEngine(nodeProvider: store, documentResolver: store) { ResourcePolicy = policy });
    }

    private static ExecutionPlan Compile(QueryEngine engine, string query)
    {
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors.Select(e => e.Message)));
        return compiled.ExecutionPlan!;
    }

    private static async Task<string> RunAsync(QueryEngine engine, string query)
    {
        var items = new List<object?>();
        await foreach (var item in engine.ExecuteAsync(Compile(engine, query)))
            items.Add(item);
        return string.Join(" ", items.Select(i => i?.ToString()));
    }

    private static Task<string> Names(QueryEngine engine) => RunAsync(engine, $"string-join(doc('{Doc}')/r/*!name(), ' ')");

    private static string Put(string uri) => $"(insert node <b/> into doc('{Doc}')/r, put(doc('{Doc}'), '{uri}'))";

    [Fact]
    public async Task The_host_writer_is_given_the_node_as_the_other_updates_left_it()
    {
        var host = new HostWriter();
        var (_, engine) = Setup(ResourcePolicy.CreateBuilder().AllowScheme("*").AllowWriteScheme("phoenixml").WithResourceResolver(host).Build());

        await RunAsync(engine, Put(HostUri));

        host.Asked.Should().Equal(HostUri);
        host.Written[HostUri].ToString().Should().Be("<r><a>a</a><b/></r>");
        (await Names(engine)).Should().Be("a b");
    }

    [Fact]
    public async Task The_host_writer_is_asked_only_when_the_list_is_applied()
    {
        var host = new HostWriter();
        var (_, engine) = Setup(ResourcePolicy.CreateBuilder().AllowScheme("*").AllowWriteScheme("phoenixml").WithResourceResolver(host).Build());
        using var context = engine.CreateContext();
        context.DeferPendingUpdates = true;

        await foreach (var _ in Compile(engine, Put(HostUri)).ExecuteAsync(context)) { }

        host.Asked.Should().BeEmpty();
        var put = context.PendingUpdates.Primitives.OfType<PutPrimitive>().Should().ContainSingle().Subject;
        put.Uri.AbsoluteUri.Should().Be(HostUri);
        put.Parameters.Should().BeNull();
        put.Method.Should().Be("xml");

        PendingUpdateApplicator.ApplyUpdates(context.PendingUpdates, context);

        host.Asked.Should().Equal(HostUri);
    }

    [Fact]
    public async Task When_the_host_does_not_take_it_the_library_writes_the_file()
    {
        var host = new HostWriter { Takes = _ => false };
        var (_, engine) = Setup(ResourcePolicy.CreateBuilder().AllowScheme("*").AllowWriteTo("file", null, _allowed).WithResourceResolver(host).Build());
        var file = Path.Combine(_allowed, "out.xml");

        await RunAsync(engine, Put(new Uri(file).AbsoluteUri));

        host.Asked.Should().ContainSingle();
        (await File.ReadAllTextAsync(file)).Should().Be("<r><a>a</a><b/></r>");
    }

    [Fact]
    public async Task With_a_host_resolver_the_policy_decides_when_the_list_is_applied_before_anything_changes()
    {
        // The policy allows reads only: fn:put is not refused when it is called (the host could take
        // it), and the write is refused when the list is applied, before the insert.
        var host = new HostWriter();
        var (_, engine) = Setup(ResourcePolicy.CreateBuilder().AllowScheme("*").WithResourceResolver(host).Build());
        using var context = engine.CreateContext();
        context.DeferPendingUpdates = true;

        await foreach (var _ in Compile(engine, Put(HostUri)).ExecuteAsync(context)) { }
        context.PendingUpdates.Primitives.OfType<PutPrimitive>().Should().ContainSingle();
        var act = () => PendingUpdateApplicator.ApplyUpdates(context.PendingUpdates, context);

        act.Should().Throw<XQueryRuntimeException>().Which.ErrorCode.Should().Be("XUDY9001");
        host.Asked.Should().BeEmpty();
        (await Names(engine)).Should().Be("a");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_policy_of_the_context_that_applies_the_list_authorises_the_write(bool noPolicy)
    {
        // Collected under a policy that allows the file, applied with a context whose policy does
        // not, or that has none.
        var file = Path.Combine(_allowed, "out.xml");
        var (store, engine) = Setup(ResourcePolicy.CreateBuilder().AllowScheme("*").AllowWriteTo("file", null, _allowed).Build());
        using var collecting = engine.CreateContext();
        collecting.DeferPendingUpdates = true;
        await foreach (var _ in Compile(engine, Put(new Uri(file).AbsoluteUri)).ExecuteAsync(collecting)) { }
        var readOnly = new QueryEngine(nodeProvider: store, documentResolver: store)
        {
            ResourcePolicy = noPolicy ? null : ResourcePolicy.CreateBuilder().AllowScheme("*").Build(),
        };
        using var applying = readOnly.CreateContext();

        var act = () => PendingUpdateApplicator.ApplyUpdates(collecting.PendingUpdates, applying);

        act.Should().Throw<XQueryRuntimeException>().Which.ErrorCode.Should().Be("XUDY9001");
        File.Exists(file).Should().BeFalse();
        (await Names(engine)).Should().Be("a");
    }

    [Fact]
    public async Task Without_a_host_resolver_a_uri_that_is_not_a_file_is_refused_before_anything_changes()
    {
        var (_, engine) = Setup(ResourcePolicy.CreateBuilder().AllowScheme("*").AllowWriteScheme("phoenixml").Build());

        var act = () => RunAsync(engine, Put(HostUri));

        (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be("XUDY9001");
        (await Names(engine)).Should().Be("a");
    }

    [Fact]
    public async Task Two_puts_to_one_uri_for_the_host_are_XUDY0031()
    {
        var host = new HostWriter();
        var (_, engine) = Setup(ResourcePolicy.CreateBuilder().AllowScheme("*").AllowWriteScheme("phoenixml").WithResourceResolver(host).Build());

        var act = () => RunAsync(engine, $"(put(doc('{Doc}'), '{HostUri}'), put(<x/>, '{HostUri}'))");

        (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be("XUDY0031");
        host.Asked.Should().BeEmpty();
    }

    // URIs that System.Uri.AbsoluteUri normalises: escapes of unreserved characters decoded, dot
    // segments removed under an authority, the scheme in lower case. The host gets the text.
    public static TheoryData<string> UnnormalisedUris => new()
    {
        "phoenixml:container/c/%2E%2E/d.xml",
        "phoenixml:container/./d.xml",
        "phoenixml://h/c/../d.xml",
        "phoenixml:container/c/%41.xml",
        "PHOENIXML:container/c/d.xml",
    };

    [Theory]
    [MemberData(nameof(UnnormalisedUris))]
    public async Task The_host_writer_is_given_the_uri_as_the_query_wrote_it(string written)
    {
        var host = new HostWriter();
        var (_, engine) = Setup(ResourcePolicy.CreateBuilder().AllowScheme("*").AllowWriteScheme("phoenixml").WithResourceResolver(host).Build());

        await RunAsync(engine, Put(written));

        host.Asked.Should().Equal(written);
        host.Written[written].ToString().Should().Be("<r><a>a</a><b/></r>");
    }

    [Fact]
    public async Task A_relative_uri_is_given_as_RFC_3986_resolves_it()
    {
        var host = new HostWriter();
        var (_, engine) = Setup(ResourcePolicy.CreateBuilder().AllowScheme("*").AllowWriteScheme("phoenixml").WithResourceResolver(host).Build());

        await RunAsync(engine, "declare base-uri 'phoenixml:container/c/'; " + Put("../d%41.xml"));

        host.Asked.Should().Equal("phoenixml:container/d%41.xml");
    }

    [Fact]
    public async Task Two_puts_to_uris_that_name_one_resource_are_XUDY0031()
    {
        var host = new HostWriter();
        var (_, engine) = Setup(ResourcePolicy.CreateBuilder().AllowScheme("*").AllowWriteScheme("phoenixml").WithResourceResolver(host).Build());

        var act = () => RunAsync(engine, $"(put(doc('{Doc}'), 'phoenixml:c/%41.xml'), put(<x/>, 'phoenixml:c/A.xml'))");

        (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be("XUDY0031");
        host.Asked.Should().BeEmpty();
    }

    // A path is not a URI: the host is given its file: URI (G-B).
    [Fact]
    public async Task A_path_is_given_to_the_host_as_its_file_uri()
    {
        var host = new HostWriter();
        var (_, engine) = Setup(ResourcePolicy.CreateBuilder().AllowScheme("*").AllowWriteTo("file", null, _allowed).WithResourceResolver(host).Build());
        var file = Path.Combine(_allowed, "out.xml");

        await RunAsync(engine, Put(file));

        host.Asked.Should().Equal(new Uri(file).AbsoluteUri);
        File.Exists(file).Should().BeFalse("the host took it");
    }

    // The policy is given the text the host would be given (G-B): a refusal when the list is
    // applied names the URI as the query wrote it, not as System.Uri.AbsoluteUri writes it.
    [Fact]
    public async Task A_refusal_when_the_list_is_applied_names_the_uri_as_the_query_wrote_it()
    {
        const string written = "phoenixml:container/c/%41.xml";
        var host = new HostWriter();
        var (_, engine) = Setup(ResourcePolicy.CreateBuilder().AllowScheme("*").WithResourceResolver(host).Build());

        var act = () => RunAsync(engine, Put(written));

        var error = (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which;
        error.ErrorCode.Should().Be("XUDY9001");
        error.Message.Should().Contain(written);
        host.Asked.Should().BeEmpty();
        (await Names(engine)).Should().Be("a");
    }

    private const string Output = "http://www.w3.org/2010/xslt-xquery-serialization";

    private static string Put3(string parameters)
        => $"(insert node <b/> into doc('{Doc}')/r, put(doc('{Doc}'), '{HostUri}', {parameters}))";

    [Fact]
    public async Task The_parameters_and_the_method_of_a_put_are_on_the_primitive_and_used_for_the_host()
    {
        var host = new HostWriter();
        var (_, engine) = Setup(ResourcePolicy.CreateBuilder().AllowScheme("*").AllowWriteScheme("phoenixml").WithResourceResolver(host).Build());
        using var context = engine.CreateContext();
        context.DeferPendingUpdates = true;

        await foreach (var _ in Compile(engine, Put3($"<output:serialization-parameters xmlns:output='{Output}'><output:method value='text'/></output:serialization-parameters>")).ExecuteAsync(context)) { }

        var put = context.PendingUpdates.Primitives.OfType<PutPrimitive>().Should().ContainSingle().Subject;
        put.Method.Should().Be("text");
        put.Parameters.Should().NotBeNull();
        put.Parameters!.LocalName.Should().Be("serialization-parameters");
        PendingUpdateApplicator.ApplyUpdates(context.PendingUpdates, context);
        host.Written[HostUri].ToString().Should().Be("a");
    }

    [Theory]
    [InlineData("()", "xml")]
    [InlineData("<output:serialization-parameters xmlns:output='" + Output + "'/>", "xml")]
    [InlineData("<output:serialization-parameters xmlns:output='" + Output + "'><output:method value='html'/></output:serialization-parameters>", "html")]
    [InlineData("subsequence(<output:serialization-parameters xmlns:output='" + Output + "'/>, 2)", "xml")]
    [InlineData("subsequence((<output:serialization-parameters xmlns:output='" + Output + "'><output:method value='html'/></output:serialization-parameters>, <x/>), 1, 1)", "html")]
    public async Task The_method_is_xml_when_the_parameters_give_none(string parameters, string method)
    {
        var host = new HostWriter();
        var (_, engine) = Setup(ResourcePolicy.CreateBuilder().AllowScheme("*").AllowWriteScheme("phoenixml").WithResourceResolver(host).Build());
        using var context = engine.CreateContext();
        context.DeferPendingUpdates = true;

        await foreach (var _ in Compile(engine, Put3(parameters)).ExecuteAsync(context)) { }

        context.PendingUpdates.Primitives.OfType<PutPrimitive>().Should().ContainSingle().Which.Method.Should().Be(method);
    }

    // Through a dynamic call (invoke updating) an empty sequence arrives as an empty array, and
    // through a partial application the parameters are the bound value.
    private const string HtmlParameters = "<output:serialization-parameters xmlns:output='" + Output + "'><output:method value='html'/></output:serialization-parameters>";

    [Theory]
    [InlineData("invoke updating fn:put#3(doc('" + Doc + "'), '" + HostUri + "', ())", "xml")]
    [InlineData("invoke updating fn:put#3(doc('" + Doc + "'), '" + HostUri + "', " + HtmlParameters + ")", "html")]
    [InlineData("let $p := " + HtmlParameters + " return invoke updating fn:put(?, ?, $p)(doc('" + Doc + "'), '" + HostUri + "')", "html")]
    public async Task The_method_of_a_put_called_dynamically(string call, string method)
    {
        var host = new HostWriter();
        var (_, engine) = Setup(ResourcePolicy.CreateBuilder().AllowScheme("*").AllowWriteScheme("phoenixml").WithResourceResolver(host).Build());
        using var context = engine.CreateContext();
        context.DeferPendingUpdates = true;

        await foreach (var _ in Compile(engine, call).ExecuteAsync(context)) { }

        context.PendingUpdates.Primitives.OfType<PutPrimitive>().Should().ContainSingle().Which.Method.Should().Be(method);
    }

    // An invalid third argument stops the query when fn:put is called: no update is applied, the
    // host is not asked. For an element, the code is the one fn:serialize gives for the same
    // parameters. A map and a string are not a node (the type of the argument); a text node is a
    // node but not an element (XQUF 3.0 §6.1: element(output:serialization-parameters)?).
    [Theory]
    [InlineData("<output:serialization-parameters xmlns:output='" + Output + "'><output:indent value='perhaps'/></output:serialization-parameters>", "SEPM0017")]
    [InlineData("<output:serialization-parameters xmlns:output='" + Output + "'><output:bogus value='1'/></output:serialization-parameters>", "SEPM0017")]
    [InlineData("<output:serialization-parameters xmlns:output='" + Output + "' xmlns:phx='https://schemas.phoenixml.dev/2026/functions'><phx:indent-style value='other'/></output:serialization-parameters>", "SEPM0016")]
    [InlineData("<output:serialization-parameters xmlns:output='" + Output + "'><output:method value='XML'/></output:serialization-parameters>", "SEPM0017")]
    [InlineData("<serialization-parameters/>", "XPTY0004")]
    [InlineData("map { 'method': 'text' }", "XPTY0004")]
    [InlineData("'text'", "XPTY0004")]
    [InlineData("text { 'text' }", "XPTY0004")]
    public async Task An_invalid_parameter_stops_the_query_before_any_change(string parameters, string code)
    {
        var host = new HostWriter();
        var (_, engine) = Setup(ResourcePolicy.CreateBuilder().AllowScheme("*").AllowWriteScheme("phoenixml").WithResourceResolver(host).Build());

        var act = () => RunAsync(engine, Put3(parameters));

        (await act.Should().ThrowAsync<XQueryRuntimeException>("fn:put with {0}", parameters)).Which.ErrorCode.Should().Be(code);
        host.Asked.Should().BeEmpty();
        (await Names(engine)).Should().Be("a");
        if (parameters.StartsWith('<'))
        {
            var serialize = () => RunAsync(engine, $"serialize(<a/>, {parameters})");
            (await serialize.Should().ThrowAsync<XQueryRuntimeException>("fn:serialize with {0}", parameters)).Which.ErrorCode.Should().Be(code);
        }
    }
}
