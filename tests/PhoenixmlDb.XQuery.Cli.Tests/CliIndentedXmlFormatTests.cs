using System.Collections.Generic;
using System.IO;
using AwesomeAssertions;
using PhoenixmlDb.XQuery;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Cli.Tests;

/// <summary>
/// The CLI writes nodes indented with XmlWriter, which writes an empty element as <c>&lt;e /&gt;</c>.
/// The XML output method writes <c>&lt;e/&gt;</c>, as serialize() and Saxon do, and as the XSLT
/// engine does since xslt#559. Only the empty tags change: " />" in a comment or a processing
/// instruction is content and stays.
/// </summary>
public sealed class CliIndentedXmlFormatTests
{
    private static async Task<string> Serialize(string query, OutputMethod method)
    {
        var store = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store);
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors));
        var items = new List<object?>();
        await foreach (var item in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
            items.Add(item);

        using var writer = new StringWriter();
        new ResultSerializer(store, writer, method).Serialize(items.Count == 1 ? items[0] : items.ToArray());
        return writer.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("xml")]
    [InlineData("adaptive")]
    public async Task An_empty_element_has_no_space_before_the_slash(string method)
    {
        var output = await Serialize("""document{<root><missing/><a x="1"/><b>t</b></root>}""", OutputMethods.Parse(method)!.Value);

        output.Should().EndWith("<root>\n  <missing/>\n  <a x=\"1\"/>\n  <b>t</b>\n</root>").And.NotContain(" />");
    }

    [Fact]
    public async Task An_element_with_no_declaration_is_written_the_same_way()
        => (await Serialize("<root><e/></root>", OutputMethod.Xml)).Should().Be("<root>\n  <e/>\n</root>");

    [Fact]
    public async Task A_comment_and_a_processing_instruction_keep_their_content()
    {
        var output = await Serialize("<root><!-- a /> b --><?p x /> ?><e/></root>", OutputMethod.Xml);

        output.Should().Be("<root>\n  <!-- a /> b -->\n  <?p x /> ?>\n  <e/>\n</root>");
    }

    [Fact]
    public async Task Output_larger_than_the_buffer_is_written_whole()
    {
        var output = await Serialize("<r>{for $i in 1 to 20000 return <e i='{$i}'/>}</r>", OutputMethod.Xml);

        output.Should().StartWith("<r>\n  <e i=\"1\"/>\n").And.EndWith("  <e i=\"20000\"/>\n</r>").And.NotContain(" />");
        output.Split('\n').Length.Should().Be(20002);
    }
}
