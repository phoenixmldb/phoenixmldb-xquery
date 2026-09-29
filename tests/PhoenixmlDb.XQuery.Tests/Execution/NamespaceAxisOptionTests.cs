using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// CompilationOptions.AllowNamespaceAxis compiles the namespace axis, an XPath feature XQuery does
/// not have. Default off: in XQuery it stays XQST0134.
/// </summary>
public sealed class NamespaceAxisOptionTests
{
    private static async Task<List<object?>> RunAsync(string query, bool allow)
    {
        var store = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store);
        var compiled = engine.Compile(query, new CompilationOptions { AllowNamespaceAxis = allow });
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors.Select(e => e.Message)));
        var results = new List<object?>();
        await foreach (var item in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
            results.Add(item);
        return results;
    }

    [Fact]
    public async Task With_the_option_the_namespace_axis_evaluates()
        => (await RunAsync("""string-join(sort(<a xmlns:p="urn:p"/>/namespace::* ! name()), ",")""", allow: true))
            .Should().Equal("p,xml");

    [Fact]
    public void Without_it_the_namespace_axis_is_a_static_error()
    {
        var act = () => new QueryEngine().Compile("""<a/>/namespace::*""");
        act.Should().Throw<Exception>().WithMessage("*XQST0134*");
    }
}
