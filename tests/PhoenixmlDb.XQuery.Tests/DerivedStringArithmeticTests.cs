using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// Arithmetic is not defined for xs:string or any type derived from it (XPTY0004). Derived
/// strings, and the result of <c>cast as xs:string</c>, are XsTypedString values; the operand
/// check tested only for a plain string, so they fell through to numeric conversion and
/// <c>xs:token('') + 1.0e0</c> returned NaN (QT3 op-numeric-add-16).
/// </summary>
public class DerivedStringArithmeticTests
{
    private readonly XQueryFacade _facade = new();

    [Theory]
    [InlineData("xs:token('') + 1.0e0")]
    [InlineData("('' cast as xs:string) + 1.0e0")]
    [InlineData("xs:float(15) * xs:NCName('a')")]
    [InlineData("xs:normalizedString('1') - 1")]
    public async Task A_derived_string_operand_is_XPTY0004(string query)
    {
        var act = () => _facade.EvaluateAsync(query);
        (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be("XPTY0004");
    }
}
