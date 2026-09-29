using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// Full-text analysis builds one pipeline whose stages each answer to their own option. It chose
/// between whole Lucene analyzers, so one flag decided two things and several options did nothing:
/// phx:is-stop-word never saw a stop word (#70), the Stemming=true default never stemmed (#29),
/// a phrase matched across a stop-word gap (#30), and `using no stop words`, `using stop words`,
/// `using stemming` and `using case sensitive` never reached analysis.
/// </summary>
public sealed class FullTextAnalysisOptionsTests
{
    private readonly XQueryFacade _facade = new();

    private const string Walrus = "\"the walrus and the carpenter\"";

    [Theory]
    // #70: membership in the stop-word list, not "analysis produced no tokens".
    [InlineData("""phx:is-stop-word("the")""", "true")]
    [InlineData("""phx:is-stop-word("The")""", "true")]
    [InlineData("""phx:is-stop-word("fox")""", "false")]
    [InlineData("""phx:is-stop-word("123")""", "false")]
    [InlineData("""phx:is-stop-word("")""", "false")]
    // #29: the default stems.
    [InlineData("""phx:stem("running")""", "run")]
    [InlineData("""string-join(phx:tokenize("The quick foxes jumped"), "|")""", "quick|fox|jump")]
    [InlineData(Walrus + """ contains text "carpenters" """, "true")]
    [InlineData("\"running shoes\" contains text \"run\"", "true")]
    [InlineData(Walrus + """ contains text "carpenters" using no stemming""", "false")]
    // #30: a phrase needs the same position offsets; a stop word in it stands for one token.
    [InlineData(Walrus + """ contains text "walrus carpenter" """, "false")]
    [InlineData(Walrus + """ contains text "walrus and the carpenter" """, "true")]
    [InlineData(Walrus + """ contains text "walrus or the carpenter" """, "true")]
    [InlineData(Walrus + """ contains text "walrus carpenter" all words""", "true")]
    // The query's own options reach analysis.
    [InlineData(Walrus + """ contains text "the" """, "false")]
    [InlineData(Walrus + """ contains text "the" using no stop words""", "true")]
    [InlineData(Walrus + """ contains text "walrus carpenter" using no stop words""", "false")]
    [InlineData(Walrus + """ contains text "walrus" using stop words ("walrus")""", "false")]
    [InlineData("\"The Walrus\" contains text \"walrus\" using case sensitive", "false")]
    [InlineData("\"The Walrus\" contains text \"Walrus\" using case sensitive", "true")]
    public async Task Each_option_controls_its_own_stage(string query, string expected)
        => (await _facade.EvaluateAsync(query)).Should().Be(expected);
}
