using PhoenixmlDb.XQuery.Parser;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Parser;

/// <summary>
/// Martin Honnen 2026-07-30 (XSpec). A prefixed name in an element()/attribute() kind test
/// was validated against the XQuery prolog at parse time. For XSLT callers (AllowNamespaceAxis)
/// the prefix lives on the enclosing xsl:* element, which the XQuery parser never sees, so the
/// validation must be DEFERRED to the XSLT namespace post-pass rather than raising a spurious
/// XPST0081. Pure XQuery still validates against its own prolog declarations.
/// </summary>
public sealed class KindTestPrefixDeferralTests
{
    [Fact]
    public void XsltMode_UndeclaredPrefixInAttributeKindTest_DoesNotThrow()
    {
        // AllowNamespaceAxis marks an XSLT/external-namespace caller — the prefix 'x' is
        // resolved later against the stylesheet, so parsing must succeed here.
        var parser = new XQueryParserFacade { AllowNamespaceAxis = true };
        var result = parser.Parse("$e/@*[self::attribute(x:foo)]");
        Assert.NotNull(result);
    }

    [Fact]
    public void XsltMode_UndeclaredPrefixInElementKindTest_DoesNotThrow()
    {
        var parser = new XQueryParserFacade { AllowNamespaceAxis = true };
        var result = parser.Parse("$e/*[self::element(x:foo)]");
        Assert.NotNull(result);
    }

    [Fact]
    public void PureXQuery_UndeclaredPrefixInKindTest_StillRaisesXPST0081()
    {
        // Without AllowNamespaceAxis this is pure XQuery: an undeclared prefix in a kind test
        // must still be rejected (it can only come from the prolog, which does not declare 'x').
        var parser = new XQueryParserFacade();
        var ex = Assert.Throws<XQueryParseException>(() => parser.Parse("$e/@*[self::attribute(x:foo)]"));
        Assert.Contains("XPST0081", ex.Message);
    }

    [Fact]
    public void PureXQuery_PrologDeclaredPrefixInKindTest_Parses()
    {
        // A prolog-declared prefix resolves normally in pure XQuery.
        var parser = new XQueryParserFacade();
        var result = parser.Parse("declare namespace x = \"http://example.com/x\"; $e/@*[self::attribute(x:foo)]");
        Assert.NotNull(result);
    }
}
