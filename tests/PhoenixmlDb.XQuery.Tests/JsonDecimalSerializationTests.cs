using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// The JSON output method writes a number as its xs:string cast (Serialization 3.1 §10). An
/// xs:decimal keeps the scale of the lexical form it was built from ('15.00'), and that scale
/// must not reach the output (XSLT si-fork-815, Saxon bug 3601).
/// </summary>
public class JsonDecimalSerializationTests
{
    private readonly XQueryFacade _facade = new();

    [Theory]
    [InlineData("xs:decimal('15.00')", "15")]
    [InlineData("xs:decimal('-0.50')", "-0.5")]
    [InlineData("xs:decimal('2.330')", "2.33")]
    [InlineData("[xs:decimal('6.00'), xs:decimal('12.20')]", "[6,12.2]")]
    public async Task Decimal_serializes_in_canonical_form(string expr, string expected)
    {
        var result = await _facade.EvaluateAsync($"declare option output:method \"json\";\n{expr}");
        result.Replace(" ", "", System.StringComparison.Ordinal).Should().Be(expected);
    }
}
