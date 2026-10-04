using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Functions;

/// <summary>
/// U+FFFE and U+FFFF are not XML characters (XML 1.0 Char), so json-to-xml treats them like the
/// control characters: the fallback function replaces them, or U+FFFD does when there is none.
/// </summary>
/// <remarks>
/// Only the controls below U+0020 were caught. A JSON escape of U+FFFF reached the tree as is and the
/// fallback was never called (QT3 d1e78807d).
/// </remarks>
public class JsonToXmlNonCharacterTests
{
    private readonly XQueryFacade _facade = new();

    [Theory]
    [InlineData("FFFF")]
    [InlineData("FFFE")]
    public async Task TheFallback_ReplacesANonCharacter(string hex)
        => (await _facade.EvaluateAsync(
                $$"""string(json-to-xml('"a\u{{hex}}b"', map { 'fallback': function($s) { '[' || $s || ']' } }))"""))
            .Should().Be($"a[\\u{hex}]b");

    [Fact]
    public async Task WithoutAFallback_ANonCharacterBecomesTheReplacementCharacter()
        => (await _facade.EvaluateAsync("""string-to-codepoints(string(json-to-xml('"\uFFFF"')))"""))
            .Should().Be("65533");

    [Fact]
    public async Task ACharacterJustBelow_IsKept()
        => (await _facade.EvaluateAsync("""string-to-codepoints(string(json-to-xml('"\uFFFD"')))"""))
            .Should().Be("65533");
}
