using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Functions;

/// <summary>
/// A constructor function is a cast, so a source type the casting table forbids is XPTY0004,
/// not FORG0001: the constructors turned the operand into a string and reported its lexical
/// form as invalid. Also: an external variable with no value is XPDY0002.
/// </summary>
public class ConstructorSourceTypeTests
{
    private readonly XQueryFacade _facade = new();

    private async Task<string?> CodeOf(string query)
    {
        var act = () => _facade.EvaluateAsync(query);
        var ex = (await act.Should().ThrowAsync<Exception>()).Which;
        for (Exception? e = ex; e != null; e = e.InnerException)
            if (e.GetType().GetProperty("ErrorCode")?.GetValue(e) is string { Length: > 0 } code)
                return code;
        return null;
    }

    [Theory]
    [InlineData("xs:dateTime(2.5)")]
    [InlineData("xs:date(1)")]
    [InlineData("xs:time(true())")]
    [InlineData("xs:gYear(2.5)")]
    [InlineData("xs:duration(1)")]
    [InlineData("xs:hexBinary(1)")]
    [InlineData("xs:base64Binary(1)")]
    [InlineData("1 cast as xs:hexBinary")]
    public async Task A_forbidden_source_type_is_XPTY0004(string query) =>
        (await CodeOf(query)).Should().Be("XPTY0004");

    [Theory]
    [InlineData("string(xs:dateTime(xs:date('2020-01-01')))", "2020-01-01T00:00:00")]
    [InlineData("string(xs:date(xs:untypedAtomic('2020-01-02')))", "2020-01-02")]
    [InlineData("string(xs:gYear(xs:date('2020-01-01')))", "2020")]
    public async Task A_permitted_source_type_still_converts(string query, string expected) =>
        (await _facade.EvaluateAsync(query)).Should().Be(expected);

    [Fact]
    public async Task A_lexically_invalid_string_is_still_FORG0001() =>
        (await CodeOf("xs:date('not a date')")).Should().Be("FORG0001");

    [Fact]
    public async Task An_unbound_external_variable_is_XPDY0002() =>
        (await CodeOf("declare variable $x external; $x")).Should().Be("XPDY0002");
}
