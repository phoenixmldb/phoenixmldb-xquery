using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using PhoenixmlDb.XQuery;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Cli.Tests;

/// <summary>
/// The phx extension functions need no database and no prolog: the library predeclares phx, so the CLI's
/// query path (an engine over an XdmDocumentStore, serialized by the CLI's ResultSerializer) reaches them as is
/// (namespace-consolidation design, N1).
/// </summary>
public sealed class CliPhxFunctionTests
{
    [Fact]
    public async Task A_phx_function_runs_with_no_prolog()
    {
        var store = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store);
        var compiled = engine.Compile("phx:tokenize('alpha beta')");
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors));
        var items = new List<object?>();
        await foreach (var item in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
            items.Add(item);

        using var writer = new StringWriter();
        new ResultSerializer(store, writer, OutputMethod.Adaptive).Serialize(items.ToArray());

        writer.ToString().Should().Contain("alpha").And.Contain("beta");
    }
}
