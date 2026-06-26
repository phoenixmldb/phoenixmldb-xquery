using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// Type-system coverage for the special XSD union/empty types
/// <c>xs:numeric</c>, <c>xs:error</c> and <c>xs:dateTimeStamp</c>, mirroring the
/// W3C QT3 xs-numeric / xs-error / xs-dateTimeStamp conformance sets.
/// </summary>
public class XsUnionTypeTests
{
    private readonly XQueryFacade _facade = new();

    // --- xs:numeric ------------------------------------------------------

    [Theory]
    [InlineData("3.14e0 instance of xs:numeric")]   // double
    [InlineData("3.14 instance of xs:numeric")]     // decimal
    [InlineData("3 instance of xs:numeric")]        // integer
    [InlineData("xs:float('93.7') instance of xs:numeric")]
    [InlineData("'12.5' castable as xs:numeric")]
    public async Task Numeric_members_and_castability_are_true(string query)
    {
        var result = await _facade.EvaluateAsync(query);
        result.Should().Be("true");
    }

    [Fact]
    public async Task Numeric_nonNumeric_is_not_numeric()
    {
        var result = await _facade.EvaluateAsync("'abc' instance of xs:numeric");
        result.Should().Be("false");
    }

    [Fact]
    public async Task Numeric_constructor_from_string_yields_double()
    {
        var result = await _facade.EvaluateAsync("xs:numeric('12.5') instance of xs:double");
        result.Should().Be("true");
    }

    [Fact]
    public async Task Numeric_castable_failure_is_false()
    {
        var result = await _facade.EvaluateAsync("'12.5.7' castable as xs:numeric");
        result.Should().Be("false");
    }

    [Theory]
    // cast as xs:numeric preserves the member type of an already-numeric value.
    [InlineData("17 cast as xs:numeric instance of xs:integer")]
    [InlineData("17.2 cast as xs:numeric instance of xs:decimal")]
    [InlineData("1e3 cast as xs:numeric instance of xs:double")]
    [InlineData("xs:float(1e3) cast as xs:numeric instance of xs:float")]
    // non-numeric input casts to xs:double (the first applicable member type)
    [InlineData("true() cast as xs:numeric instance of xs:double")]
    public async Task Numeric_cast_preserves_or_promotes_member_type(string query)
    {
        var result = await _facade.EvaluateAsync(query);
        result.Should().Be("true");
    }

    // --- xs:error --------------------------------------------------------

    [Theory]
    // xs:error? and xs:error* denote exactly the empty sequence, identical to empty-sequence().
    [InlineData("xs:error#1 instance of function(xs:anyAtomicType?) as xs:error?")]
    [InlineData("xs:error#1 instance of function(xs:anyAtomicType?) as empty-sequence()")]
    [InlineData("function() as empty-sequence() { () } instance of function() as xs:error?")]
    [InlineData("function() as empty-sequence() { () } instance of function() as xs:error*")]
    public async Task Error_optional_is_equivalent_to_empty_sequence(string query)
    {
        var result = await _facade.EvaluateAsync(query);
        result.Should().Be("true");
    }

    [Fact]
    public async Task Error_constructor_of_value_raises_FORG0001()
    {
        var act = async () => await _facade.EvaluateAsync("xs:error(1)");
        var ex = await act.Should().ThrowAsync<PhoenixmlDb.XQuery.Execution.XQueryRuntimeException>();
        ex.Which.ErrorCode.Should().Be("FORG0001");
    }

    [Fact]
    public async Task Error_value_is_never_an_instance_of_error()
    {
        // xs:error is the empty union type; no value is an instance.
        var result = await _facade.EvaluateAsync("1 instance of xs:error");
        result.Should().Be("false");
    }

    // --- xs:dateTimeStamp ------------------------------------------------

    [Fact]
    public async Task DateTimeStamp_castable_from_value_with_timezone()
    {
        var result = await _facade.EvaluateAsync("current-date() castable as xs:dateTimeStamp");
        result.Should().Be("true");
    }
}
