using PhoenixmlDb.XQuery.LanguageServer;
using PhoenixmlDb.XQuery.LanguageServer.Handlers;
using PhoenixmlDb.XQuery.LanguageServer.Lsp;
using Xunit;

namespace PhoenixmlDb.XQuery.LanguageServer.Tests;

public class SignatureHelpTests
{
    [Fact]
    public void ReturnsNullOutsideFunctionCall()
    {
        var buf = new DocumentBuffer("file:///x.xq", 1, "1 + 1");
        Assert.Null(SignatureHelpHandler.Handle(buf, new Position(0, 4)));
    }

    [Fact]
    public void ReturnsSignatureInsideKnownFunctionCall()
    {
        var buf = new DocumentBuffer("file:///x.xq", 1, "count(//book)");
        var help = SignatureHelpHandler.Handle(buf, new Position(0, 6));
        Assert.NotNull(help);
        Assert.Single(help!.Signatures);
        Assert.Contains("count", help.Signatures[0].Label, System.StringComparison.Ordinal);
    }

    [Fact]
    public void ActiveParameterCountsCommasAtDepthZero()
    {
        // string-join($arg, "|")  → caret after the second arg → activeParameter = 1
        var buf = new DocumentBuffer("file:///x.xq", 1, "string-join(\"a\", \"|\")");
        var help = SignatureHelpHandler.Handle(buf, new Position(0, 18));
        Assert.NotNull(help);
        Assert.Equal(1, help!.ActiveParameter);
    }

    [Fact]
    public void NestedParensDontAffectActiveParam()
    {
        // f(g(a, b), c) — caret on `c` should be activeParam=1, not 3
        var buf = new DocumentBuffer("file:///x.xq", 1, "count(string(g(1, 2)), 3)");
        var help = SignatureHelpHandler.Handle(buf, new Position(0, 23));
        // The caret is inside count(...), so signatureHelp finds count's signature.
        Assert.NotNull(help);
        Assert.Equal(1, help!.ActiveParameter);
    }

    [Fact]
    public void UnknownFunctionReturnsNull()
    {
        var buf = new DocumentBuffer("file:///x.xq", 1, "no-such-fn(1)");
        Assert.Null(SignatureHelpHandler.Handle(buf, new Position(0, 11)));
    }
}
