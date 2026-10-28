using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// fn:doc raises FODC0005 for a string that is not a URI reference. A relative reference whose
/// first segment holds a colon is not one (RFC 3986 §4.2), but System.Uri accepts it, so
/// doc(':/') looked for a file and raised FODC0002 (QT3 K2-SeqDocFunc-14).
/// </summary>
public class DocInvalidUriTests
{
    private static async Task<string> ErrorCode(string query)
    {
        var store = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store);
        var compiled = engine.Compile(query);
        try
        {
            await foreach (var _ in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext())) { }
            return "";
        }
        catch (XQueryRuntimeException ex)
        {
            return ex.ErrorCode;
        }
    }

    [Theory]
    [InlineData("doc(':/')", "FODC0005")]
    [InlineData("doc('x/a:b')", "FODC0002")]
    [InlineData("doc('no-such-file.xml')", "FODC0002")]
    public async Task Invalid_relative_reference_is_FODC0005(string query, string code)
        => (await ErrorCode(query)).Should().Be(code);
}
