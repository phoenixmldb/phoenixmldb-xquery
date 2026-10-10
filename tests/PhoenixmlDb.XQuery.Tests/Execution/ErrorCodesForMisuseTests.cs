using FluentAssertions;
using PhoenixmlDb.XQuery.Functions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// A query that misuses a function gets the error of the specification, not an exception of
/// the runtime: a function of the wrong arity handed to a higher-order function, an array
/// index or an arity that no int can hold, the head of an empty array, a function item
/// where a string is wanted.
/// </summary>
public class ErrorCodesForMisuseTests
{
    private readonly XQueryFacade _facade = new();

    [Theory]
    [InlineData("for-each(('aa', 'bb'), starts-with#2)", "XPTY0004")]
    [InlineData("filter(('aa', 'bb'), starts-with#2)", "XPTY0004")]
    [InlineData("array:for-each([10, 20], remove#2)", "XPTY0004")]
    [InlineData("array:filter(['apple', 'banana'], substring-after#2)", "XPTY0004")]
    [InlineData("fold-left(1 to 5, 1, function($a, $b, $c) { $a + $b + $c })", "XPTY0004")]
    [InlineData("fold-right(1 to 5, 0, function($a, $b, $c) { $a + $b + $c })", "XPTY0004")]
    [InlineData("fold-left(1 to 5, 1, function($a) { $a })", "XPTY0004")]
    [InlineData("array:head([])", "FOAY0001")]
    [InlineData("array:tail([])", "FOAY0001")]
    [InlineData("[1](4294967297)", "FOAY0001")]
    [InlineData("[1](2)", "FOAY0001")]
    [InlineData("concat('abc', 'abc', concat#3)", "FOTY0013")]
    [InlineData("function-arity(concat#340282366920938463463374607431768211456)", "FOAR0002")]
    [InlineData("function-name(concat#340282366920938463463374607431768211456)", "FOAR0002")]
    public async Task The_error_is_that_of_the_specification(string query, string code)
    {
        var act = () => _facade.EvaluateAsync(query);
        var thrown = (await act.Should().ThrowAsync<Exception>()).Which;
        var actual = thrown switch
        {
            XQueryException e => e.ErrorCode,
            PhoenixmlDb.XQuery.Execution.XQueryRuntimeException e => e.ErrorCode,
            // An error found while the query is read arrives wrapped, with the code in the text.
            _ => thrown.Message.Contains(code, StringComparison.Ordinal) ? code : thrown.GetType().Name,
        };
        actual.Should().Be(code, thrown.Message);
    }

    [Theory]
    // a function of the right arity, a map and an array as the function, a variadic function
    [InlineData("string-join(for-each(('a', 'b'), upper-case#1), '')", "AB")]
    [InlineData("string-join(for-each((1, 2), map { 1: 'x', 2: 'y' }), '')", "xy")]
    [InlineData("string-join(for-each((2, 1), ['x', 'y']), '')", "yx")]
    [InlineData("fold-left(1 to 5, 0, function($a, $b) { $a + $b })", "15")]
    [InlineData("fold-right(1 to 3, '', function($a, $b) { $b || $a })", "321")]
    [InlineData("fold-left(('a', 'b'), 'x', concat#2)", "xab")]
    [InlineData("array:head([7, 8])", "7")]
    [InlineData("[1, 2](2)", "2")]
    public async Task And_what_was_right_still_is(string query, string expected)
        => (await _facade.EvaluateAsync(query)).Should().Be(expected);
}
