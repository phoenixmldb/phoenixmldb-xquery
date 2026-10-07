using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using PhoenixmlDb.XQuery;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Cli.Tests;

/// <summary>
/// An element in no namespace under a default namespace needs <c>xmlns=""</c> in the output. The
/// CLI's ResultSerializer wrote it with the local name alone, which an XmlWriter takes as "in the
/// default namespace", so the printed <c>&lt;z /&gt;</c> read back in the parent's namespace (#105).
/// fn:serialize and the facade were already right; this is the CLI's own writer.
/// </summary>
public sealed class CliUndeclaredDefaultNamespaceTests
{
    private static async Task<string> PrintAsync(string query, string method)
    {
        var store = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store);
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors));
        var items = new List<object?>();
        await foreach (var item in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
            items.Add(item);

        using var writer = new StringWriter();
        new ResultSerializer(store, writer, OutputMethods.Parse(method)!.Value).Serialize(items.Count == 1 ? items[0] : items.ToArray());
        return writer.ToString();
    }

    private static string NamespaceOf(string output, string localName) =>
        XDocument.Parse(output).Descendants().Single(x => x.Name.LocalName == localName).Name.NamespaceName;

    public static TheoryData<string, string> Queries
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var method in new[] { "xml", "adaptive" })
            {
                // parsed: the document, and the element on its own
                data.Add("parse-xml('<r xmlns=\"urn:d\"><z xmlns=\"\"><y/></z><w/></r>')", method);
                data.Add("parse-xml('<r xmlns=\"urn:d\"><z xmlns=\"\"><y/></z><w/></r>')/*", method);
                // constructed, and a parsed child copied into a constructed parent
                data.Add("<r xmlns=\"urn:d\"><z xmlns=\"\"><y/></z><w/></r>", method);
                data.Add("<r xmlns=\"urn:d\">{parse-xml('<z><y/></z>')/*}<w/></r>", method);
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Queries))]
    public async Task An_element_in_no_namespace_under_a_default_namespace_reads_back_in_no_namespace(
        string query, string method)
    {
        var output = await PrintAsync(query, method);
        NamespaceOf(output, "r").Should().Be("urn:d", output);
        NamespaceOf(output, "w").Should().Be("urn:d", output);
        NamespaceOf(output, "z").Should().BeEmpty(output);
        NamespaceOf(output, "y").Should().BeEmpty(output);
        Regex.Count(output, "xmlns=\"\"").Should().Be(1, output);
    }

    [Theory]
    [InlineData("parse-xml('<a><b xmlns=\"\"/></a>')")]
    [InlineData("<a><b/></a>")]
    public async Task No_undeclaration_is_written_where_no_default_namespace_is_in_scope(string query)
    {
        var output = await PrintAsync(query, "xml");
        output.Should().NotContain("xmlns", output);
    }

    [Fact]
    public async Task An_element_that_goes_back_into_the_default_namespace_declares_it_again()
    {
        var output = await PrintAsync(
            "parse-xml('<r xmlns=\"urn:d\"><z xmlns=\"\"><v xmlns=\"urn:d\"/></z></r>')", "xml");
        NamespaceOf(output, "z").Should().BeEmpty(output);
        NamespaceOf(output, "v").Should().Be("urn:d", output);
    }
}
