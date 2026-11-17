using System.Xml.Linq;
using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.FullText;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// Design 07 §5.2: stop word lists and thesauri named by URI, and the defaults, come from the
/// host's <see cref="IFullTextResourceResolver"/> (Full Text 3.0 §3.4.3, §3.4.7).
/// </summary>
public sealed class FullTextResourcesTests
{
    private const string Text = "\"the quick brown fox jumps over the lazy dog\"";

    private static readonly FullTextThesaurus Thesaurus = FullTextThesaurus.Load(XDocument.Parse("""
        <thesaurus xmlns="http://www.w3.org/2007/xqftts/thesaurus">
          <entry><term>animal</term>
            <synonym><term>mammal</term><relationship>NT</relationship>
              <synonym><term>dog</term><relationship>NT</relationship></synonym>
            </synonym>
          </entry>
          <entry><term>hound</term><synonym><term>dog</term><relationship>USE</relationship></synonym></entry>
        </thesaurus>
        """));

    private static async Task<object?> EvalAsync(string query, IFullTextResourceResolver? resources)
    {
        var env = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: env, documentResolver: env) { FullTextResources = resources };
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors.Select(e => e.Message)));
        using var ctx = engine.CreateContext();
        ctx.StaticBaseUri = "http://example.com/base/";
        var items = new List<object?>();
        await foreach (var item in compiled.ExecutionPlan!.ExecuteAsync(ctx)) items.Add(item);
        return items.Single();
    }

    private static FullTextResources Resources() => new FullTextResources()
        .AddStopWords("http://example.com/base/stop.txt", FullTextResources.ParseStopWords("over\nthe\n"))
        .AddStopWords("http://example.com/fox.xml", FullTextResources.ParseStopWords("<stop-words><word>fox</word></stop-words>"))
        .AddThesaurus("http://example.com/t.xml", Thesaurus);

    [Theory]
    // A relative URI resolves against the static base URI. A query stop word matches any one
    // token: "jumps the the lazy" fits "jumps over the lazy" only when "the" is a stop word.
    [InlineData(Text + " contains text \"jumps the the lazy\" using stop words at \"stop.txt\"", true)]
    [InlineData(Text + " contains text \"jumps the the lazy\"", false)]
    [InlineData(Text + " contains text \"quick fox fox jumps\" using stop words at \"stop.txt\" union at \"http://example.com/fox.xml\"", true)]
    [InlineData(Text + " contains text \"quick fox fox jumps\" using stop words at \"stop.txt\"", false)]
    [InlineData(Text + " contains text \"jumps the the lazy\" using stop words at \"stop.txt\" except (\"the\")", false)]
    // Thesaurus: hierarchical levels, other relationships one level, the phrase itself always.
    [InlineData(Text + " contains text \"animal\" using thesaurus at \"http://example.com/t.xml\" relationship \"NT\"", true)]
    [InlineData(Text + " contains text \"animal\" using thesaurus at \"http://example.com/t.xml\" relationship \"NT\" exactly 1 levels", false)]
    [InlineData(Text + " contains text \"animal\" using thesaurus at \"http://example.com/t.xml\" relationship \"NT\" exactly 2 levels", true)]
    [InlineData(Text + " contains text \"hound\" using thesaurus at \"http://example.com/t.xml\" relationship \"USE\"", true)]
    [InlineData(Text + " contains text \"hound\" using thesaurus at \"http://example.com/t.xml\" relationship \"NT\"", false)]
    [InlineData(Text + " contains text \"fox\" using thesaurus at \"http://example.com/t.xml\"", true)]
    public async Task Resolves_named_resources(string query, bool expected)
        => (await EvalAsync(query, Resources())).Should().Be(expected);

    [Fact]
    public async Task Defaults_come_from_the_resolver()
    {
        var resources = Resources();
        resources.StopWordsByDefault = ["cat"];
        resources.ThesaurusByDefault = Thesaurus;
        (await EvalAsync(Text + " contains text \"brown cat jumps\" using stop words default", resources)).Should().Be(true);
        (await EvalAsync(Text + " contains text \"hound\" using thesaurus default", resources)).Should().Be(true);
        // Without a resolver: the built-in English list, and no thesaurus entries.
        (await EvalAsync(Text + " contains text \"brown the jumps\" using stop words default", null)).Should().Be(true);
        (await EvalAsync(Text + " contains text \"hound\" using thesaurus default", null)).Should().Be(false);
    }

    [Theory]
    [InlineData(Text + " contains text \"fox\" using stop words at \"missing.txt\"", "FTST0008")]
    [InlineData(Text + " contains text \"fox\" using thesaurus at \"http://example.com/missing.xml\"", "FTST0018")]
    public async Task A_resource_that_is_not_known_is_an_error(string query, string code)
    {
        var act = () => EvalAsync(query, Resources());
        (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be(code);
    }

    [Fact]
    public void Loads_the_native_thesaurus_format()
    {
        var native = FullTextThesaurus.Load(XDocument.Parse("""
            <thesaurus xmlns="https://schemas.phoenixml.dev/2026/ft" language="en">
              <entry term="whale"><related term="orca" relationship="NT"/><related term="wale" relationship="UF"/></entry>
            </thesaurus>
            """));
        native.Language.Should().Be("en");
        native.Expand("Whale", "NT").Should().Equal("orca");
        native.Expand("whale", null).Should().BeEquivalentTo(["orca", "wale"]);
    }
}
