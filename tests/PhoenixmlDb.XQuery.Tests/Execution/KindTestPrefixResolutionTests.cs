using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// QT3 K2-DirectConElemNamespace-78 + adjacent — element() and attribute() kind
/// tests with a prefixed name (e.g. <c>element(P:L)</c>) must resolve the prefix
/// using lexical namespace bindings. Previously the embedded NameTest inside the
/// KindTest never reached the NamespaceResolver, so the test never matched any
/// element and returned the empty sequence.
/// </summary>
public class KindTestPrefixResolutionTests
{
    [Fact]
    public async System.Threading.Tasks.Task ElementKindTest_WithPrefix_MatchesByResolvedNamespace()
    {
        var query = """
            let $e := document{(
                <X1:L xmlns:X1="http://example.com/URL1">1</X1:L>,
                <X2:L xmlns:X2="http://example.com/URL2">2</X2:L>
            )}
            return <outer xmlns:P="http://example.com/URL1">{ count($e/element(P:L)) }</outer>
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

        items.Should().HaveCount(1);
        var outer = items[0] as Xdm.Nodes.XdmElement;
        outer.Should().NotBeNull();
        outer!.StringValue.Should().Be("1",
            "element(P:L) with P bound to URL1 must match the X1:L child (URL1), not the X2:L child (URL2)");
    }

    [Fact]
    public async System.Threading.Tasks.Task ElementKindTest_WithEQName_MatchesByResolvedNamespace()
    {
        var query = """
            let $e := document{<L xmlns="http://example.com/URL1">hi</L>}
            return count($e/element(Q{http://example.com/URL1}L))
            """;

        var env = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: env, documentResolver: env);
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue();

        using var ctx = engine.CreateContext();
        var items = new System.Collections.Generic.List<object?>();
        await foreach (var item in compiled.ExecutionPlan!.ExecuteAsync(ctx))
            items.Add(item);

        items.Should().ContainSingle().Which.Should().Be((long)1,
            "Q{{uri}}local in element() kind test must resolve to the explicit URI");
    }

    [Fact]
    public async System.Threading.Tasks.Task ElementKindTest_NestedScopes_EachStepUsesLexicalBinding()
    {
        // QT3 K2-DirectConElemNamespace-78: outer P=URL1, inner P=URL2.
        // Each `element(P:L)` step must use its own scope's P binding.
        var query = """
            let $e := document{(
                <X1:L xmlns:X1="http://example.com/URL1">1</X1:L>,
                <X2:L xmlns:X2="http://example.com/URL2">2</X2:L>
            )}
            return <outer xmlns:P="http://example.com/URL1">{
              let $outer-hits := count($e/element(P:L))
              return <inner xmlns:P="http://example.com/URL2">{
                let $inner-hits := count($e/element(P:L))
                return ($outer-hits || "/" || $inner-hits)
              }</inner>
            }</outer>
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

        var outer = items[0] as Xdm.Nodes.XdmElement;
        outer.Should().NotBeNull();
        outer!.StringValue.Should().Be("1/1",
            "outer P=URL1 → match X1:L (1 hit); inner P=URL2 → match X2:L (1 hit). Each scope must use its lexical binding.");
    }
}
