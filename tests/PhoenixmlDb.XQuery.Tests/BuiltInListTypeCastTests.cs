using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// The built-in LIST types — <c>xs:IDREFS</c>, <c>xs:NMTOKENS</c>, <c>xs:ENTITIES</c> — as
/// cast/castable targets. XQuery 3.0 permits them there even though a list type is not an item
/// type: the lexical form is split on whitespace and each token cast to the member type, so the
/// result is a SEQUENCE (QT3 <c>CastAs-ListType-7</c>: <c>"a b c" cast as xs:IDREFS</c> is
/// <c>'a','b','c'</c> of type <c>xs:IDREF*</c>).
///
/// **Position is the whole rule.** In any SequenceType position — <c>as</c> declarations,
/// <c>instance of</c>, a function parameter type — a list type is still XPST0051. The first
/// version of this change allowed the names everywhere and broke exactly four QT3 cases that
/// assert that error, which is why the second half of this file exists.
/// </summary>
public class BuiltInListTypeCastTests
{
    private readonly XQueryFacade _facade = new();

    // ---- cast/castable target: permitted ------------------------------------------------

    [Theory]
    [InlineData("\"a b c\" castable as xs:IDREFS", "true")]
    [InlineData("\"a b c\" castable as xs:ENTITIES", "true")]
    [InlineData("\"a b c\" castable as xs:NMTOKENS", "true")]
    // NMTOKEN admits digits; IDREF/ENTITY are NCName-based and do not. Same input, different
    // answers — this pair is what shows the member type is actually consulted rather than the
    // list being waved through.
    [InlineData("\"1 2 3\" castable as xs:NMTOKENS", "true")]
    [InlineData("\"1 2 3\" castable as xs:IDREFS", "false")]
    [InlineData("\"1 2 3\" castable as xs:ENTITIES", "false")]
    // A list type has minLength 1: the empty list is not a valid value.
    [InlineData("\"\" castable as xs:IDREFS", "false")]
    [InlineData("\"   \" castable as xs:IDREFS", "false")]
    public async Task Castable_as_list_type(string query, string expected)
        => (await _facade.EvaluateAsync(query)).Should().Be(expected);

    [Fact]
    public async Task Cast_to_list_type_yields_one_item_per_token()
        => (await _facade.EvaluateAsync("count(\"a b c\" cast as xs:IDREFS)")).Should().Be("3");

    [Fact]
    public async Task Cast_to_list_type_yields_the_member_type()
        => (await _facade.EvaluateAsync("(\"a b c\" cast as xs:IDREFS) instance of xs:IDREF*"))
            .Should().Be("true");

    [Fact]
    public async Task Cast_to_list_type_splits_on_any_whitespace_run()
        => (await _facade.EvaluateAsync("count(\"a\tb\n c   d\" cast as xs:NMTOKENS)"))
            .Should().Be("4");

    [Fact]
    public async Task Cast_to_list_type_with_an_invalid_token_raises_FORG0001()
    {
        // The code lives on the exception, not in the message text.
        var act = async () => await _facade.EvaluateAsync("\"a 1b c\" cast as xs:IDREFS");
        var ex = await act.Should().ThrowAsync<PhoenixmlDb.XQuery.Execution.XQueryRuntimeException>();
        ex.Which.ErrorCode.Should().Be("FORG0001");
        ex.Which.Message.Should().Contain("1b", "the offending TOKEN is what locates the problem, not the whole list");
    }

    // ---- SequenceType position: still XPST0051 -------------------------------------------

    /// <summary>
    /// Each of these is a QT3 case that asserts XPST0051, and each one regressed when the list
    /// types were first made recognisable everywhere rather than only as a cast target:
    /// CastAs-ListType-20, ForExprType047, FunctionCall-027, instanceof111.
    /// </summary>
    [Theory]
    [InlineData("xs:NMTOKEN('abc') instance of xs:NMTOKENS")]
    [InlineData("let $v as xs:NMTOKENS := xs:NMTOKEN('a') return $v")]
    [InlineData("for $t as xs:NMTOKENS in (xs:NMTOKEN('ab')) return $t")]
    [InlineData("function($in as xs:NMTOKENS) as item()* {$in}(xs:untypedAtomic('abc def'))")]
    [InlineData("1 instance of xs:IDREFS")]
    [InlineData("1 instance of xs:ENTITIES")]
    public async Task List_type_as_a_sequence_type_raises_XPST0051(string query)
    {
        var act = async () => await _facade.EvaluateAsync(query);
        (await act.Should().ThrowAsync<System.Exception>()).Which.Message.Should().Contain("XPST0051");
    }

    // ---- guards ---------------------------------------------------------------------------

    /// <summary>
    /// The singular member types were always recognised and must keep behaving as single items,
    /// so the list handling cannot be credited for them.
    /// </summary>
    [Theory]
    [InlineData("count(\"a\" cast as xs:IDREF)", "1")]
    [InlineData("count(\"a\" cast as xs:NMTOKEN)", "1")]
    [InlineData("\"1\" castable as xs:NMTOKEN", "true")]
    [InlineData("\"1\" castable as xs:IDREF", "false")]
    public async Task Singular_member_types_are_unaffected(string query, string expected)
        => (await _facade.EvaluateAsync(query)).Should().Be(expected);

    /// <summary>Guard: an unknown xs: name is still rejected, so the switch did not become permissive.</summary>
    [Fact]
    public async Task Unknown_xs_type_is_still_XPST0051()
    {
        var act = async () => await _facade.EvaluateAsync("\"a\" cast as xs:doesNotExist");
        (await act.Should().ThrowAsync<System.Exception>()).Which.Message.Should().Contain("XPST0051");
    }
}
