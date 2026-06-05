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
            Key = "978-0-13-468599-1",
            LookupAsync = (idx, key, _) =>
            {
                idx.Should().Be("book/isbn");
                key.Should().Be("978-0-13-468599-1");
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
            Key = "doesn't matter",
            LookupAsync = null
        };

        var context = new QueryExecutionContext(new ContainerId(1));
        var items = new List<object?>();
        await foreach (var item in op.ExecuteAsync(context)) items.Add(item);

        items.Should().BeEmpty();
    }

    private static async IAsyncEnumerable<object?> AsyncEnumerableFrom(params object?[] items)
    {
        foreach (var i in items) { yield return i; await Task.Yield(); }
    }
}
