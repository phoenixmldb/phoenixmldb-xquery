using AwesomeAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// XQuery and XPath Full Text 3.0 grammar (productions 199-235) and the AllMatches semantics of
/// §4. Before 3.2 the grammar was a subset: match options only after the whole selection,
/// positional filters parsed but not composable, and no occurs, weight, score variables,
/// without content or declare ft-option; 178 of the 685 W3C full-text cases did not parse.
/// </summary>
public sealed class FullTextSpecTests
{
    private readonly XQueryFacade _facade = new();

    private const string Text = "\"the quick brown fox jumps over the lazy dog\"";

    [Theory]
    // Match options belong to the primary they follow ([205]).
    [InlineData("\"running fast\" contains text (\"run\" using stemming) ftand \"fast\"", "true")]
    [InlineData("\"running fast\" contains text \"fast\" ftand \"run\" using stemming", "true")]
    [InlineData("\"running fast\" contains text \"run\" ftand \"fast\" using stemming", "false")]
    // FTTimes [211].
    [InlineData(Text + " contains text \"the\" occurs exactly 2 times", "true")]
    [InlineData(Text + " contains text \"the\" occurs at least 3 times", "false")]
    [InlineData(Text + " contains text \"the\" occurs at most 1 times", "false")]
    [InlineData(Text + " contains text \"fox\" occurs from 1 to 2 times", "true")]
    // Positional filters [213]; FTOrder applies first (§3.6).
    [InlineData(Text + " contains text \"fox\" ftand \"quick\" ordered", "false")]
    [InlineData(Text + " contains text \"quick\" ftand \"fox\" window 3 words", "true")]
    [InlineData(Text + " contains text \"quick\" ftand \"dog\" window 3 words", "false")]
    [InlineData(Text + " contains text (\"fox\" ftand \"quick\") window 3 words ordered", "false")]
    [InlineData(Text + " contains text \"quick\" ftand \"jumps\" distance exactly 2 words", "true")]
    [InlineData(Text + " contains text \"quick\" ftand \"jumps\" distance at most 1 words", "false")]
    [InlineData(Text + " contains text \"the\" at start", "true")]
    [InlineData(Text + " contains text \"dog\" at end", "true")]
    [InlineData(Text + " contains text \"lazy dog\" entire content", "false")]
    [InlineData("<p>One. Two three.</p> contains text \"one\" ftand \"two\" same sentence", "false")]
    [InlineData("<p>One. Two three.</p> contains text \"two\" ftand \"three\" same sentence", "true")]
    [InlineData("declare boundary-space preserve; <d><p>one</p> <p>two</p></d> contains text \"one\" ftand \"two\" different paragraph", "true")]
    // A tag splits a word unless letters touch it on both sides (highlighting markup).
    [InlineData("<p>paper <b>p</b>rototypes</p> contains text \"prototypes\"", "true")]
    // ftnot, not in.
    [InlineData(Text + " contains text \"fox\" ftand ftnot \"cat\"", "true")]
    [InlineData(Text + " contains text \"fox\" ftand ftnot \"dog\"", "false")]
    [InlineData(Text + " contains text \"fox\" not in \"brown fox\"", "false")]
    // Diacritics insensitive by default (§3.4.6); wildcards (§3.4.2).
    [InlineData("\"un café noir\" contains text \"cafe\"", "true")]
    [InlineData("\"un café noir\" contains text \"cafe\" using diacritics sensitive", "false")]
    [InlineData(Text + " contains text \"j.*s\" using wildcards", "true")]
    [InlineData(Text + " contains text \"j.*s\"", "false")]
    // without content [235]: the ignored nodes are pruned from the search context.
    [InlineData("let $d := <d><a>fox</a><b>dog</b></d> return $d contains text \"fox\" without content $d/a", "false")]
    // declare ft-option [26] sets the module's default options.
    [InlineData("declare ft-option using stemming; \"running\" contains text \"run\"", "true")]
    public async Task Evaluates_by_the_specification(string query, string expected)
        => (await _facade.EvaluateAsync(query)).Should().Be(expected);

    [Theory]
    [InlineData("let score $s := " + Text + " contains text \"fox\" return $s > 0", "true")]
    [InlineData("let score $s := " + Text + " contains text \"cat\" return string($s)", "0")]
    [InlineData("string-join(for $w score $s in (<a>fox</a>, <a>cat</a>)[. contains text \"fox\"] return string($s > 0), ',')", "true")]
    public async Task Binds_score_variables(string query, string expected)
        => (await _facade.EvaluateAsync(query)).Should().Be(expected);

    [Theory]
    [InlineData("for $t score $t in (1, 2) return $t", "XQST0089")]
    [InlineData(Text + " contains text \"fox\" weight {2000}", "FTDY0016")]
    [InlineData(Text + " contains text {47}", "XPTY0004")]
    [InlineData(Text + " contains text \"fox\" using language \"123\"", "FTST0013")]
    [InlineData(Text + " contains text \"f.{1\" using wildcards", "FTDY0020")]
    [InlineData(Text + " contains text \"fox\" using stemming using no stemming", "FTST0019")]
    [InlineData(Text + " contains text \"fox\" using stop words at \"http://example.com/none\"", "FTST0008")]
    [InlineData(Text + " contains text (\"fox\" ftand ftnot \"dog\") not in \"lazy\"", "FTDY0017")]
    public async Task Raises_the_specification_errors(string query, string code)
    {
        var act = () => _facade.EvaluateAsync(query);
        var error = (await act.Should().ThrowAsync<Exception>()).Which;
        ((error as PhoenixmlDb.XQuery.Execution.XQueryRuntimeException)?.ErrorCode ?? error.Message).Should().Contain(code);
    }
}
