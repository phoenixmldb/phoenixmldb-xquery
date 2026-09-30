using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// Each case used to surface a raw .NET exception — "Value was either too large or too small
/// for an Int32", "Unable to cast object of type 'XsDate' to type 'IConvertible'", "The input
/// string was not in a correct format" — where the specification assigns an error code.
/// </summary>
public class LeakedExceptionTests
{
    private readonly XQueryFacade _facade = new();

    private async Task<string?> CodeOf(string query)
    {
        var act = () => _facade.EvaluateAsync(query);
        var ex = (await act.Should().ThrowAsync<Exception>()).Which;
        for (Exception? e = ex; e != null; e = e.InnerException)
            if (e.GetType().GetProperty("ErrorCode")?.GetValue(e) is string { Length: > 0 } code)
                return code;
        return ex.GetType().Name + ": " + ex.Message;
    }

    [Theory]
    // A well-formed duration or date outside the supported range (F&O 3.1 §10)
    [InlineData("xs:dayTimeDuration('P9223372036854775807D')", "FODT0002")]
    [InlineData("'P9223372036854775807D' cast as xs:dayTimeDuration", "FODT0002")]
    [InlineData("'-P768614336404564651Y' cast as xs:duration", "FODT0002")]
    [InlineData("xs:yearMonthDuration('P768614336404564651Y')", "FODT0002")]
    [InlineData("avg((xs:dayTimeDuration('P10675199D'), xs:dayTimeDuration('P10675199D')))", "FODT0002")]
    [InlineData("'18446744073709551616-05-15' cast as xs:date", "FODT0001")]
    // A bounded integer subtype past its bound is outside the value space
    [InlineData("xs:int('2147483648')", "FORG0001")]
    [InlineData("xs:long('9223372036854775808')", "FORG0001")]
    [InlineData("xs:unsignedByte('256')", "FORG0001")]
    // ...but a non-finite float/double has no integer value at all
    [InlineData("xs:byte(xs:float('-INF'))", "FOCA0002")]
    [InlineData("xs:int(xs:double('NaN'))", "FOCA0002")]
    // Array positions beyond the int range are out of bounds
    [InlineData("array:get([1], 4294967297)", "FOAY0001")]
    [InlineData("[1, 2]?(4294967297)", "FOAY0001")]
    // Non-numeric operands
    [InlineData("avg(xs:duration('P1Y1M1D'))", "FORG0006")]
    [InlineData("avg(xs:date('1993-03-31'))", "FORG0006")]
    [InlineData("avg(xs:anyURI('a string'))", "FORG0006")]
    [InlineData("abs(xs:anyURI('www.example.org'))", "XPTY0004")]
    [InlineData("-xs:date('2007-11-29')", "XPTY0004")]
    [InlineData("+xs:dayTimeDuration('P1D')", "XPTY0004")]
    // Array lookup keys must be integers
    [InlineData("['a', 'b']?first", "XPTY0004")]
    [InlineData("['a', 'b']?(current-date())", "XPTY0004")]
    // codepoints-to-string takes integers
    [InlineData("codepoints-to-string(xs:NMTOKENS('30 31'))", "XPTY0004")]
    public async Task The_specified_error_is_raised(string query, string code) =>
        (await CodeOf(query)).Should().Be(code);

    [Theory]
    [InlineData("avg((xs:int(1), xs:int(2)))", "1.5")]
    [InlineData("['a', 'b']?(xs:untypedAtomic('2'))", "b")]
    [InlineData("-xs:int(3)", "-3")]
    [InlineData("string(xs:dayTimeDuration('P1D') + xs:dayTimeDuration('PT1H'))", "P1DT1H")]
    public async Task Valid_operands_are_unaffected(string query, string expected) =>
        (await _facade.EvaluateAsync(query)).Should().Be(expected);
}
