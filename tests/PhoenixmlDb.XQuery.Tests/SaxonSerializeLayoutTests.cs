using AwesomeAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// fn:serialize with <c>phx:indent-style="saxon-he-10"</c> (xslt#499): the result is the one Saxon-HE
/// 10.9's serialize() gives, to the byte. The cases are generated (<c>scripts/saxon-serialize-cases.py</c>):
/// random sequences of trees, serialized with the xml or xhtml method and random parameters. Saxon
/// ignores the phx parameter, so its result for the same query is the expected one.
/// </summary>
public class SaxonSerializeLayoutTests
{
    private static readonly string Dir = Path.Combine(AppContext.BaseDirectory, "SaxonSerialize");

    public static TheoryData<string> Cases()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.EnumerateFiles(Dir, "case*.xq").Order(StringComparer.Ordinal))
            data.Add(Path.GetFileNameWithoutExtension(file));
        return data;
    }

    [Fact]
    public void There_are_cases()
        => Cases().Count.Should().Be(150);

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task The_result_is_the_one_Saxon_HE_10_gives(string name)
    {
        var query = await File.ReadAllTextAsync(Path.Combine(Dir, name + ".xq"));
        var expected = await File.ReadAllTextAsync(Path.Combine(Dir, name + ".txt"));
        (await new XQueryFacade().EvaluateAsync(query, "<x/>")).Should().Be(expected);
    }

    private const string Phx = "QName('https://schemas.phoenixml.dev/2026/functions', 'indent-style')";
    private const string Tree = "<r a='1'><b/><c>t</c></r>";
    private const string Output = "declare namespace output = 'http://www.w3.org/2010/xslt-xquery-serialization';\n";

    private static Task<string> Run(string query) => new XQueryFacade().EvaluateAsync(query, "<x/>");

    [Fact]
    public async Task Without_the_parameter_the_layout_is_the_default_one()
        => (await Run($"serialize({Tree}, map {{ 'method': 'xml', 'indent': true() }})"))
            .Should().Be("<r a=\"1\">\n  <b/>\n  <c>t</c>\n</r>");

    [Fact]
    public async Task Without_indent_the_parameter_changes_nothing()
        => (await Run($"serialize({Tree}, map {{ 'method': 'xml', 'indent': false(), {Phx}: 'saxon-he-10' }})"))
            .Should().Be("<r a=\"1\"><b/><c>t</c></r>");

    [Fact]
    public async Task In_a_serialization_parameters_element()
        => (await Run(Output + $"serialize({Tree}, <output:serialization-parameters xmlns:phx='https://schemas.phoenixml.dev/2026/functions'>"
                + "<output:method value='xml'/><output:indent value='yes'/><phx:indent-style value='saxon-he-10'/></output:serialization-parameters>)"))
            .Should().Be("\n<r a=\"1\">\n   <b/>\n   <c>t</c>\n</r>\n");

    [Theory]
    [InlineData("map { 'method': 'xml', 'indent': true(), " + Phx + ": 'saxon-he-9' }")]
    [InlineData("<output:serialization-parameters xmlns:phx='https://schemas.phoenixml.dev/2026/functions'><output:indent value='yes'/><phx:indent-style value='x'/></output:serialization-parameters>")]
    public async Task Another_value_is_SEPM0016(string parameters)
    {
        var act = () => Run(Output + $"serialize(<r/>, {parameters})");
        (await act.Should().ThrowAsync<PhoenixmlDb.XQuery.Execution.XQueryRuntimeException>()).Which.ErrorCode.Should().Be("SEPM0016");
    }
}
