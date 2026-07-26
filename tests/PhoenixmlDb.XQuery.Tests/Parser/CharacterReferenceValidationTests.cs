using FluentAssertions;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Parser;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Parser;

/// <summary>
/// Adversarial-audit finding I-charref: numeric character references that decode to a UTF-32
/// scalar which is NOT a valid XML 1.0 character (C0 controls other than #x9/#xA/#xD, surrogates,
/// #xFFFE/#xFFFF, anything above #x10FFFF) must be rejected with the spec error XQST0090 rather
/// than silently producing an illegal character (e.g. <c>&amp;#xFFFF;</c> used to parse cleanly and
/// would emit an illegal character on serialization).
/// </summary>
[Trait("Category", "Parser")]
public class CharacterReferenceValidationTests
{
    private readonly XQueryParserFacade _parser = new();

    [Theory]
    [InlineData("\"&#0;\"")]       // NUL — C0 control, never valid XML
    [InlineData("\"&#x0;\"")]      // NUL, hex form
    [InlineData("\"&#1;\"")]       // C0 control
    [InlineData("\"&#8;\"")]       // backspace (C0)
    [InlineData("\"&#xB;\"")]      // vertical tab (C0, not #x9/#xA/#xD)
    [InlineData("\"&#xD800;\"")]   // high surrogate
    [InlineData("\"&#xDFFF;\"")]   // low surrogate
    [InlineData("\"&#xFFFE;\"")]   // non-character
    [InlineData("\"&#xFFFF;\"")]   // non-character (previously accepted!)
    [InlineData("\"&#x110000;\"")] // above the Unicode maximum U+10FFFF
    [InlineData("\"&#x7FFFFFFFFFFFFFFF;\"")] // overflows even Int64
    public void Parse_InvalidXmlCharacterReference_RaisesXQST0090(string query)
    {
        var ex = Record.Exception(() => _parser.Parse(query));
        ex.Should().NotBeNull("'{0}' is not a valid XML character and must be a static error", query);
        var xqe = ex.Should().BeOfType<PhoenixmlDb.Core.XQueryException>().Subject;
        xqe.ErrorCode.Should().Be("XQST0090");
    }

    [Theory]
    [InlineData("\"&#x41;\"", "A")]        // basic Latin
    [InlineData("\"&#65;\"", "A")]         // decimal form
    [InlineData("\"&#9;\"", "\t")]         // tab — explicitly allowed
    [InlineData("\"&#xA;\"", "\n")]        // line feed — explicitly allowed
    [InlineData("\"&#xD;\"", "\r")]        // carriage return — explicitly allowed
    [InlineData("\"&#x20;\"", " ")]        // space
    [InlineData("\"&#xE9;\"", "é")]   // é
    [InlineData("\"&#x1F600;\"", "\U0001F600")] // astral (emoji) — valid supplementary char
    public void Parse_ValidCharacterReference_DecodesCorrectly(string query, string expected)
    {
        var result = _parser.Parse(query);
        result.Should().BeOfType<StringLiteral>().Which.Value.Should().Be(expected);
    }
}
