using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// Coverage for the cast/castable target-type rules: a derived-integer cast
/// must yield a value of that derived dynamic type (QT3 prod-CastExpr
/// CastAs660..669), <c>xs:numeric</c> is a permitted cast target (F&amp;O §19),
/// while abstract/non-castable targets (<c>xs:anyAtomicType</c>,
/// <c>xs:NOTATION</c>) must raise XPST0080.
/// </summary>
public class CastTargetTypeTests
{
    private readonly XQueryFacade _facade = new();

    [Theory]
    // A cast to a derived integer subtype produces a value of that dynamic type,
    // so `instance of` the same (or a sub-) type holds. Regression guard for the
    // prod-CastExpr CastAs660..669 cases.
    [InlineData("xs:long(120) cast as xs:short instance of xs:short")]
    [InlineData("xs:short(120) cast as xs:long instance of xs:long")]
    [InlineData("xs:nonPositiveInteger(-120) cast as xs:negativeInteger instance of xs:negativeInteger")]
    [InlineData("xs:nonNegativeInteger(120) cast as xs:positiveInteger instance of xs:positiveInteger")]
    [InlineData("xs:short(120) cast as xs:unsignedShort instance of xs:unsignedShort")]
    [InlineData("xs:byte(120) cast as xs:unsignedByte instance of xs:unsignedByte")]
    // The tagged subtype is also an instance of its supertypes.
    [InlineData("xs:long(120) cast as xs:short instance of xs:integer")]
    public async Task Cast_to_derived_integer_yields_that_dynamic_type(string query)
    {
        var result = await _facade.EvaluateAsync(query);
        result.Should().Be("true");
    }

    [Fact]
    public async Task Cast_to_derived_integer_is_not_instance_of_unrelated_subtype()
    {
        // xs:short is NOT a subtype of xs:byte — guards against over-tagging.
        var result = await _facade.EvaluateAsync("xs:int(120) cast as xs:short instance of xs:byte");
        result.Should().Be("false");
    }

    [Fact]
    public async Task Cast_as_numeric_is_permitted()
    {
        // xs:numeric is the special F&O union cast target — must NOT raise XPST0080.
        var result = await _facade.EvaluateAsync("17 cast as xs:numeric instance of xs:integer");
        result.Should().Be("true");
    }

    [Theory]
    [InlineData("1 cast as xs:anyAtomicType")]
    [InlineData("1 castable as xs:anyAtomicType")]
    [InlineData("'x' cast as xs:NOTATION")]
    [InlineData("'x' castable as xs:NOTATION")]
    public async Task Cast_to_non_castable_target_raises_XPST0080(string query)
    {
        var act = async () => await _facade.EvaluateAsync(query);
        var ex = await act.Should().ThrowAsync<PhoenixmlDb.XQuery.Parser.XQueryParseException>();
        ex.Which.Message.Should().Contain("XPST0080");
    }
}
