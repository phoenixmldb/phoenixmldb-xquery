using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using PhoenixmlDb.XQuery;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Cli.Tests;

/// <summary>
/// The CLI's ResultSerializer is a separate serializer from the engine's, so the copy-namespaces
/// no-inherit marker had to be skipped there too. It wrote every declaration and threw
/// "Invalid name character" for the marker whenever the `xquery` tool printed a no-inherit copy.
/// Asserted by reparsing the output rather than by exact text, so formatting cannot mask the result.
/// </summary>
public sealed class CliNoInheritMarkerTests
{
    [Fact]
    public async Task No_inherit_copy_serializes_through_the_cli_serializer()
    {
        var store = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store);
        var compiled = engine.Compile(
            "declare copy-namespaces preserve, no-inherit; <ctor>{<e xmlns:p=\"urn:q\"><p:d/></e>}</ctor>");
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors));
        var items = new List<object?>();
        await foreach (var item in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
            items.Add(item);

        using var writer = new StringWriter();
        new ResultSerializer(store, writer, OutputMethod.Xml).Serialize(items.Count == 1 ? items[0] : items.ToArray());
        var output = writer.ToString();

        output.Should().NotContain("no-inherit");
        var d = XDocument.Parse(output).Descendants().Single(x => x.Name.LocalName == "d");
        d.Name.NamespaceName.Should().Be("urn:q");
    }
}
