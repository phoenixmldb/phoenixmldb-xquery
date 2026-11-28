using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.Parser;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// Two function declarations are the same function when their expanded names and arities are equal
/// (XQST0034, XQuery 3.1 §4.18). The check at parse time compared the prefix: a Q{uri}local name has
/// none, so Q{urn:a}f and Q{urn:b}f were one signature and the module was refused.
/// </summary>
public class FunctionDeclarationNameTests
{
    private static (bool Success, string? Code, QueryEngine Engine) Compile(string query)
    {
        var engine = new QueryEngine();
        try
        {
            var compiled = engine.Compile(query);
            return (compiled.Success, compiled.Success ? null : compiled.Errors[0].Code, engine);
        }
        catch (XQueryParseException e)
        {
            return (false, e.ErrorCode, engine);
        }
    }

    [Fact]
    public async Task Functions_with_one_local_name_in_two_namespaces_are_two_functions()
    {
        const string Query = "declare function Q{urn:a}f() { 1 }; declare function Q{urn:b}f() { 2 }; Q{urn:a}f() * 10 + Q{urn:b}f()";
        var (success, code, engine) = Compile(Query);
        success.Should().BeTrue(code);

        var items = new List<object?>();
        await foreach (var item in engine.ExecuteAsync(engine.Compile(Query).ExecutionPlan!))
            items.Add(item);
        items.Should().Equal(12L);
    }

    [Theory]
    [InlineData("declare function Q{urn:a}f() { 1 }; declare function Q{urn:a}f() { 2 }; 1")]
    [InlineData("declare namespace p = 'urn:a'; declare function p:f() { 1 }; declare function p:f() { 2 }; 1")]
    public void Two_declarations_of_one_name_and_arity_are_XQST0034(string query)
        => Compile(query).Code.Should().Be("XQST0034");
}
