using AwesomeAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Schema;

/// <summary>
/// <see cref="PhoenixmlDb.XQuery.Execution.QueryEngine.InputValidation"/> and <see cref="XQueryFacade.InputValidation"/>
/// validate the input document before the query runs, against the schemas the query imports
/// (#267). The context item and <c>$input</c> are then typed. Without it the input stays
/// untyped, and before there was no way to ask for validation but in the query.
/// </summary>
public sealed class InputValidationTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("phx-input-validation").FullName;

    public InputValidationTests()
        => File.WriteAllText(Path.Combine(_dir, "s.xsd"), """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"><xs:element name="value" type="xs:decimal"/></xs:schema>
            """);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private const string Query = "import schema default element namespace '' at 's.xsd'; declare variable $input external; "
        + "string-join((data(/value) instance of xs:decimal, data($input/value) instance of xs:decimal), ' ')";

    private Task<string> RunAsync(string input, ValidationMode? mode)
        => new XQueryFacade { InputValidation = mode }.EvaluateAsync(Query, input, queryBaseUri: new Uri(Path.Combine(_dir, "q.xq")));

    [Fact]
    public async Task Without_validation_the_input_is_untyped()
        => (await RunAsync("<value>1.5</value>", null)).Should().Be("false false");

    [Theory]
    [InlineData(ValidationMode.Strict)]
    [InlineData(ValidationMode.Lax)]
    public async Task A_valid_input_is_typed(ValidationMode mode)
        => (await RunAsync("<value>1.5</value>", mode)).Should().Be("true true");

    [Theory]
    [InlineData(ValidationMode.Strict)]
    [InlineData(ValidationMode.Lax)]
    public async Task An_invalid_input_is_XQDY0027(ValidationMode mode)
    {
        var act = () => RunAsync("<value></value>", mode);
        (await act.Should().ThrowAsync<PhoenixmlDb.XQuery.Functions.XQueryException>()).Which.ErrorCode.Should().Be("XQDY0027");
    }

    [Fact]
    public async Task Strict_validation_of_an_undeclared_element_is_XQDY0084()
    {
        var act = () => RunAsync("<other/>", ValidationMode.Strict);
        (await act.Should().ThrowAsync<PhoenixmlDb.XQuery.Functions.XQueryException>()).Which.ErrorCode.Should().Be("XQDY0084");
    }
}
