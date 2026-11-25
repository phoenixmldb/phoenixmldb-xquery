using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.Security;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// <c>fn:put</c> (XQUF 3.0 §6.1) stores a document or element when the query's updates are applied.
/// A write needs a resource policy that allows WriteDocument for the resolved URI; with no policy
/// fn:put is refused (Lucas, 2026-11-20). A refusal is phx:XUDY9001, and nothing is changed: not
/// the file, and not the other updates of the query.
/// </summary>
public sealed class PutFunctionTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("phx-put").FullName;
    private readonly string _allowed;
    private readonly string _outside;

    public PutFunctionTests()
    {
        _allowed = Directory.CreateDirectory(Path.Combine(_root, "allowed")).FullName;
        _outside = Directory.CreateDirectory(Path.Combine(_root, "outside")).FullName;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private ResourcePolicy WritePolicy => ResourcePolicy.CreateBuilder()
        .AllowScheme("*")
        .AllowWriteTo("file", null, _allowed)
        .Build();

    private static (XdmDocumentStore Store, QueryEngine Engine) Setup(ResourcePolicy? policy)
    {
        var store = new XdmDocumentStore();
        store.LoadFromString("<r><a>a</a></r>", "urn:test:d");
        return (store, new QueryEngine(nodeProvider: store, documentResolver: store) { ResourcePolicy = policy });
    }

    private static async Task<string> RunAsync(QueryEngine engine, string query)
    {
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors.Select(e => e.Message)));
        var items = new List<object?>();
        await foreach (var item in engine.ExecuteAsync(compiled.ExecutionPlan!))
            items.Add(item);
        return string.Join(" ", items.Select(i => i?.ToString()));
    }

    private static string Uri(string path) => new Uri(path).AbsoluteUri;

    private Task<string> Names(QueryEngine engine) => RunAsync(engine, "string-join(doc('urn:test:d')/r/*!name(), ' ')");

    [Fact]
    public async Task With_a_policy_that_allows_the_directory_the_document_is_stored()
    {
        var (_, engine) = Setup(WritePolicy);
        var file = Path.Combine(_allowed, "out.xml");

        (await RunAsync(engine, $"fn:put(document {{ <out n='1'>text</out> }}, '{Uri(file)}')")).Should().BeEmpty();

        (await File.ReadAllTextAsync(file)).Should().Contain("<out n=\"1\">text</out>");
        Directory.GetFiles(_allowed).Should().Equal(file);
    }

    [Fact]
    public async Task The_node_is_stored_as_the_other_updates_of_the_query_leave_it()
    {
        var (_, engine) = Setup(WritePolicy);
        var file = Path.Combine(_allowed, "r.xml");

        await RunAsync(engine, $"(fn:put(doc('urn:test:d')/r, '{Uri(file)}'), insert node <b/> into doc('urn:test:d')/r)");

        (await File.ReadAllTextAsync(file)).Should().Contain("<r><a>a</a><b/></r>");
    }

    public static TheoryData<string> Refused => new()
    {
        // No policy at all
        "no-policy",
        // A policy, and a file outside the directory it allows
        "outside",
        // ".." out of the allowed directory
        "dot-dot",
        // A link in the allowed directory to a directory outside it
        "link",
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public async Task A_write_the_policy_does_not_allow_is_refused_and_nothing_changes(string how)
    {
        string target;
        ResourcePolicy? policy = WritePolicy;
        switch (how)
        {
            case "no-policy":
                policy = null;
                target = Path.Combine(_allowed, "out.xml");
                break;
            case "outside":
                target = Path.Combine(_outside, "out.xml");
                break;
            case "dot-dot":
                target = _allowed + "/../outside/out.xml";
                break;
            default:
                Directory.CreateSymbolicLink(Path.Combine(_allowed, "link"), _outside);
                target = Path.Combine(_allowed, "link", "out.xml");
                break;
        }
        var (_, engine) = Setup(policy);

        // With another update in the same query: it is not made either.
        var act = () => RunAsync(engine, $"(insert node <b/> into doc('urn:test:d')/r, fn:put(<out/>, '{(how == "dot-dot" ? "file://" + target : Uri(target))}'))");

        var error = (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which;
        error.ErrorCode.Should().Be("XUDY9001");
        error.ErrorPrefix.Should().Be("phx");
        Directory.GetFiles(_outside).Should().BeEmpty();
        Directory.GetFiles(_allowed).Should().BeEmpty();
        (await Names(engine)).Should().Be("a");
    }

    [Fact]
    public void A_directory_replaced_by_a_link_after_the_write_was_allowed_is_refused()
    {
        // fn:put authorises allowed/sub/out.xml; before the write, sub becomes a link out of the
        // allowed directory. The file opened beside the target is not in the allowed directory:
        // the write is refused and nothing stays behind.
        var sub = Directory.CreateDirectory(Path.Combine(_allowed, "sub")).FullName;
        var authorized = WritePolicy.Authorize(Uri(Path.Combine(sub, "out.xml")), ResourceAccessKind.WriteDocument);
        Directory.Delete(sub);
        Directory.CreateSymbolicLink(sub, _outside);

        var act = () => AuthorizedFile.WriteAllText(authorized, "<out/>");

        act.Should().Throw<ResourceAccessDeniedException>();
        Directory.GetFiles(_outside).Should().BeEmpty();
    }

    [Theory]
    [InlineData("fn:put(comment { 'c' }, 'out.xml')", "FOUP0001")]
    [InlineData("fn:put(text { 't' }, 'out.xml')", "FOUP0001")]
    [InlineData("fn:put(<out/>, 'http:\\\\invalid>URI')", "FOUP0002")]
    public async Task A_node_that_is_not_a_document_or_element_or_an_invalid_uri_is_an_error(string query, string code)
    {
        var (_, engine) = Setup(WritePolicy);

        var act = () => RunAsync(engine, query);

        (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be(code);
    }

    [Fact]
    public async Task Two_puts_to_one_uri_are_XUDY0031_and_nothing_is_written()
    {
        var (_, engine) = Setup(WritePolicy);
        var uri = Uri(Path.Combine(_allowed, "out.xml"));

        var act = () => RunAsync(engine, $"(fn:put(<a/>, '{uri}'), fn:put(<b/>, '{uri}'))");

        (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be("XUDY0031");
        Directory.GetFiles(_allowed).Should().BeEmpty();
    }

    [Fact]
    public async Task A_put_in_a_modify_clause_is_XUDY0037()
    {
        var (_, engine) = Setup(WritePolicy);
        var uri = Uri(Path.Combine(_allowed, "out.xml"));

        var act = () => RunAsync(engine, $"copy $c := <a/> modify fn:put($c, '{uri}') return $c");

        (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be("XUDY0037");
        Directory.GetFiles(_allowed).Should().BeEmpty();
    }

    [Fact]
    public void A_call_of_fn_put_is_an_updating_expression()
    {
        var (_, engine) = Setup(WritePolicy);

        var compiled = engine.Compile("let $x := fn:put(<a/>, 'out.xml') return 1");

        compiled.Success.Should().BeFalse();
        compiled.Errors[0].Code.Should().Be("XUST0001");
    }
}
