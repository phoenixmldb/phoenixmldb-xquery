using System.Text;
using FluentAssertions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// phx:metadata resolves its key before the host's IMetadataProvider sees it (namespace-consolidation design §3.4),
/// through the real query pipeline: prolog-declared and host-bound prefixes resolve, dbxml always means the metadata
/// namespace, an unbound prefix raises FONS0004, and a provider never receives a prefix. The literal "dbxml:" routing
/// this replaced made a prolog-declared prefix unusable in a key.
/// </summary>
public sealed class MetadataKeyResolutionTests
{
    private const string Meta = "Q{https://schemas.phoenixml.dev/2026/meta}";

    private sealed class RecordingProvider : IMetadataProvider
    {
        private readonly List<string> _keys = [];
        public IReadOnlyList<string> Keys => _keys;

        public byte[]? GetMetadata(DocumentId documentId, string key)
        {
            _keys.Add(key);
            return Encoding.UTF8.GetBytes(key == Meta + "size" ? "1024" : "value");
        }

        public IEnumerable<(string Key, byte[] Value)> GetAllMetadata(DocumentId documentId) => [];
    }

    private static async Task<(List<object?> Items, RecordingProvider Provider)> EvalAsync(
        string query, IReadOnlyDictionary<string, string>? bindings = null)
    {
        var provider = new RecordingProvider();
        var env = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: env, metadataProvider: provider, documentResolver: env);
        var compiled = engine.Compile(query, new CompilationOptions { StaticNamespaces = bindings });
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors.Select(e => $"{e.Code} {e.Message}")));
        using var ctx = engine.CreateContext();
        var items = new List<object?>();
        await foreach (var item in compiled.ExecutionPlan!.ExecuteAsync(ctx)) items.Add(item);
        return (items, provider);
    }

    [Fact]
    public async Task APrologDeclaredPrefix_ResolvesInAKey()
        => (await EvalAsync("declare namespace p = 'urn:p'; phx:metadata(parse-xml('<a/>'), 'p:status')"))
            .Provider.Keys.Should().Equal("Q{urn:p}status");

    [Fact]
    public async Task AHostBoundPrefix_ResolvesInAKey()
        => (await EvalAsync("phx:metadata(parse-xml('<a/>'), 'h:status')", new Dictionary<string, string> { ["h"] = "urn:host" }))
            .Provider.Keys.Should().Equal("Q{urn:host}status");

    [Fact]
    public async Task ASystemKey_IsAnIntegerInTheMetadataNamespace()
    {
        var (items, provider) = await EvalAsync("phx:metadata(parse-xml('<a/>'), 'dbxml:size') instance of xs:integer");
        items.Should().Equal(true);
        provider.Keys.Should().Equal(Meta + "size");
    }

    [Fact]
    public async Task Dbxml_MeansTheMetadataNamespace_EvenWhenThePrologRebindsIt()
        => (await EvalAsync("declare namespace dbxml = 'urn:other'; phx:metadata(parse-xml('<a/>'), 'dbxml:size')"))
            .Provider.Keys.Should().Equal(Meta + "size");

    [Fact]
    public async Task AnUnboundPrefix_RaisesFONS0004()
    {
        var act = () => EvalAsync("phx:metadata(parse-xml('<a/>'), 'nope:status')");
        (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be("FONS0004");
    }

    [Fact]
    public async Task UnprefixedAndExpandedKeys_PassThrough()
        => (await EvalAsync("phx:metadata(parse-xml('<a/>'), 'status'), phx:metadata(parse-xml('<a/>'), 'Q{urn:x}status')"))
            .Provider.Keys.Should().Equal("status", "Q{urn:x}status");

    [Fact]
    public async Task AProvider_NeverReceivesAPrefix()
    {
        var (_, provider) = await EvalAsync(
            "declare namespace p = 'urn:p'; for $k in ('status', 'p:status', 'dbxml:name', 'Q{urn:x}y', 'math:pi', 'http://example.com/key') "
            + "return phx:metadata(parse-xml('<a/>'), $k)");
        provider.Keys.Should().HaveCount(6).And.OnlyContain(k => k.StartsWith("Q{", StringComparison.Ordinal) || !k.StartsWith("p:", StringComparison.Ordinal))
            .And.NotContain(k => k.StartsWith("p:", StringComparison.Ordinal) || k.StartsWith("dbxml:", StringComparison.Ordinal) || k.StartsWith("math:", StringComparison.Ordinal));
        provider.Keys.Should().Contain("http://example.com/key", "a string that is not a lexical QName passes through unchanged");
    }
}
