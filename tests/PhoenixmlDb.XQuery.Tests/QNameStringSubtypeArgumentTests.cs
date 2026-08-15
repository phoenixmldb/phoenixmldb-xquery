using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// <c>fn:QName($paramURI as xs:string?, $paramQName as xs:string)</c> must accept any value
/// whose type derives from <c>xs:string</c>, per the function conversion rules — the XSD
/// hierarchy being
/// <c>xs:string &gt; xs:normalizedString &gt; xs:token &gt; { xs:language, xs:NMTOKEN, xs:Name }</c>
/// and <c>xs:Name &gt; xs:NCName &gt; { xs:ID, xs:IDREF, xs:ENTITY }</c>.
///
/// Those subtypes are carried by <c>XsTypedString</c> rather than a bare CLR string (so that
/// <c>xs:normalizedString("x") instance of xs:token</c> can answer correctly). The argument
/// guard tested only for <c>string</c> and <c>XsUntypedAtomic</c>, so it rejected every one of
/// them with <c>XPTY0004</c> — including <c>xs:NCName</c>, which is the natural thing to build
/// a QName from.
///
/// Found via XSpec, where it accounted for 56 of the 162 XSLT suites in the census.
/// </summary>
public class QNameStringSubtypeArgumentTests
{
    private readonly XQueryFacade _facade = new();

    [Theory]
    // The reported case and its siblings down the xs:string derivation chain.
    [InlineData("""string(QName("urn:a", xs:NCName("foo")))""", "foo")]
    [InlineData("""string(QName("urn:a", xs:Name("foo")))""", "foo")]
    [InlineData("""string(QName("urn:a", xs:token("foo")))""", "foo")]
    [InlineData("""string(QName("urn:a", xs:normalizedString("foo")))""", "foo")]
    // A plain string and untypedAtomic kept working.
    [InlineData("""string(QName("urn:a", "foo"))""", "foo")]
    [InlineData("""string(QName("urn:a", xs:untypedAtomic("foo")))""", "foo")]
    // A prefixed name survives, and the subtype may carry the prefix too.
    [InlineData("""string(QName("urn:a", "p:foo"))""", "p:foo")]
    [InlineData("""string(QName("urn:a", xs:Name("p:foo")))""", "p:foo")]
    public async Task QName_accepts_xs_string_subtypes(string query, string expected)
    {
        var result = await _facade.EvaluateAsync(query);
        result.Should().Be(expected);
    }

    [Fact]
    public async Task QName_first_argument_accepts_a_string_subtype()
    {
        var result = await _facade.EvaluateAsync(
            """string(namespace-uri-from-QName(QName(xs:token("urn:a"), "foo")))""");
        result.Should().Be("urn:a");
    }

    [Fact]
    public async Task QName_resolves_the_namespace_from_a_subtype_argument()
    {
        // Not just the lexical form: the constructed QName must carry the URI.
        var result = await _facade.EvaluateAsync(
            """string(namespace-uri-from-QName(QName("urn:a", xs:NCName("foo"))))""");
        result.Should().Be("urn:a");
    }

    [Theory]
    // Guard against over-accepting: a genuinely wrong argument type is still XPTY0004.
    [InlineData("""QName("urn:a", 42)""")]
    [InlineData("""QName("urn:a", xs:date("2026-01-01"))""")]
    [InlineData("""QName(42, "foo")""")]
    public async Task QName_still_rejects_non_string_arguments(string query)
    {
        var act = async () => await _facade.EvaluateAsync(query);
        var ex = await act.Should().ThrowAsync<PhoenixmlDb.XQuery.Execution.XQueryRuntimeException>();
        ex.Which.ErrorCode.Should().Be("XPTY0004");
    }
}
