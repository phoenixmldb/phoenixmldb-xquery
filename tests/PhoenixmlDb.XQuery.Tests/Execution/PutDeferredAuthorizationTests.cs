using AwesomeAssertions;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.Security;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// <c>fn:put</c> when the host applies the list (<see cref="QueryExecutionContext.DeferPendingUpdates"/>),
/// as a database does: its queries run with no resource policy, and it applies their lists with
/// one. fn:put authorises nothing when it is called; <see cref="PendingUpdateApplicator.ApplyUpdates"/>
/// authorises each write with the policy, and the resolver, of the context it is given, and refuses
/// every put (phx:XUDY9001) when that context has no policy. Before, fn:put was refused at the call
/// whenever the run had no policy, so a database could not use it at all.
/// Also XUDY0031 for two puts to one file under two spellings (a symbolic link and the path it leads
/// to): the authorised paths are compared. Before, the second write replaced the first when fn:put
/// had not authorised the URIs (a host resolver; now also a deferred list).
/// </summary>
public sealed class PutDeferredAuthorizationTests : IDisposable
{
    private const string Doc = "urn:test:d";
    private readonly string _root = Directory.CreateTempSubdirectory("phx-put-deferred").FullName;
    private readonly string _allowed;

    public PutDeferredAuthorizationTests()
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

    private ResourcePolicy Permits(IResourceResolver? resolver = null)
    {
        var builder = ResourcePolicy.CreateBuilder().AllowScheme("*").AllowWriteTo("file", null, _allowed);
        return (resolver is null ? builder : builder.WithResourceResolver(resolver)).Build();
    }

    private static ResourcePolicy ReadsOnly() => ResourcePolicy.CreateBuilder().AllowScheme("*").Build();

    private static (XdmDocumentStore Store, QueryEngine Engine) Setup(ResourcePolicy? policy)
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

    /// <summary>Runs <paramref name="query"/> with the list left to the host, and returns the context that holds it.</summary>
    private static async Task<QueryExecutionContext> CollectAsync(QueryEngine engine, string query)
    {
        var context = engine.CreateContext();
        context.DeferPendingUpdates = true;
        await foreach (var _ in Compile(engine, query).ExecuteAsync(context)) { }
        return context;
    }

    /// <summary>A context of the same store that applies a list, with <paramref name="policy"/>.</summary>
    private static QueryExecutionContext Applying(XdmDocumentStore store, ResourcePolicy? policy)
        => new QueryEngine(nodeProvider: store, documentResolver: store) { ResourcePolicy = policy }.CreateContext();

    private static Task<string> Names(QueryEngine engine) => RunAsync(engine, $"string-join(doc('{Doc}')/r/*!name(), ' ')");

    private static string FileUri(string path) => new Uri(path).AbsoluteUri;

    private static string Put(string uri) => $"(insert node <b/> into doc('{Doc}')/r, put(doc('{Doc}'), '{uri}'))";

    private static string PutTwo(string first, string second)
        => $"(insert node <b/> into doc('{Doc}')/r, put(doc('{Doc}'), '{first}'), put(<x/>, '{second}'))";

    private string Out => Path.Combine(_allowed, "out.xml");

    // ---- fn:put collects with no policy when the host applies the list ----

    [Fact]
    public async Task Deferred_with_no_policy_fn_put_gives_a_put_and_no_error()
    {
        var (_, engine) = Setup(policy: null);

        using var context = await CollectAsync(engine, Put(FileUri(Out)));

        var put = context.PendingUpdates.Primitives.OfType<PutPrimitive>().Should().ContainSingle().Subject;
        put.Uri.AbsoluteUri.Should().Be(FileUri(Out));
        put.Method.Should().Be("xml");
        File.Exists(Out).Should().BeFalse();
        (await Names(engine)).Should().Be("a", "the host has not applied the list");
    }

