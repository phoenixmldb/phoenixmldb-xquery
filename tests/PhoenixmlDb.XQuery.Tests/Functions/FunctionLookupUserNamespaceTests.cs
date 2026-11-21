using AwesomeAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Functions;

/// <summary>
/// function-lookup finds a function the query declares in its own namespace (BUGS.md #131).
/// </summary>
/// <remarks>
/// The library matched the looked-up QName by its namespace id, and the id of a QName made at run
/// time is not the one the declaration was registered under. Its URI fallback knew only the URIs
/// that XSLT registered, so a declared XQuery function was never found: function-lookup returned
/// the empty sequence. Present in 3.2.0.
/// </remarks>
public class FunctionLookupUserNamespaceTests
{
    private readonly XQueryFacade _facade = new();

    private const string Prolog = """declare namespace u = "urn:u"; declare function u:g($x) { $x * 2 }; """;

    [Theory]
    [InlineData("""function-lookup(QName("urn:u", "g"), 1)(4)""", "8")]
    [InlineData("""function-lookup(QName("urn:u", "v:g"), 1)(4)""", "8")]
    [InlineData("""function-lookup(xs:QName("u:g"), 1)(4)""", "8")]
    [InlineData("""empty(function-lookup(QName("urn:u", "g"), 2))""", "true")]
    [InlineData("""empty(function-lookup(QName("urn:other", "g"), 1))""", "true")]
    public async Task Declared_function_is_found_by_its_uri(string call, string expected)
    {
        (await _facade.EvaluateAsync(Prolog + call)).Should().Be(expected);
    }
}
