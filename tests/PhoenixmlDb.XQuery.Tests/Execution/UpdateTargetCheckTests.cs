using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// The checks XQuery Update Facility 3.0 makes on the target and the content of an update
/// expression (§5.1-5.4, §5.6). The update expressions took the first item of the target, of any
/// kind, and of the copy source, and checked nothing else: an insert into a text node, a rename of a
/// comment or two targets were accepted, and failed or did something else when the updates were
/// applied. The new value of `replace value of` was its first item, unatomized.
/// </summary>
public class UpdateTargetCheckTests
{
    private const string D = "doc('urn:test:d')";

    private static QueryEngine Setup()
    {
        var store = new XdmDocumentStore();
        store.LoadFromString("<r id='1'><a>a</a><b>b</b><!--c--><?p v?></r>", "urn:test:d");
        return new QueryEngine(nodeProvider: store, documentResolver: store);
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

    private static Task<string> Document(QueryEngine engine)
        => RunAsync(engine, $"string-join({D}/r/*!name(), ' ') || '/' || string({D}/r) || '/' || {D}/r/@id");

    [Theory]
    // §5.1 insert
    [InlineData($"insert node (<x/>, attribute y {{ 1 }}) into {D}/r", "XUTY0004")]
    [InlineData($"insert node <x/> into {D}/r/a/text()", "XUTY0005")]
    [InlineData($"insert node <x/> into ({D}/r/a, {D}/r/b)", "XUTY0005")]
    [InlineData("insert node <x/> into 1", "XUTY0005")]
    [InlineData($"insert node <x/> after {D}", "XUTY0006")]
    [InlineData($"insert node <x/> before {D}/r/@id", "XUTY0006")]
    [InlineData("insert node <x/> after <p/>", "XUDY0029")]
    [InlineData($"insert node attribute y {{ 1 }} into {D}", "XUTY0022")]
    [InlineData($"insert node attribute y {{ 1 }} before {D}/r", "XUDY0030")]
    // §5.2 delete
    [InlineData("delete node 1", "XUTY0007")]
    [InlineData($"delete nodes ({D}/r/a, 'x')", "XUTY0007")]
    // §5.3 replace
    [InlineData($"replace node {D} with <x/>", "XUTY0008")]
    [InlineData($"replace value of node ({D}/r/a, {D}/r/b) with 'v'", "XUTY0008")]
    [InlineData("replace node <p/> with <x/>", "XUDY0009")]
    [InlineData($"replace node {D}/r/a with attribute y {{ 1 }}", "XUTY0010")]
    [InlineData($"replace node {D}/r/@id with <x/>", "XUTY0011")]
    [InlineData($"replace node {D}/r/@id with 'text'", "XUTY0011")]
    [InlineData($"replace value of node {D}/r/comment() with 'a--b'", "XQDY0072")]
    [InlineData($"replace value of node {D}/r/comment() with 'a-'", "XQDY0072")]
    [InlineData($"replace value of node {D}/r/processing-instruction() with 'a?>b'", "XQDY0026")]
    // §5.4 rename, §5.6 copy modify
    [InlineData($"rename node {D}/r/a/text() as 'x'", "XUTY0012")]
    [InlineData($"rename node {D}/r/comment() as 'x'", "XUTY0012")]
    [InlineData($"copy $c := ({D}/r/a, {D}/r/b) modify () return $c", "XUTY0013")]
    [InlineData("copy $c := 1 modify () return $c", "XUTY0013")]
    public async Task A_target_or_content_the_expression_does_not_allow_is_an_error_and_nothing_changes(string update, string code)
    {
        var engine = Setup();

        var act = () => RunAsync(engine, update);

        (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be(code);
        (await Document(engine)).Should().Be("a b/ab/1");
    }

    [Theory]
    [InlineData($"insert node attribute y {{ 2 }} into {D}/r/a", "a b/ab/1")]
    [InlineData($"insert node <x>X</x> before {D}/r/a/text()", "a b/Xab/1")]
    [InlineData($"insert node (attribute y {{ 2 }}, <x>X</x>) as last into {D}/r", "a b x/abX/1")]
    [InlineData($"replace node {D}/r/@id with attribute id {{ 9 }}", "a b/ab/9")]
    // The new value is the atomized content, joined with spaces.
    [InlineData($"replace value of node {D}/r/a with <v>new</v>", "a b/newb/1")]
    [InlineData($"replace value of node {D}/r/a with (1, 2.5)", "a b/1 2.5b/1")]
    [InlineData($"replace value of node {D}/r/@id with ()", "a b/ab/")]
    // A node with no parent is not deleted (§5.2).
    [InlineData($"delete node {D}", "a b/ab/1")]
    public async Task An_allowed_target_and_content_are_updated(string update, string document)
    {
        var engine = Setup();

        (await RunAsync(engine, update)).Should().BeEmpty();

        (await Document(engine)).Should().Be(document);
    }
}