    [Fact]
    public async Task Deferred_a_relative_uri_is_resolved_against_the_static_base_uri()
    {
        var (_, engine) = Setup(policy: null);

        using var context = await CollectAsync(engine, $"declare base-uri '{FileUri(_allowed)}/'; " + Put("sub/../out.xml"));

        context.PendingUpdates.Primitives.OfType<PutPrimitive>().Should().ContainSingle()
            .Which.Uri.AbsoluteUri.Should().Be(FileUri(Out));
    }

    [Fact]
    public async Task Deferred_with_no_policy_the_parameters_are_still_checked_at_the_call()
    {
        var (_, engine) = Setup(policy: null);
        const string bad = "<output:serialization-parameters xmlns:output='http://www.w3.org/2010/xslt-xquery-serialization'>"
            + "<output:indent value='perhaps'/></output:serialization-parameters>";

        var act = () => CollectAsync(engine, $"put(doc('{Doc}'), '{FileUri(Out)}', {bad})");

        (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be("SEPM0017");
    }

    // ---- the context that applies the list authorises the write ----

    [Fact]
    public async Task Applied_with_a_policy_that_refuses_the_write_is_XUDY9001_and_nothing_changes()
    {
        var (store, engine) = Setup(policy: null);
        using var collected = await CollectAsync(engine, Put(FileUri(Out)));
        using var applying = Applying(store, ReadsOnly());

        var act = () => PendingUpdateApplicator.ApplyUpdates(collected.PendingUpdates, applying);

        var error = act.Should().Throw<XQueryRuntimeException>().Which;
        error.ErrorCode.Should().Be("XUDY9001");
        error.ErrorNamespaceUri.Should().Be("https://schemas.phoenixml.dev/2026/functions");
        File.Exists(Out).Should().BeFalse();
        (await Names(engine)).Should().Be("a", "the insert of the same list is not applied");
    }

    [Fact]
    public async Task Applied_with_no_policy_is_XUDY9001_and_nothing_changes()
    {
        var (store, engine) = Setup(policy: null);
        using var collected = await CollectAsync(engine, Put(FileUri(Out)));
        using var applying = Applying(store, policy: null);

        var act = () => PendingUpdateApplicator.ApplyUpdates(collected.PendingUpdates, applying);

        act.Should().Throw<XQueryRuntimeException>().Which.ErrorCode.Should().Be("XUDY9001");
        File.Exists(Out).Should().BeFalse();
        (await Names(engine)).Should().Be("a");
    }

    [Fact]
    public async Task Applied_with_a_policy_that_permits_it_the_resolver_gets_the_text()
    {
        var host = new HostWriter();
        var (store, engine) = Setup(policy: null);
        using var collected = await CollectAsync(engine, Put(FileUri(Out)));
        using var applying = Applying(store, Permits(host));

        PendingUpdateApplicator.ApplyUpdates(collected.PendingUpdates, applying);

        host.Asked.Should().Equal(FileUri(Out));
        host.Written[FileUri(Out)].ToString().Should().Be("<r><a>a</a><b/></r>");
        File.Exists(Out).Should().BeFalse("the host took it");
        (await Names(engine)).Should().Be("a b");
    }

    [Fact]
    public async Task Applied_with_a_policy_that_permits_it_and_no_resolver_the_library_writes_the_file()
    {
        var (store, engine) = Setup(policy: null);
        using var collected = await CollectAsync(engine, Put(FileUri(Out)));
        using var applying = Applying(store, Permits());

        PendingUpdateApplicator.ApplyUpdates(collected.PendingUpdates, applying);

        (await File.ReadAllTextAsync(Out)).Should().Be("<r><a>a</a><b/></r>");
    }

    [Fact]
    public async Task Not_deferred_with_no_policy_fn_put_is_refused_at_the_call_as_before()
    {
        var (_, engine) = Setup(policy: null);

        var act = () => RunAsync(engine, Put(FileUri(Out)));

        var error = (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which;
        error.ErrorCode.Should().Be("XUDY9001");
        error.Message.Should().Contain("--allow-write", "the refusal of the call, not of ApplyUpdates");
        File.Exists(Out).Should().BeFalse();
        (await Names(engine)).Should().Be("a");
    }

    // ---- XUDY0031 for one file under two spellings ----

    /// <summary>
    /// allowed/real and allowed/link, a symbolic link to it. Windows: the CI runner creates
    /// directory links, as the other link tests of this project need (no skip).
    /// </summary>
    private (string Link, string Real) LinkAndReal()
    {
        var real = Directory.CreateDirectory(Path.Combine(_allowed, "real")).FullName;
        var link = Path.Combine(_allowed, "link");
        Directory.CreateSymbolicLink(link, real);
        return (Path.Combine(link, "out.xml"), Path.Combine(real, "out.xml"));
    }

    [Fact]
    public async Task Deferred_two_puts_to_one_file_through_a_link_and_the_real_path_are_XUDY0031()
    {
        var (link, real) = LinkAndReal();
        var (store, engine) = Setup(policy: null);
        using var collected = await CollectAsync(engine, PutTwo(FileUri(link), FileUri(real)));
        using var applying = Applying(store, Permits());

        var act = () => PendingUpdateApplicator.ApplyUpdates(collected.PendingUpdates, applying);

        var error = act.Should().Throw<XQueryRuntimeException>().Which;
        error.ErrorCode.Should().Be("XUDY0031");
        error.Message.Should().Contain(FileUri(link)).And.Contain(FileUri(real));
        File.Exists(real).Should().BeFalse();
        (await Names(engine)).Should().Be("a");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Through_a_host_resolver_that_does_not_take_them_two_puts_to_one_file_are_XUDY0031(bool deferred)
    {
        // The host does not take the URIs, so the library would write the file twice; before, the
        // second write replaced the first.
        var (link, real) = LinkAndReal();
        var host = new HostWriter { Takes = _ => false };
        var (store, engine) = Setup(Permits(host));
        var query = PutTwo(FileUri(link), FileUri(real));

        Func<Task> act = deferred
            ? async () =>
            {
                using var collected = await CollectAsync(engine, query);
                using var applying = Applying(store, Permits(host));
                PendingUpdateApplicator.ApplyUpdates(collected.PendingUpdates, applying);
            }
            : () => RunAsync(engine, query);

        (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be("XUDY0031");
        host.Asked.Should().BeEmpty("the check is made before any write");
        File.Exists(real).Should().BeFalse();
        (await Names(engine)).Should().Be("a");
    }

    [Fact]
    public async Task Without_a_resolver_and_not_deferred_two_puts_to_one_file_are_XUDY0031_as_before()
    {
        var (link, real) = LinkAndReal();
        var (_, engine) = Setup(Permits());

        var act = () => RunAsync(engine, PutTwo(FileUri(link), FileUri(real)));

        (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be("XUDY0031");
        File.Exists(real).Should().BeFalse();
        (await Names(engine)).Should().Be("a");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Two_puts_to_two_files_are_both_written(bool deferred)
    {
        var first = Path.Combine(_allowed, "one.xml");
        var second = Path.Combine(_allowed, "two.xml");
        var (store, engine) = Setup(deferred ? null : Permits());
        var query = PutTwo(FileUri(first), FileUri(second));

        if (deferred)
        {
            using var collected = await CollectAsync(engine, query);
            using var applying = Applying(store, Permits());
            PendingUpdateApplicator.ApplyUpdates(collected.PendingUpdates, applying);
        }
        else
        {
            await RunAsync(engine, query);
        }

        (await File.ReadAllTextAsync(first)).Should().Be("<r><a>a</a><b/></r>");
        (await File.ReadAllTextAsync(second)).Should().Be("<x/>");
    }
}
