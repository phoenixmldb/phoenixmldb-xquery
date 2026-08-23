using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// Two independent fixes found while auditing the XSpec census, both of which had been
/// reported by an error message that named the wrong thing.
/// </summary>
public class RegexSubtractionAndUriPromotionTests
{
    private readonly XQueryFacade _facade = new();
    private async Task<string> Eval(string xq) => await _facade.EvaluateAsync(xq);

    /// <summary>
    /// XSD character-class SUBTRACTION whose subtracted class starts with a colon —
    /// <c>[\i-[:]]</c>, the canonical idiom for an NCName start character, and the most common
    /// use of subtraction anywhere.
    ///
    /// The POSIX guard fired on the two characters "[:", which is how BOTH POSIX syntax and
    /// this subtraction begin, and it ran BEFORE the subtraction check — so it never consulted
    /// the one fact that separates them (whether an unescaped '-' precedes the bracket), even
    /// though the next line already computed it. 17 XSpec suites failed with "POSIX character
    /// class syntax is not supported" against patterns using no POSIX syntax at all; grepping
    /// XSpec for "[[:" returns nothing.
    /// </summary>
    [Fact]
    public async Task NCName_start_char_subtraction_is_not_mistaken_for_POSIX()
    {
        (await Eval(@"matches('a', '^[\i-[:]]$')")).Should().Be("true");
        (await Eval(@"matches(':', '^[\i-[:]]$')")).Should().Be("false");
    }

    [Fact]
    public async Task Full_NCName_pattern_compiles()
        => (await Eval(@"matches('a-b', '^[\i-[:]][\c-[:]]*$')")).Should().Be("true");

    [Fact]
    public async Task Plain_subtraction_still_works()
    {
        (await Eval("matches('b', '^[a-z-[aeiou]]$')")).Should().Be("true");
        (await Eval("matches('e', '^[a-z-[aeiou]]$')")).Should().Be("false");
    }

    /// <summary>
    /// Genuine POSIX classes are NOT valid in XSD regex and must still be rejected — the fix
    /// narrowed the guard, it did not remove it.
    /// </summary>
    [Fact]
    public async Task Genuine_POSIX_syntax_is_still_rejected()
    {
        var act = async () => await Eval("matches('a', '^[[:alpha:]]$')");
        await act.Should().ThrowAsync<Exception>();
    }

    /// <summary>
    /// F&amp;O function conversion includes URI promotion: xs:anyURI is valid wherever xs:string
    /// is declared. The engine honours that everywhere the conversion machinery runs; fn:QName
    /// hand-rolled its type check and rejected it — in exactly the case the function exists
    /// for, since a namespace URI is the natural thing to hold in an xs:anyURI.
    /// </summary>
    [Fact]
    public async Task QName_accepts_an_anyURI_namespace()
        => (await Eval(@"string(QName(xs:anyURI('urn:x'), 'p:local'))")).Should().Be("p:local");

    [Fact]
    public async Task QName_still_rejects_a_genuinely_wrong_type()
    {
        var act = async () => await Eval("QName(42, 'p:local')");
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Other_string_functions_keep_accepting_anyURI()
    {
        (await Eval(@"string-length(xs:anyURI('urn:x'))")).Should().Be("5");
        (await Eval(@"upper-case(xs:anyURI('urn:x'))")).Should().Be("URN:X");
    }
}
