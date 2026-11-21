using AwesomeAssertions;
using Xunit;

namespace PhoenixmlDb.Conformance.Tests.Update;

/// <summary>
/// Runs all of XQUTS 1.0.1 and writes one line per case to <c>XQUTS_RESULTS</c>
/// (group, name, outcome, detail, number of states). The verdict on the run is the gate's, in
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
        var results = await runner.RunAllAsync(TimeSpan.FromSeconds(30));
        results.Should().HaveCount(runner.CaseCount);
        var states = runner.StatesPerCase;

        var target = Environment.GetEnvironmentVariable("XQUTS_RESULTS");
        if (!string.IsNullOrWhiteSpace(target))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
            await File.WriteAllLinesAsync(target, results.Select(r =>
                $"{r.Group}\t{r.Name}\t{r.Outcome}\t{r.Detail.Replace('\t', ' ')}\t{states[r.Name]}"));
        }
        TestContext.Current.SendDiagnosticMessage(
            $"XQUTS 1.0.1: {results.Count(r => r.Outcome == "pass")}/{results.Count} pass, "
            + $"{results.Count(r => r.Outcome == "fail")} fail, {results.Count(r => r.Outcome == "inspect")} inspect; "
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
