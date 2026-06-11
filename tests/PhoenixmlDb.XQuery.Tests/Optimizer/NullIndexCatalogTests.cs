using FluentAssertions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Optimizer;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Optimizer;

public sealed class NullIndexCatalogTests
{
    [Fact]
    public void Null_ReturnsNoCoverage_ForAnyPath()
    {
        var catalog = new NullIndexCatalog();
        var coverage = catalog.LookupValueIndex(
            new ContainerId(1), elementPath: new[] { "book" }, attributeName: "isbn");
        coverage.Should().BeNull(
            because: "the null catalog never claims index coverage");
    }
}
