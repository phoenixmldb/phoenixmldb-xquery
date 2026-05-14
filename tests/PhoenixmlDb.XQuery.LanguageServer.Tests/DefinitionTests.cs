using PhoenixmlDb.XQuery.LanguageServer;
using PhoenixmlDb.XQuery.LanguageServer.Handlers;
using PhoenixmlDb.XQuery.LanguageServer.Lsp;
using Xunit;

namespace PhoenixmlDb.XQuery.LanguageServer.Tests;

public class DefinitionTests
{
    [Fact]
    public void FindsVariableDeclaration()
    {
        var xq = "declare variable $x := 1; $x + 1";
        var buf = new DocumentBuffer("file:///t.xq", 1, xq);
        // Caret on the use of $x at offset ~ 27 (the '$x' after the semicolon)
        var loc = DefinitionHandler.Handle(buf, new Position(0, 27));
        Assert.NotNull(loc);
        // The declaration starts at "$x" at offset 17 → line 0, character 17
        Assert.Equal(17, loc!.Range.Start.Character);
    }

    [Fact]
    public void FindsFunctionDeclaration()
    {
        var xq = "declare function local:foo() { 1 }; local:foo()";
        var buf = new DocumentBuffer("file:///t.xq", 1, xq);
        // Caret on the call site of `foo` (after "local:") around offset 42
        var loc = DefinitionHandler.Handle(buf, new Position(0, 42));
        Assert.NotNull(loc);
        // Declaration's `foo` name token starts somewhere on line 0
        Assert.Equal(0, loc!.Range.Start.Line);
    }

    [Fact]
    public void UnknownTokenReturnsNull()
    {
        var buf = new DocumentBuffer("file:///t.xq", 1, "$undefined + 1");
        var loc = DefinitionHandler.Handle(buf, new Position(0, 5));
        Assert.Null(loc);
    }

    [Fact]
    public void FindsForBinding()
    {
        var xq = "for $i in 1 to 3 return $i";
        var buf = new DocumentBuffer("file:///t.xq", 1, xq);
        // Caret on the use of $i at offset ~25
        var loc = DefinitionHandler.Handle(buf, new Position(0, 25));
        Assert.NotNull(loc);
    }
}
