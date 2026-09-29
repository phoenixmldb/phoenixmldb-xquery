using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// Descendant navigation walks the tree iteratively, so tree DEPTH is not capped by the
/// function-call recursion limit (1000). `//x` over a 1,100-deep document raised FOER0000
/// ("may be too deeply nested or contain cycles"), though XDM trees have no cycles (#95).
/// </summary>
public sealed class DeepTreeNavigationTests
{
    private readonly XQueryFacade _facade = new();

    private static string Deep(int depth)
        => $"""parse-xml(string-join((for $i in 1 to {depth} return "<x>", for $i in 1 to {depth} return "</x>")))""";

    [Theory]
    [InlineData(1100, "//x")]
    [InlineData(1100, "/descendant::x")]
    [InlineData(1100, "/descendant-or-self::node()[self::x]")]
    [InlineData(3000, "//x")]
    public async Task A_deep_document_is_navigable(int depth, string path)
        => (await _facade.EvaluateAsync($"count({Deep(depth)}{path})")).Should().Be(depth.ToString(System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public async Task Descendants_stay_in_document_order()
        => (await _facade.EvaluateAsync("""string-join(parse-xml("<a><b><c/><d/></b><e><f/></e></a>")//* ! name(), ",")"""))
            .Should().Be("a,b,c,d,e,f");

    [Fact]
    public async Task Stopping_early_is_fine()
        => (await _facade.EvaluateAsync($"name(({Deep(2000)}//x)[1])")).Should().Be("x");
}
