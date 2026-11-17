using AwesomeAssertions;
using Xunit;

namespace PhoenixmlDb.Conformance.Tests.FullText;

/// <summary>
/// Runs all of XQFTTS 1.0.4 and writes one line per case to <c>XQFTTS_RESULTS</c>
/// (group, name, outcome, detail). The verdict on the run is the gate's, in
/// <c>scripts/fulltext-conformance.sh</c>, which compares every case with the committed baseline;
/// this test fails only when the suite is present and the run itself did not complete.
/// </summary>
[Trait("Category", "Conformance")]
[Trait("Suite", "XQFTTS")]
public sealed class XqfttsConformanceTests
{
    [Fact]
    public async Task Run_the_full_text_suite()
    {
        var suite = ConformanceSuites.Locate("xqftts", "XQFTTS_TEST_SUITE");
        if (!File.Exists(Path.Combine(suite, "XQFTTSCatalog.xml")))
            Assert.Skip($"XQFTTS not found at {suite}; run scripts/fetch-fulltext-suite.sh");

        var runner = new XqfttsRunner(suite);
        var results = await runner.RunAllAsync(TimeSpan.FromSeconds(30));
        results.Should().HaveCount(runner.CaseCount);

        var target = Environment.GetEnvironmentVariable("XQFTTS_RESULTS");
        if (!string.IsNullOrWhiteSpace(target))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
            await File.WriteAllLinesAsync(target, results.Select(r => $"{r.Group}\t{r.Name}\t{r.Outcome}\t{r.Detail.Replace('\t', ' ')}"));
        }
        TestContext.Current.SendDiagnosticMessage(
            $"XQFTTS 1.0.4: {results.Count(r => r.Outcome == "pass")}/{results.Count} pass, "
            + $"{results.Count(r => r.Outcome == "fail")} fail, {results.Count(r => r.Outcome == "inspect")} inspect");
    }

    [Theory]
    [InlineData("<a x='1' y='2'>t</a>", "<a y=\"2\" x=\"1\">t</a>", true)]
    [InlineData("<a> t  u </a>\n", "<a>t u</a>", true)]
    [InlineData("true", "true", true)]
    [InlineData("<p></p>", "<p/>", true)]
    [InlineData("<p xmlns:e='urn:e'>t</p>", "<p>t</p>", true)]
    [InlineData("<a>t</a>", "<a>u</a>", false)]
    [InlineData("<a/><b/>", "<a/>", false)]
    public void Fragments_compare_after_space_normalization(string expected, string actual, bool same)
        => XqfttsRunner.SameFragment(expected, actual).Should().Be(same);
}
