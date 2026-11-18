using AwesomeAssertions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.FullText;
using Xunit;

#pragma warning disable CA1308 // the test index lowercases, as a case-folded index does

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// Design 07 §5.4: a host's candidate source may rule out search context items before the
/// evaluator sees them. It is asked only with what a match requires (alternatives of terms),
/// and the evaluator still decides every item the source does not rule out.
/// </summary>
public sealed class FullTextCandidateSourceTests
{
    /// <summary>Records each query; rules out an item whose string value lacks every alternative.</summary>
    private sealed class RecordingSource : IFullTextCandidateSource
    {
        public List<FtCandidateQuery> Queries { get; } = [];
        public int RuledOut { get; private set; }

        public IFullTextCandidateFilter? Prepare(FtCandidateQuery query, ContainerId container)
        {
            Queries.Add(query);
            return new Filter(this, query);
        }

        private sealed class Filter(RecordingSource source, FtCandidateQuery query) : IFullTextCandidateFilter
        {
            public bool MayMatch(object? item)
            {
                if (item is not Xdm.Nodes.XdmNode node)
                    return true;
                var words = node.StringValue.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var may = query.Alternatives.Any(terms => terms.All(t => words.Any(w => w.StartsWith(t.ToLowerInvariant()[..Math.Min(3, t.Length)], StringComparison.Ordinal))));
                if (!may) source.RuledOut++;
                return may;
            }
        }
    }

    private static async Task<List<object?>> EvalAsync(string query, IFullTextCandidateSource? source)
    {
        var env = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: env, documentResolver: env) { FullTextCandidates = source };
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors.Select(e => e.Message)));
        using var ctx = engine.CreateContext();
        var items = new List<object?>();
        await foreach (var item in compiled.ExecutionPlan!.ExecuteAsync(ctx)) items.Add(item);
        return items;
    }

    private const string Books = """
        let $b := (<b>the quick fox</b>, <b>a lazy dog</b>, <b>quick dogs run</b>)
        return
        """;

    [Theory]
    [InlineData("\"fox\"", "fox")]
    [InlineData("\"quick fox\"", "quick fox")]
    [InlineData("\"fox\" ftor \"dog\"", "fox|dog")]
    [InlineData("\"quick\" ftand (\"fox\" ftor \"dogs\")", "quick fox|quick dogs")]
    [InlineData("{(\"quick fox\", \"lazy\")} any", "quick fox|lazy")]
    [InlineData("\"quick fox\" any word", "quick|fox")]
    [InlineData("\"quick\" ftand ftnot \"dog\"", "quick")]
    [InlineData("(\"quick\" ftand \"fox\") window 3 words", "quick fox")]
    public async Task Asks_with_the_terms_a_match_requires(string selection, string expected)
    {
        var source = new RecordingSource();
        var withSource = await EvalAsync($"{Books} string-join($b[. contains text {selection}], ',')", source);
        var without = await EvalAsync($"{Books} string-join($b[. contains text {selection}], ',')", null);
        withSource.Should().Equal(without);
        source.Queries.Should().NotBeEmpty();
        string.Join("|", source.Queries[0].Alternatives.Select(a => string.Join(" ", a))).Should().Be(expected);
    }

    [Theory]
    [InlineData("ftnot \"fox\"")]
    [InlineData("\"f.x\" using wildcards")]
    [InlineData("\"fox\" occurs at most 1 times")]
    [InlineData("\"the fox\" using stop words (\"the\")")]
    [InlineData("\"fox\" ftor ftnot \"dog\"")]
    public async Task Is_not_asked_when_a_match_requires_no_term(string selection)
    {
        var source = new RecordingSource();
        await EvalAsync($"{Books} $b[. contains text {selection}]", source);
        source.Queries.Should().BeEmpty();
    }

    [Fact]
    public async Task Items_ruled_out_are_not_evaluated_and_answers_do_not_change()
    {
        var source = new RecordingSource();
        var withSource = await EvalAsync($"{Books} string-join($b[. contains text \"quick\" using stemming], ',')", source);
        withSource.Should().Equal(await EvalAsync($"{Books} string-join($b[. contains text \"quick\" using stemming], ',')", null));
        withSource.Should().Equal("the quick fox,quick dogs run");
        source.RuledOut.Should().Be(1);
        source.Queries[0].Stemming.Should().BeTrue();
    }
}
