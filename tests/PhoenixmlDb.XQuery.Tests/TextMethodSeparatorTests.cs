using AwesomeAssertions;
using PhoenixmlDb.XQuery;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// What the text output method writes between the items of a sequence. Serialization 3.1 §8.1.15:
/// "The effect of the item-separator serialization parameter is described in [2 Sequence
/// Normalization]." §2 step 3: "If the item-separator serialization parameter is absent, then for
/// each subsequence of adjacent strings in S2, copy a single string to the new sequence equal to
/// the values of the strings in the subsequence concatenated in order, each separated by a single
/// space. Copy all other items to the new sequence." So a space goes only between two adjacent
/// atomic values. The text method wrote one between any two items, nodes too:
/// <c>(&lt;a&gt;x&lt;/a&gt;, &lt;b&gt;y&lt;/b&gt;)</c> was "x y" for "xy" (3.2.0 too).
/// </summary>
public sealed class TextMethodSeparatorTests
{
    private readonly XQueryFacade _facade = new();

    private Task<string> Text(string sequence, string? itemSeparator = null)
        => _facade.EvaluateAsync(itemSeparator is null
            ? $"serialize({sequence}, map {{ 'method': 'text' }})"
            : $"serialize({sequence}, map {{ 'method': 'text', 'item-separator': '{itemSeparator}' }})");

    [Theory]
    [InlineData("(<a>x</a>, <b>y</b>)", "xy")]                          // two nodes
    [InlineData("(<a>x</a>, 'q')", "xq")]                               // a node and a string
    [InlineData("('q', <a>x</a>)", "qx")]                               // a string and a node
    [InlineData("(1, 2)", "1 2")]                                       // two atomic values: unchanged
    [InlineData("(<a>x</a>, 1, 2, <b>y</b>)", "x1 2y")]                 // only the atomic values are joined
    [InlineData("(comment { 'c' }, <a>x</a>)", "x")]                    // a comment writes nothing
    [InlineData("(processing-instruction p { 'q' }, text { 't' })", "t")]
    [InlineData("document { <a>x</a>, <b>y</b> }", "xy")]               // a document node
    [InlineData("(document { <a>x</a> }, document { <b>y</b> })", "xy")]
    [InlineData("[<a>x</a>, <b>y</b>]", "xy")]                          // an array is flattened first (§2 step 1)
    [InlineData("[1, 2, 3, 4, 5]", "1 2 3 4 5")]                        // QT3 Serialization-text-19
    [InlineData("([<a>x</a>, 1], 2, <b>y</b>)", "x1 2y")]
    public async Task Without_an_item_separator_only_adjacent_atomic_values_are_separated(string sequence, string expected)
        => (await Text(sequence)).Should().Be(expected);

    // §2 step 3: "Otherwise, copy each item in S2 to the new sequence, inserting between each pair of
    // items a string whose value is equal to the value of the item-separator parameter." Between
    // nodes too.
    [Theory]
    [InlineData("(<a>x</a>, <b>y</b>)", "x|y")]
    [InlineData("(1, 2)", "1|2")]
    [InlineData("(<a>x</a>, 'q', 3)", "x|q|3")]
    [InlineData("[<a>x</a>, <b>y</b>]", "x|y")]
    public async Task With_an_item_separator_it_is_written_between_each_pair_of_items(string sequence, string expected)
        => (await Text(sequence, "|")).Should().Be(expected);

    // Guards. The result of a query whose prolog declares the text method had the rule already (it
    // is serialized by another path); the xml method had it; the facade's adaptive result does not
    // change (Serialization 3.1 §10: the adaptive method does no sequence normalization).
    [Fact]
    public async Task A_query_whose_method_is_text_writes_two_elements_as_their_text()
        => (await _facade.EvaluateAsync(
                "declare namespace output = 'http://www.w3.org/2010/xslt-xquery-serialization'; "
                + "declare option output:method 'text'; (<a>x</a>, <b>y</b>)"))
            .Should().Be("xy");

    [Fact]
    public async Task The_xml_method_does_not_change()
        => (await _facade.EvaluateAsync("serialize((<a>x</a>, 'q', 'r', <b>y</b>), map { 'method': 'xml' })"))
            .Should().Be("<a>x</a>q r<b>y</b>");

    [Fact]
    public async Task The_adaptive_result_of_the_facade_does_not_change()
        => (await _facade.EvaluateAsync("(1, <a>x</a>, 2)")).Should().Be("1<a>x</a>2");
}
