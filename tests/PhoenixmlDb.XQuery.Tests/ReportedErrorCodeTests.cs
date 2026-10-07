using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.Parser;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// The code an error carries, as opposed to a code its text mentions. The QT3 harness used to
/// accept an expected code found anywhere in the message, which hid each of these.
/// </summary>
public sealed class ReportedErrorCodeTests
{
    private static async Task<Exception?> RunAsync(string query)
    {
        var store = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store);
        try
        {
            var compiled = engine.Compile(query);
            if (!compiled.Success)
                return new XQueryRuntimeException(compiled.Errors[0].Code, compiled.Errors[0].Message);
            await foreach (var _ in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext())) { }
            return null;
        }
        catch (Exception ex) when (ex is XQueryRuntimeException or XQueryParseException or PhoenixmlDb.XQuery.Functions.XQueryException)
        {
            return ex;
        }
    }

    /// <summary>
    /// A well-formed date whose year is past the supported range is FODT0001 (F&amp;O 3.1 §10),
    /// not FORG0001 "invalid lexical form" with FODT0001 in the text.
    /// </summary>
    [Theory]
    [InlineData("xs:date('25252734927766555-05-12')")]
    [InlineData("xs:dateTime('25252734927766555-05-12T12:59:00')")]
    [InlineData("'25252734927766555-05-12' cast as xs:date")]
    [InlineData("xs:untypedAtomic('-25252734927766554-12-31T12:00:00') cast as xs:dateTime")]
    public async Task A_year_past_the_supported_range_is_FODT0001(string query)
    {
        (await RunAsync(query)).Should().BeOfType<XQueryRuntimeException>().Which.ErrorCode.Should().Be("FODT0001");
    }

    [Fact]
    public async Task A_malformed_date_is_still_FORG0001()
    {
        (await RunAsync("xs:date('2024-13-45')")).Should().BeOfType<XQueryRuntimeException>().Which.ErrorCode.Should().Be("FORG0001");
    }

    /// <summary>
    /// A reserved function name is not a function name in a named function reference either
    /// (XQuery 3.1 §A.3): a syntax error, where it was XPST0017 "unknown function".
    /// </summary>
    [Theory]
    [InlineData("attribute#0")]
    [InlineData("empty-sequence#0")]
    [InlineData("typeswitch#1")]
    [InlineData("map#2")]
    public async Task A_reserved_function_name_in_a_function_reference_is_a_syntax_error(string query)
    {
        (await RunAsync(query)).Should().BeOfType<XQueryParseException>().Which.ErrorCode.Should().Be("XPST0003");
    }

    [Fact]
    public async Task A_prefixed_reference_to_such_a_name_is_an_ordinary_unknown_function()
    {
        (await RunAsync("fn:attribute#0")).Should().BeOfType<XQueryRuntimeException>().Which.ErrorCode.Should().Be("XPST0017");
    }

    /// <summary>The parse exception carries the code its message leads with.</summary>
    [Theory]
    [InlineData("1 +", "XPST0003")]
    [InlineData("1 cast as unbound:t", "XPST0081")]
    [InlineData("99999999999999999999999999999999999999999999999999999999999999999999999999999999999999.1", "FOAR0002")]
    public async Task A_parse_error_reports_its_code(string query, string code)
    {
        (await RunAsync(query)).Should().BeOfType<XQueryParseException>().Which.ErrorCode.Should().Be(code);
    }
}
