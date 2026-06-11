using FluentAssertions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

public sealed class IndexLookupOperatorTests
{
    [Fact]
    public async Task Execute_DelegatesToLookupCallback_AndYieldsItsResults()
    {
        var op = new IndexLookupOperator
        {
            IndexName = "book/isbn",
            Predicate = new IndexEquality("978-0-13-468599-1"),
            LookupAsync = (idx, key, _) =>
            {
                idx.Should().Be("book/isbn");
                key.Should().Be(new IndexEquality("978-0-13-468599-1"));
                return AsyncEnumerableFrom("nodeA", "nodeB");
            }
        };

        var context = new QueryExecutionContext(new ContainerId(1));
        var items = new List<object?>();
        await foreach (var item in op.ExecuteAsync(context)) items.Add(item);

        items.Should().Equal("nodeA", "nodeB");
    }

    [Fact]
    public async Task Execute_WithNullLookup_YieldsNothing()
    {
        var op = new IndexLookupOperator
        {
            IndexName = "book/isbn",
            Predicate = new IndexEquality("doesn't matter"),
            LookupAsync = null
        };

        var context = new QueryExecutionContext(new ContainerId(1));
        var items = new List<object?>();
        await foreach (var item in op.ExecuteAsync(context)) items.Add(item);

        items.Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_FallsBackToContextResolver_WhenLookupIsNull()
    {
        // Production path: the host (storage layer) attaches a resolver to the
        // context; the operator carries no inline delegate. This proves the
        // fallback dispatch is symmetric with the inline-delegate path so
        // either wiring style produces the same results.
        var op = new IndexLookupOperator
        {
            IndexName = "book/isbn",
            Predicate = new IndexEquality("978-0-13-468599-1"),
            LookupAsync = null
        };

        var predicate = new IndexEquality("978-0-13-468599-1");
        var resolver = new StubIndexLookupResolver((predicate,
            new object?[] { "nodeA", "nodeB" }));
        var context = new QueryExecutionContext(new ContainerId(1))
        {
            IndexLookupResolver = resolver
        };

        var items = new List<object?>();
        await foreach (var item in op.ExecuteAsync(context)) items.Add(item);

        items.Should().Equal("nodeA", "nodeB");
        resolver.Calls.Should().ContainSingle()
            .Which.Should().Be(("book/isbn", (object)predicate));
    }

    [Fact]
    public async Task Execute_InlineLookupWins_OverContextResolver()
    {
        // Inline LookupAsync set explicitly on the operator should take precedence
        // over the context resolver so unit tests / mocked operators stay isolated
        // from any environmental wiring that happened to leak into the context.
        var resolver = new StubIndexLookupResolver(("any",
            new object?[] { "fromResolver" }));
        var op = new IndexLookupOperator
        {
            IndexName = "book/isbn",
            Predicate = new IndexEquality("any"),
            LookupAsync = (_, _, _) => AsyncEnumerableFrom("fromInline")
        };
        var context = new QueryExecutionContext(new ContainerId(1))
        {
            IndexLookupResolver = resolver
        };

        var items = new List<object?>();
        await foreach (var item in op.ExecuteAsync(context)) items.Add(item);

        items.Should().Equal("fromInline");
        resolver.Calls.Should().BeEmpty(because: "inline delegate must short-circuit the resolver");
    }

    private sealed class StubIndexLookupResolver : IIndexLookupResolver
    {
        private readonly Dictionary<object, object?[]> _byKey;
        public List<(string IndexName, object Key)> Calls { get; } = new();

        public StubIndexLookupResolver(params (object Key, object?[] Items)[] entries)
        {
            _byKey = entries.ToDictionary(e => e.Key, e => e.Items);
        }

        public async IAsyncEnumerable<object?> ResolveAsync(string indexName, object key, QueryExecutionContext context)
        {
            Calls.Add((indexName, key));
            if (_byKey.TryGetValue(key, out var items))
            {
                foreach (var i in items) { yield return i; await Task.Yield(); }
            }
        }
    }

    private static async IAsyncEnumerable<object?> AsyncEnumerableFrom(params object?[] items)
    {
        foreach (var i in items) { yield return i; await Task.Yield(); }
    }
}
