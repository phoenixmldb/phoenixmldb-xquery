using System.Diagnostics;
using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Security;

/// <summary>
/// Deeply nested input must not cost time out of proportion to its size, nor run past a
/// cancellation. fn:parse-xml was quadratic in depth (64,000 nested elements: 6 s; 128,000:
/// 25 s) because each element asked .NET for its base URI, which walks every ancestor. A schema
/// document nested thousands of levels deep sends System.Xml's own loader the same way, and
/// that cannot be cancelled, so such a document is refused before it gets there.
/// </summary>
public sealed class DeepNestingTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "phx-deep-" + Guid.NewGuid().ToString("N"));

    public DeepNestingTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static string Nested(int depth) =>
        string.Concat(Enumerable.Repeat("<a>", depth)) + string.Concat(Enumerable.Repeat("</a>", depth));

    private static async Task<List<object?>> Run(string query, string xml, CancellationToken token = default)
    {
        var store = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store);
        var compiled = engine.Compile("declare variable $xml external; " + query);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors));
        var context = engine.CreateContext(cancellationToken: token);
        context.SetExternalVariable("xml", xml);
        var results = new List<object?>();
        await foreach (var item in compiled.ExecutionPlan!.ExecuteAsync(context))
            results.Add(item);
        return results;
    }

    [Fact]
    public async Task ParseXml_of_a_deeply_nested_document_is_not_quadratic()
    {
        // 150,000 levels took about 35 s; it now takes about a second. Compared by the ratio of
        // two depths, not against a clock: a wall-clock bound fails at random on a slow CI
        // runner. Four times the depth is about four times the time when linear, sixteen when
        // quadratic; the best of three runs of each takes out a GC or a scheduling pause.
        async Task<double> BestMs(int depth)
        {
            var best = double.MaxValue;
            for (var run = 0; run < 3; run++)
            {
                var watch = Stopwatch.StartNew();
                var result = await Run("count(parse-xml($xml)//a)", Nested(depth));
                watch.Stop();
                result.Should().ContainSingle().Which.Should().Be((long)depth);
                best = Math.Min(best, watch.Elapsed.TotalMilliseconds);
            }
            return best;
        }

        var small = await BestMs(40_000);
        var large = await BestMs(160_000);
        (large / Math.Max(small, 1)).Should().BeLessThan(10, $"40,000 levels took {small:F0} ms, 160,000 took {large:F0} ms");
    }

    /// <summary>
    /// The same on a small stack. A thread's default stack is 1 MB on Windows and 8 MB on most
    /// Linux systems, so a step that recurses once per level passes on one and ends the process
    /// on the other; 256 KB makes any such step fail everywhere.
    /// </summary>
    [Fact]
    public void ParseXml_of_a_deeply_nested_document_does_not_need_a_deep_stack()
    {
        object? result = null;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
#pragma warning disable xUnit1031, CA1849 // the point is to stay on this thread's small stack
                // The text sits at the bottom, so the string value has to be gathered through
                // every level, from the document and from the outermost element.
                var xml = string.Concat(Enumerable.Repeat("<a>", 150_000)) + "x" + string.Concat(Enumerable.Repeat("</a>", 150_000));
                result = Run("let $d := parse-xml($xml) return (count($d//a), string($d), string($d/a), data($d/a) = 'x')", xml)
                    .GetAwaiter().GetResult();
#pragma warning restore xUnit1031, CA1849
            }
#pragma warning disable CA1031 // reported through the assertion below
            catch (Exception ex) { failure = ex; }
#pragma warning restore CA1031
        }, maxStackSize: 256 * 1024);
        thread.Start();
        thread.Join();

        failure.Should().BeNull();
        result.Should().BeEquivalentTo(new object[] { 150_000L, "x", "x", true });
    }

    [Fact]
    public async Task ParseXml_stops_when_the_query_is_cancelled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var act = () => Run("count(parse-xml($xml)//a)", Nested(50_000), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static string SchemaNestedIn(int depth) =>
        "<xs:schema xmlns:xs='http://www.w3.org/2001/XMLSchema' targetNamespace='urn:d'>"
        + "<xs:element name='e' type='xs:string'><xs:annotation><xs:appinfo>" + Nested(depth)
        + "</xs:appinfo></xs:annotation></xs:element></xs:schema>";

    [Fact]
    public void A_schema_within_the_depth_limit_loads()
    {
        var provider = new XsdSchemaProvider();
        provider.AddFromString("urn:d", SchemaNestedIn(100));
        provider.HasElementDeclaration("urn:d", "e").Should().BeTrue();
    }

    [Fact]
    public async Task A_schema_nested_past_the_limit_is_refused_promptly()
    {
        var provider = new XsdSchemaProvider();
        // The refusal is the result; the bound only catches a hang, so it is far above the time
        // any runner takes (a 5 s bound failed at random on Windows CI).
        var load = Task.Run(() => provider.AddFromString("urn:d", SchemaNestedIn(20_000)));
        (await Task.WhenAny(load, Task.Delay(TimeSpan.FromSeconds(60))))
            .Should().BeSameAs(load, "loading the schema was still running after 60 s");
        var act = () => load;
        (await act.Should().ThrowAsync<SchemaException>()).Which.Message.Should().Contain("levels deep");
        provider.HasElementDeclaration("urn:d", "e").Should().BeFalse();
    }

    [Fact]
    public async Task The_limit_applies_to_a_schema_imported_by_a_query_too()
    {
        var path = Path.Combine(_dir, "deep.xsd");
        await File.WriteAllTextAsync(path, SchemaNestedIn(20_000));
        var engine = new QueryEngine();
        // The refusal is the result; the bound only catches a hang (a 5 s bound failed at random
        // on Windows CI, 5.6 s for what takes ~100 ms on a workstation).
        var compile = Task.Run(() => engine.Compile($"import schema namespace d = 'urn:d' at '{new Uri(path).AbsoluteUri}'; 1"));
        (await Task.WhenAny(compile, Task.Delay(TimeSpan.FromSeconds(60))))
            .Should().BeSameAs(compile, "compiling the query was still running after 60 s");
        var compiled = await compile;

        compiled.Success.Should().BeFalse();
        compiled.Errors.Should().Contain(e => e.Code == "XQST0059");
    }
}
