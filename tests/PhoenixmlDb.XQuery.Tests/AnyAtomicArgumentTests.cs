using AwesomeAssertions;
using PhoenixmlDb.XQuery;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// The function conversion rules for an argument (XPath 3.1 §3.1.5.2) with an atomic parameter
/// type: a node is atomized, and an xs:untypedAtomic value is not cast to a namespace-sensitive
/// type.
/// </summary>
public class AnyAtomicArgumentTests
{
    private static Task<string> Eval(string q) => new XQueryFacade().EvaluateAsync(q, inputXml: null);

    /// <summary>
    /// A node passed for xs:anyAtomicType is atomized, and nothing is cast: an element validated
    /// as xs:integer gives an xs:integer, an untyped one xs:untypedAtomic. The coercion of
    /// arguments skipped xs:anyAtomicType, so the node itself was bound (QT3 FunctionCall-005/007).
    /// </summary>
    [Fact]
    public async Task A_node_for_any_atomic_type_is_atomized()
        => (await Eval("""
            declare function local:f($in as xs:anyAtomicType) as xs:string {
              string-join(($in instance of xs:integer, $in instance of xs:untypedAtomic, $in instance of node()) ! string(), ' ')
            };
            string-join((local:f(validate type xs:integer { <a>12</a> }), local:f(<b>x</b>),
              string((function($in as xs:anyAtomicType) { $in instance of xs:untypedAtomic })(<c>y</c>))), '|')
            """)).Should().Be("true false false|false true false|true");

    /// <summary>
    /// An xs:untypedAtomic value for the xs:QName parameter of fn:local-name-from-QName and its two
    /// companions is XPTY0117: it is not cast to a namespace-sensitive type (QT3 FunctionCall-018).
    /// It was XPTY0004.
    /// </summary>
    [Theory]
    [InlineData("local-name-from-QName")]
    [InlineData("namespace-uri-from-QName")]
    [InlineData("prefix-from-QName")]
    public async Task An_untyped_value_for_a_QName_parameter_is_XPTY0117(string function)
    {
        var act = () => Eval($"{function}(<a xmlns:p='urn:p'>p:l</a>)");
        (await act.Should().ThrowAsync<PhoenixmlDb.XQuery.Functions.XQueryException>()).Which.ErrorCode.Should().Be("XPTY0117");
    }
}
