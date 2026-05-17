using PhoenixmlDb.XQuery.LanguageServer;
using PhoenixmlDb.XQuery.LanguageServer.Handlers;
using PhoenixmlDb.XQuery.LanguageServer.Lsp;
using Xunit;

namespace PhoenixmlDb.XQuery.LanguageServer.Tests;

public class ReferencesTests
{
    [Fact]
    public void FindsAllVariableOccurrences()
    {
        var xq = "declare variable $x := 1; $x + $x";
        var buf = new DocumentBuffer("file:///t.xq", 1, xq);
        // Caret on first use of $x after the semicolon (offset 27)
        var refs = ReferencesHandler.Handle(buf, new Position(0, 27));
        Assert.Equal(3, refs.Length);
    }

    [Fact]
    public void FindsFunctionCallSitesAndDecl()
    {
        var xq = "declare function local:foo() { 1 }; local:foo() + local:foo()";
        var buf = new DocumentBuffer("file:///t.xq", 1, xq);
        // Caret on the first call site
        var refs = ReferencesHandler.Handle(buf, new Position(0, 42));
        // Decl + 2 call sites = 3
        Assert.Equal(3, refs.Length);
    }

    [Fact]
    public void UnknownTokenReturnsEmpty()
    {
        var buf = new DocumentBuffer("file:///t.xq", 1, "1 + 2");
        // Caret on whitespace, no token
        var refs = ReferencesHandler.Handle(buf, new Position(0, 1));
        Assert.Empty(refs);
    }

    [Fact]
    public void RespectsWordBoundaries()
    {
        var xq = "declare variable $x := 1; declare variable $xy := 2; $x + $xy";
        var buf = new DocumentBuffer("file:///t.xq", 1, xq);
        // Caret on $x (the short one) at offset 54
        var refs = ReferencesHandler.Handle(buf, new Position(0, 54));
        // Should match $x decl + $x use, NOT $xy
        Assert.Equal(2, refs.Length);
    }
}
