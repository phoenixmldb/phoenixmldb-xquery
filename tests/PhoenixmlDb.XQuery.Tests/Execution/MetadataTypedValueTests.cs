using AwesomeAssertions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// phoenixml#132. IMetadataProvider returns every value of a key, each at its stored type, in stored order, and
/// phx:metadata returns them as item()*: a set gives all its values, an xs:integer stays an xs:integer, and the map
/// form's values are sequences too. The key may be the Clark form {uri}local or an xs:QName, and the arguments follow
/// the function conversion rules: a $node that is not a node is XPTY0004, and a node given as $key is atomized.
/// </summary>
public sealed class MetadataTypedValueTests
{
    private sealed class Provider : IMetadataProvider
    {
        public List<string> Keys { get; } = [];

        private static readonly Dictionary<string, IReadOnlyList<XdmValue>> Values = new()
        {
            ["tags"] = [XdmValue.XsString("b"), XdmValue.XsString("a"), XdmValue.XsString("c")],
            ["pages"] = [XdmValue.XsInteger(120)],
            ["published"] = [XdmValue.Date(new DateOnly(2026, 11, 18))],
            ["Q{urn:demo}owner"] = [XdmValue.XsString("ada")],
            ["Q{}plain"] = [XdmValue.XsString("no namespace")],
            ["status"] = [XdmValue.XsString("draft")],
        };

        public IReadOnlyList<XdmValue> GetMetadata(DocumentId documentId, string key)
        {
            Keys.Add(key);
            return Values.TryGetValue(key, out var v) ? v : [];
        }

        public IEnumerable<(string Key, IReadOnlyList<XdmValue> Values)> GetAllMetadata(DocumentId documentId)
            => Values.Select(kv => (kv.Key, kv.Value));
    }

    private static async Task<(List<object?> Items, Provider Provider)> Eval(string query)
    {
        var provider = new Provider();
        var store = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: store, metadataProvider: provider, documentResolver: store);
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors.Select(e => $"{e.Code} {e.Message}")));
        using var ctx = engine.CreateContext();
        var items = new List<object?>();
        await foreach (var item in compiled.ExecutionPlan!.ExecuteAsync(ctx)) items.Add(item);
        return (items, provider);
    }

    private const string Doc = "parse-xml('<a k=\"status\"/>')";

    [Fact]
    public async Task A_set_returns_all_its_values_in_stored_order_and_a_scalar_one()
        => (await Eval($"string-join(phx:metadata({Doc}, 'tags'), ','), count(phx:metadata({Doc}, 'status')), count(phx:metadata({Doc}, 'none'))"))
            .Items.Should().Equal("b,a,c", 1L, 0L);

    [Fact]
    public async Task A_value_keeps_its_stored_type()
        => (await Eval($"phx:metadata({Doc}, 'pages') instance of xs:integer, phx:metadata({Doc}, 'pages') > 100, phx:metadata({Doc}, 'published') instance of xs:date"))
            .Items.Should().Equal(true, true, true);

    [Fact]
    public async Task A_Clark_key_reaches_the_provider_as_an_expanded_name()
    {
        var (items, provider) = await Eval($"phx:metadata({Doc}, '{{urn:demo}}owner'), phx:metadata({Doc}, 'Q{{urn:demo}}owner'), phx:metadata({Doc}, '{{}}plain')");

        items.Should().Equal("ada", "ada", "no namespace");
        provider.Keys.Should().Equal("Q{urn:demo}owner", "Q{urn:demo}owner", "Q{}plain");
    }

    [Fact]
    public async Task An_xs_QName_key_gives_the_same_value_as_its_expanded_name()
    {
        var (items, provider) = await Eval($"phx:metadata({Doc}, QName('urn:demo', 'd:owner')), phx:metadata({Doc}, xs:QName('plain'))");

        items.Should().Equal("ada", "no namespace");
        provider.Keys.Should().Equal("Q{urn:demo}owner", "Q{}plain");
    }

    [Fact]
    public async Task A_node_key_is_atomized()
        => (await Eval($"let $d := {Doc} return phx:metadata($d, $d/a/@k)")).Items.Should().Equal("draft");

    [Theory]
    [InlineData("phx:metadata('status', parse-xml('<a/>'))")]
    [InlineData("phx:metadata(1, 'x')")]
    [InlineData("phx:metadata('status')")]
    [InlineData("phx:metadata(parse-xml('<a/>'), 1)")]
    [InlineData("phx:metadata(parse-xml('<a/>'), ())")]
    public async Task A_wrong_argument_type_is_XPTY0004(string query)
    {
        var act = () => Eval(query);

        var ex = (await act.Should().ThrowAsync<Exception>()).Which;
        (ex is XQueryRuntimeException rt ? rt.ErrorCode : ex is PhoenixmlDb.XQuery.Functions.XQueryException fx ? fx.ErrorCode : ex.Message)
            .Should().Be("XPTY0004");
    }

    [Fact]
    public async Task An_empty_node_gives_the_empty_sequence()
        => (await Eval("count(phx:metadata((), 'status')), map:size(phx:metadata(()))")).Items.Should().Equal(0L, 0L);

    [Fact]
    public async Task The_map_form_gives_every_key_its_values()
        => (await Eval($"let $m := phx:metadata({Doc}) return (string-join($m?tags, ','), $m?pages instance of xs:integer, map:size($m))"))
            .Items.Should().Equal("b,a,c", true, 6L);
}
