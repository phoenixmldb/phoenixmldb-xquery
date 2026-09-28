using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// Operations on a value whose type is a SUBTYPE of xs:string (xs:NCName, xs:language, xs:token,
/// …). xquery#83 made fn:prefix-from-QName and fn:default-language return those types, as the spec
/// requires, and XSpec's compiler failed the same day (xslt#191): `if (prefix-from-QName(...))`
/// raised FORG0006, because effective boolean value had no case for a string subtype. The sweep
/// that followed found three more type switches with the same gap.
/// </summary>
public class StringSubtypeOperationTests
{
    private readonly XQueryFacade _facade = new();
    private const string NC = "prefix-from-QName(QName('u', 'p:a'))";   // xs:NCName 'p'

    [Theory]
    // Effective boolean value: xs:string or DERIVED from it, true iff non-empty (xslt#191).
    [InlineData($"if ({NC}) then 'T' else 'F'", "T")]
    [InlineData("if (default-language()) then 'T' else 'F'", "T")]
    [InlineData("if (xs:NCName('a')) then 'T' else 'F'", "T")]
    [InlineData("boolean(xs:token(''))", "false")]
    [InlineData($"(1, 2)[{NC}] => count()", "2")]
    // fn:translate rejected a string subtype with XPTY0004.
    [InlineData($"translate({NC}, 'p', 'q')", "q")]
    // Map keys: a string subtype and an equal xs:string are the same key (op:same-key).
    [InlineData($"map{{ {NC} : 1 }}?p", "1")]
    [InlineData($"map:get(map{{ 'p' : 1 }}, {NC})", "1")]
    [InlineData($"map:size(map:merge((map{{ 'p' : 1 }}, map{{ {NC} : 2 }})))", "1")]
    // fn:collation-key rejected it too.
    [InlineData($"collation-key({NC}) instance of xs:base64Binary", "true")]
    public async Task A_string_subtype_behaves_as_an_xs_string(string query, string expected)
        => (await _facade.EvaluateAsync(query)).Should().Be(expected);
}
