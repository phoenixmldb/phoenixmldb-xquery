using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// Casting <c>xs:QName</c> to <c>xs:string</c> yields its LEXICAL form — <c>prefix:local</c>, or
/// the bare local name when there is no prefix (XPath 3.1 §19.2).
/// </summary>
/// <remarks>
/// <para>
/// <c>XQueryStringValue</c> had no QName branch, so the value fell through to
/// <c>QName.ToString()</c> in PhoenixmlDb.Core. That renders the EQName form
/// <c>Q{uri}local</c> as soon as an expanded namespace is attached — a good DEBUGGING rendering
/// and the wrong VALUE. Delegating a spec-defined conversion to a .NET ToString() is what let a
/// diagnostic form become a value.
/// </para>
/// <para>
/// The consequence was felt in the XSLT engine: <c>$err:code</c> could not carry its namespace
/// URI, because attaching one flipped how it printed. That in turn meant a caught error code
/// could never compare equal to <c>QName('http://www.w3.org/2005/xqt-errors', 'XTDE0570')</c>,
/// which is how XSpec asserts on error codes. Core's ToString is deliberately left alone; the
/// two renderings serve different audiences.
/// </para>
/// </remarks>
public class QNameLexicalStringValueTests
{
    private readonly XQueryFacade _facade = new();

    /// <summary>
    /// A PREFIXED QName keeps its prefix, whichever namespace-carrying field is populated.
    /// This is the case that regressed when the XSLT engine tried to attach a namespace URI.
    /// </summary>
    [Theory]
    [InlineData("""string(QName("http://www.w3.org/2005/xqt-errors", "err:XTDE0570"))""", "err:XTDE0570")]
    [InlineData("""string(QName("urn:a", "p:foo"))""", "p:foo")]
    [InlineData("""string(xs:QName("xs:string"))""", "xs:string")]
    public async Task PrefixedQName_stringifiesToItsLexicalForm(string query, string expected)
    {
        var result = await _facade.EvaluateAsync(query);
        result.Should().Be(expected);
    }

    /// <summary>An unprefixed QName is its bare local name, namespace or not.</summary>
    [Theory]
    [InlineData("""string(QName("urn:a", "foo"))""", "foo")]
    [InlineData("""string(QName((), "foo"))""", "foo")]
    public async Task UnprefixedQName_stringifiesToItsLocalName(string query, string expected)
    {
        var result = await _facade.EvaluateAsync(query);
        result.Should().Be(expected);
    }

    /// <summary>
    /// The string form must not be the only thing that changed: the namespace URI is still
    /// carried and still reachable, which is the whole point of attaching it.
    /// </summary>
    [Fact]
    public async Task StringForm_doesNotCostTheNamespaceUri()
    {
        var result = await _facade.EvaluateAsync(
            """string(namespace-uri-from-QName(QName("http://www.w3.org/2005/xqt-errors", "err:XTDE0570")))""");
        result.Should().Be("http://www.w3.org/2005/xqt-errors");
    }

    /// <summary>
    /// And equality still compares by namespace URI plus local name, ignoring the prefix, so a
    /// prefixed error code equals the same name built from its URI.
    /// </summary>
    [Fact]
    public async Task PrefixedAndUriBuiltQNames_compareEqual()
    {
        var result = await _facade.EvaluateAsync(
            """QName("http://www.w3.org/2005/xqt-errors", "err:XTDE0570")"""
            + """ eq QName("http://www.w3.org/2005/xqt-errors", "XTDE0570")""");
        result.Should().Be("true");
    }

    /// <summary>fn:string-join and concatenation see the same lexical form.</summary>
    [Fact]
    public async Task LexicalForm_isUsedByStringJoin()
    {
        var result = await _facade.EvaluateAsync(
            """string-join((QName("urn:a", "p:one"), QName("urn:a", "p:two")) ! string(.), "|")""");
        result.Should().Be("p:one|p:two");
    }
}
