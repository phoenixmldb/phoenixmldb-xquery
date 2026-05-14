using PhoenixmlDb.XQuery.LanguageServer;
using PhoenixmlDb.XQuery.LanguageServer.Handlers;
using PhoenixmlDb.XQuery.LanguageServer.Lsp;
using Xunit;

namespace PhoenixmlDb.XQuery.LanguageServer.Tests;

public class DocumentSymbolTests
{
    [Fact]
    public void EmptyBufferProducesEmptyList()
    {
        var buf = new DocumentBuffer("file:///x.xq", 1, "");
        var symbols = DocumentSymbolHandler.Handle(buf);
        Assert.Empty(symbols);
    }

    [Fact]
    public void FunctionDeclarationProducesFunctionSymbol()
    {
        // Need a body expression so the parser wraps in ModuleExpression.
        var buf = new DocumentBuffer("file:///x.xq", 1,
            "declare function local:foo() { 1 }; local:foo()");
        var symbols = DocumentSymbolHandler.Handle(buf);
        var sym = Assert.Single(symbols);
        Assert.Equal("foo", sym.Name);
        Assert.Equal(SymbolKind.Function, sym.Kind);
    }

    [Fact]
    public void VariableDeclarationProducesVariableSymbol()
    {
        var buf = new DocumentBuffer("file:///x.xq", 1,
            "declare variable $x := 1; $x");
        var symbols = DocumentSymbolHandler.Handle(buf);
        var sym = Assert.Single(symbols);
        Assert.Equal("$x", sym.Name);
        Assert.Equal(SymbolKind.Variable, sym.Kind);
    }

    [Fact]
    public void MultipleDeclarationsAllListed()
    {
        var buf = new DocumentBuffer("file:///x.xq", 1,
            "declare variable $x := 1; declare function local:f() { 2 }; $x + local:f()");
        var symbols = DocumentSymbolHandler.Handle(buf);
        Assert.Equal(2, symbols.Length);
    }

    [Fact]
    public void ParseFailureProducesEmptyList()
    {
        var buf = new DocumentBuffer("file:///x.xq", 1, "garbage(((((");
        var symbols = DocumentSymbolHandler.Handle(buf);
        Assert.Empty(symbols);
    }
}
