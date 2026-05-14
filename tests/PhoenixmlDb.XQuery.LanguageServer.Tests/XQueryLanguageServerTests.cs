using PhoenixmlDb.XQuery.LanguageServer;
using PhoenixmlDb.XQuery.LanguageServer.Lsp;
using Xunit;

namespace PhoenixmlDb.XQuery.LanguageServer.Tests;

public class XQueryLanguageServerTests
{
    [Fact]
    public void InitializeReturnsServerCapabilities()
    {
        var server = new XQueryLanguageServer();
        var result = server.Initialize(null);
        Assert.NotNull(result.Capabilities);
        Assert.Equal(1, result.Capabilities.TextDocumentSync);
        Assert.True(result.Capabilities.HoverProvider);
        Assert.NotNull(result.Capabilities.CompletionProvider);
    }

    [Fact]
    public void ValidQueryProducesNoDiagnostics()
    {
        var server = new XQueryLanguageServer();
        var buf = new DocumentBuffer("file:///test.xq", 1, "1 + 1");
        var diags = server.ComputeDiagnostics(buf);
        Assert.Empty(diags);
    }

    [Fact]
    public void ParseErrorProducesDiagnostic()
    {
        var server = new XQueryLanguageServer();
        var buf = new DocumentBuffer("file:///test.xq", 1, "1 +");
        var diags = server.ComputeDiagnostics(buf);
        Assert.NotEmpty(diags);
        Assert.Equal(1, diags[0].Severity); // Error
    }

    [Fact]
    public void CompletionReturnsKnownFunctions()
    {
        var server = new XQueryLanguageServer();
        var list = server.Completion(new CompletionParams(
            new TextDocumentIdentifier("file:///test.xq"),
            new Position(0, 0)));
        Assert.False(list.IsIncomplete);
        Assert.Contains(list.Items, i => i.Label == "count");
        Assert.Contains(list.Items, i => i.Label == "string");
    }
}
