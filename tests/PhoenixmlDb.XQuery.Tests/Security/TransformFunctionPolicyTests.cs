using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.Functions;
using PhoenixmlDb.XQuery.Security;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Security;

/// <summary>
/// A host can turn <c>fn:transform</c> off (<see cref="ResourcePolicy.AllowTransformFunction"/>).
/// Every way to call it then fails with one error before the XSLT processor is asked for
/// anything, and the function is not reported as available. It is on unless turned off.
/// </summary>
/// <remarks>
/// The XSLT processor is a process-wide registration, so the class does not run beside others.
/// </remarks>
[Collection(TimingSensitiveTests.Name)]
public sealed class TransformFunctionPolicyTests
{
    private sealed class RecordingProvider : ITransformProvider
    {
        public int Calls { get; private set; }

        public ValueTask<object?> TransformAsync(IDictionary<object, object?> options, PhoenixmlDb.XQuery.Ast.ExecutionContext context)
        {
            Calls++;
            return ValueTask.FromResult<object?>(new Dictionary<object, object?> { ["output"] = "ran" });
        }
    }

    private static async Task<(string Result, int Calls)> RunAsync(string query, ResourcePolicy? policy)
    {
        var saved = TransformFunction.Provider;
        var provider = new RecordingProvider();
        TransformFunction.Provider = provider;
        try
        {
            var store = new XdmDocumentStore();
            var engine = new QueryEngine(nodeProvider: store, documentResolver: store) { ResourcePolicy = policy };
            var compiled = engine.Compile(query);
            if (!compiled.Success)
                return ("COMPILE " + string.Join("; ", compiled.Errors.Select(e => e.Code)), provider.Calls);
            var items = new List<object?>();
            try
            {
                await foreach (var item in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
                    items.Add(item);
            }
            catch (XQueryException ex)
            {
                return (ex.ErrorCode + ": " + ex.Message, provider.Calls);
            }
            return (string.Join(",", items.Select(i => i?.ToString() ?? "")), provider.Calls);
        }
        finally
        {
            TransformFunction.Provider = saved;
        }
    }

    private static ResourcePolicy Off => ResourcePolicy.CreateBuilder().AllowScheme("*").AllowTransformFunction(false).Build();

    public static TheoryData<string> CallForms => new()
    {
        "transform(map { 'stylesheet-text': '<x/>' })?output",
        "fn:transform(map { 'stylesheet-location': 'http://example.org/s.xsl' })?output",
        "transform#1(map { })?output",
        "let $f := transform#1 return $f(map { })?output",
        "let $f := transform(?) return $f(map { })?output",
        "apply(transform#1, [map { }])?output",
        "for-each(map { }, transform#1) ! ?output",
        "(map { } ! transform(.))?output",
        "(map { } => transform())?output",
        "declare function local:f($m) { transform($m) }; local:f(map { })?output",
    };

    [Theory]
    [MemberData(nameof(CallForms))]
    public async Task Turned_off_every_call_form_is_FOXT0001_and_the_processor_is_not_asked(string query)
    {
        var (result, calls) = await RunAsync(query, Off);

        result.Should().StartWith("FOXT0001").And.Contain("resource policy");
        calls.Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(CallForms))]
    public async Task By_default_every_call_form_runs(string query)
    {
        (await RunAsync(query, null)).Should().Be(("ran", 1));
        (await RunAsync(query, ResourcePolicy.CreateBuilder().AllowScheme("*").Build())).Should().Be(("ran", 1));
    }

    [Fact]
    public async Task Turned_off_function_lookup_finds_nothing()
    {
        (await RunAsync("empty(function-lookup(QName('http://www.w3.org/2005/xpath-functions', 'transform'), 1))", Off))
            .Result.Should().Be("True");
        (await RunAsync("exists(function-lookup(QName('http://www.w3.org/2005/xpath-functions', 'transform'), 1))", null))
            .Result.Should().Be("True");
        // Another function is found as before.
        (await RunAsync("exists(function-lookup(QName('http://www.w3.org/2005/xpath-functions', 'abs'), 1))", Off))
            .Result.Should().Be("True");
    }

    [Fact]
    public void It_is_on_unless_the_host_turns_it_off()
    {
        ResourcePolicy.CreateBuilder().Build().AllowTransformFunction.Should().BeTrue();
        ResourcePolicy.Unrestricted.AllowTransformFunction.Should().BeTrue();
        ResourcePolicy.ServerDefault.AllowTransformFunction.Should().BeTrue();
        ResourcePolicy.InMemoryOnly.AllowTransformFunction.Should().BeTrue();
        ResourcePolicy.CreateBuilder().AllowTransformFunction(false).Build().AllowTransformFunction.Should().BeFalse();
        ResourcePolicy.CreateBuilder().AllowTransformFunction(false).AllowTransformFunction().Build().AllowTransformFunction.Should().BeTrue();
    }
}
