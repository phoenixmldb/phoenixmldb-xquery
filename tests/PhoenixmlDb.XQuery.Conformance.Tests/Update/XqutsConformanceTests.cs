using AwesomeAssertions;
using Xunit;

namespace PhoenixmlDb.Conformance.Tests.Update;

/// <summary>
/// Runs all of XQUTS 1.0.1 and writes one line per case to <c>XQUTS_RESULTS</c>
/// (group, name, outcome, detail, number of states, outcome under the suite's own expected results,
/// "xquf30" when the case has an XQUF 3.0 override). The outcome is the one with the overrides
/// (<see cref="Xquf30Overrides"/>). The verdict on the run is the gate's, in
/// <c>scripts/xquts-conformance.sh</c>, which compares every case with the committed baseline;
/// this test fails only when the suite is present and the run itself did not complete.
/// </summary>
[Trait("Category", "Conformance")]
[Trait("Suite", "XQUTS")]
public sealed class XqutsConformanceTests
{
    [Fact]
    public async Task Run_the_update_suite()
    {
        var suite = ConformanceSuites.Locate("xquts", "XQUTS_TEST_SUITE");
        if (!File.Exists(Path.Combine(suite, "XQUTSCatalog.xml")))
            Assert.Skip($"XQUTS not found at {suite}; run scripts/fetch-xquts-suite.sh");

        var runner = new XqutsRunner(suite);
        // An override must name a state of the catalog: one that names none overrides nothing.
        Xquf30Overrides.States.Keys.Except(runner.StateQueryNames).Should().BeEmpty("every XQUF 3.0 override names a state of the catalog");
        var results = await runner.RunAllAsync(TimeSpan.FromSeconds(30));
        results.Should().HaveCount(runner.CaseCount);
        var states = runner.StatesPerCase;

        var target = Environment.GetEnvironmentVariable("XQUTS_RESULTS");
        if (!string.IsNullOrWhiteSpace(target))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
            await File.WriteAllLinesAsync(target, results.Select(r =>
                $"{r.Group}\t{r.Name}\t{r.Outcome}\t{r.Detail.Replace('\t', ' ')}\t{states[r.Name]}\t{r.SuiteOutcome}\t{(r.Overridden ? "xquf30" : "")}"));
        }
        TestContext.Current.SendDiagnosticMessage(
            $"XQUTS 1.0.1: {results.Count(r => r.SuiteOutcome == "pass")}/{results.Count} pass under the suite's expected results, "
            + $"{results.Count(r => r.Outcome == "pass")}/{results.Count} with the XQUF 3.0 overrides ("
            + $"{results.Count(r => r.Outcome == "fail")} fail, {results.Count(r => r.Outcome == "inspect")} inspect); "
            + $"{runner.StateCount} states");
    }

    [Theory]
    [InlineData("declare variable $input-context external;", "input-context", "declare variable $input-context external := D;")]
    [InlineData("declare variable $books as document-node(element(BOOKLIST)) external;", "books",
        "declare variable $books as document-node(element(BOOKLIST)) external := D;")]
    [InlineData("declare variable $input-URI-2 external; declare variable $input-URI external;", "input-URI",
        "declare variable $input-URI-2 external; declare variable $input-URI external := D;")]
    [InlineData("declare variable $x := 1; $x", "x", "declare variable $x := 1; $x")]
    public void Inputs_bind_through_the_external_declarations_default(string query, string variable, string expected)
        => XqutsRunner.WithDefault(query, variable, "D").Should().Be(expected);

    [Fact]
    public void Every_XQUF_3_0_override_cites_its_rule()
    {
        Xquf30Overrides.States.Should().NotBeEmpty();
        foreach (var (state, expected) in Xquf30Overrides.States)
        {
            expected.Citation.Should().StartWith("XQUF 3.0 §", state);
            expected.Citation.Should().Contain("\"", $"{state}: the citation quotes the rule");
        }
    }

    [Theory]
    [InlineData("XUTY0013", null, "XUTY0013", null, "pass")]
    [InlineData("XUTY0013", null, "XUST0001", null, "fail")]
    [InlineData("XUTY0013", null, null, "", "fail")]
    [InlineData(null, "", null, "", "pass")]
    [InlineData(null, "", null, "<a/>", "fail")]
    [InlineData(null, null, null, "<a/>", "pass")]
    [InlineData(null, "", "XUST0001", null, "fail")]
    public async Task A_state_with_an_override_is_judged_by_its_XQUF_3_0_result(
        string? expectedError, string? expectedResult, string? raised, string? result, string outcome)
    {
        var seen = new XqutsRunner.Observation
        {
            Code = raised,
            Message = raised is null ? null : "message",
            Items = raised is null ? [] : null,
            Actual = result,
        };
        var (got, _) = await XqutsRunner.Xquf30VerdictAsync(new Xquf30Expectation(expectedError, expectedResult, "XQUF 3.0 §0"), seen, new PhoenixmlDb.XQuery.Execution.QueryEngine());
        got.Should().Be(outcome);
    }

    [Fact]
    public async Task A_state_that_gave_neither_a_result_nor_an_error_fails_its_override()
    {
        var (got, _) = await XqutsRunner.Xquf30VerdictAsync(new Xquf30Expectation(null, null, "XQUF 3.0 §0"), new XqutsRunner.Observation(), new PhoenixmlDb.XQuery.Execution.QueryEngine());
        got.Should().Be("fail");
    }

    [Theory]
    [InlineData("<?xml version=\"1.0\"?>\n<works/>", "\n<works/>")]
    [InlineData("<?xml version=\"1.0\" encoding=\"UTF-8\"?><totals/>", "<totals/>")]
    [InlineData("<works/><?xml-stylesheet href='x'?>", "<works/><?xml-stylesheet href='x'?>")]
    public void A_leading_xml_declaration_is_not_compared(string expected, string compared)
        => XqutsRunner.WithoutXmlDeclaration(expected).Should().Be(compared);

    [Theory]
    [InlineData("iso-8859-1", "<?xml version='1.0' encoding=\"ISO-8859-1\"?>")]
    [InlineData("utf-8", "<?xml version=\"1.0\" encoding=\"UTF-8\"?>")]
    [InlineData("utf-8", "")]
    public void Sources_decode_as_their_declaration_says(string encoding, string declaration)
    {
        var text = declaration + "<n>Åke Jógvan Øyvind</n>";
        XqutsRunner.ReadXml(System.Text.Encoding.GetEncoding(encoding).GetBytes(text)).Should().Be(text);
    }
}
