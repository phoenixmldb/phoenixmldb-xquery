using AwesomeAssertions;
using PhoenixmlDb.XQuery;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Functions;

/// <summary>
/// A constructor function is <c>xs:T($arg as xs:anyAtomicType?) as xs:T?</c>, and for a list type
/// returns its item type's sequence (XPath 3.1 §3.14.2); the function item <c>xs:T#1</c> carries
/// that signature. It carried <c>item()</c>, so a typed binding of it was refused. A list type is
/// cast to only from one xs:string or xs:untypedAtomic: an xs:anyURI, or two strings, is XPTY0004
/// (QT3 CastAs-ListType-17..19, -26, -27).
/// </summary>
public class ConstructorSignatureTests
{
    private static Task<string> Eval(string q) => new XQueryFacade().EvaluateAsync(q, inputXml: null);

    [Theory]
    [InlineData("let $f as function(xs:anyAtomicType) as xs:NMTOKEN* := xs:NMTOKENS#1 return count($f('a b c'))", "3")]
    [InlineData("xs:integer#1 instance of function(xs:anyAtomicType?) as xs:integer?", "true")]
    [InlineData("xs:IDREFS#1 instance of function(xs:string) as xs:IDREF*", "true")]
    [InlineData("xs:integer#1 instance of function(xs:string) as xs:integer", "false")]
    public async Task A_constructor_function_item_has_the_signature_of_its_type(string query, string expected)
        => (await Eval("string(" + query + ")")).Should().Be(expected);

    [Theory]
    [InlineData("xs:ENTITIES(xs:anyURI('abcd'))")]
    [InlineData("let $f := xs:NMTOKENS#1 return $f(('a', 'b'))")]
    public async Task A_list_type_is_cast_to_only_from_one_string(string query)
    {
        var act = () => Eval(query);
        (await act.Should().ThrowAsync<PhoenixmlDb.XQuery.Execution.XQueryRuntimeException>()).Which.ErrorCode.Should().Be("XPTY0004");
    }

    [Theory]
    [InlineData("count(xs:NMTOKENS(' a b '))", "2")]
    [InlineData("count(xs:IDREFS(xs:untypedAtomic('x y')))", "2")]
    [InlineData("count(xs:ENTITIES(xs:token('e')))", "1")]
    public async Task A_list_type_is_still_cast_to_from_a_string(string query, string expected)
        => (await Eval("string(" + query + ")")).Should().Be(expected);
}
