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
/// Copied descendants carry the in-scope bindings they inherit from their source ancestors. The CLI's
/// ResultSerializer wrote every declaration an element holds, so it must skip the ones already in
/// scope or each copied descendant would repeat them.
/// </summary>
public sealed class CliCopiedDescendantNamespaceTests
{
    [Fact]
    public async Task Copied_descendants_print_each_inherited_declaration_once()
    {
        var store = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store);
        var compiled = engine.Compile(
            "<ctor>{parse-xml('<book xmlns=\"urn:b\"><e><p:d xmlns:p=\"urn:q\"><f/></p:d></e></book>')/*}</ctor>");
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors));
        var items = new List<object?>();
        await foreach (var item in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
            items.Add(item);

        using var writer = new StringWriter();
        new ResultSerializer(store, writer, OutputMethod.Xml).Serialize(items.Count == 1 ? items[0] : items.ToArray());
        var output = writer.ToString();

        Regex.Count(output, "xmlns=\"urn:b\"").Should().Be(1, output);
        Regex.Count(output, "xmlns:p=").Should().Be(1, output);
        var parsed = XDocument.Parse(output);
        parsed.Descendants().Single(x => x.Name.LocalName == "d").Name.NamespaceName.Should().Be("urn:q");
        parsed.Descendants().Single(x => x.Name.LocalName == "f").Name.NamespaceName.Should().Be("urn:b");
    }
}
