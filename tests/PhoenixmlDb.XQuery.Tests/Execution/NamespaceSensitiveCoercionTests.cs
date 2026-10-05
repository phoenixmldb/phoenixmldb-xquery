using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// Function coercion never casts xs:untypedAtomic to a namespace-sensitive type: there is no
/// namespace context to resolve the prefix in (XPath 3.1 §3.1.5.2, XPTY0117).
/// </summary>
/// <remarks>
/// Only xs:QName was checked. An untyped argument for an xs:NOTATION parameter, or one whose
/// schema type derives from xs:QName or xs:NOTATION, reached the cast and failed as XPTY0004
/// (QT3 CastAsNamespaceSensitiveType-4/5 here; the schema-derived 8/9/11/12 are QT3's).
/// </remarks>
public class NamespaceSensitiveCoercionTests
{
    private readonly XQueryFacade _facade = new();

    private const string Notation = "declare function local:f($q as xs:NOTATION) { 'reached' }; ";
    private const string QName = "declare function local:f($q as xs:QName) { 'reached' }; ";

    [Theory]
    [InlineData(Notation + "local:f(xs:untypedAtomic('xs:integer'))")]
    [InlineData(Notation + "local:f(<tag>xs:integer</tag>)")]
    // Control: xs:QName was already checked.
    [InlineData(QName + "local:f(xs:untypedAtomic('xs:integer'))")]
    public async Task AnUntypedArgument_IsXPTY0117(string query)
    {
        var act = () => _facade.EvaluateAsync(query);
        (await act.Should().ThrowAsync<PhoenixmlDb.XQuery.Execution.XQueryRuntimeException>())
            .Which.ErrorCode.Should().Be("XPTY0117");
    }

    [Fact]
    public async Task AStringArgument_IsStillXPTY0004()
    {
        var act = () => _facade.EvaluateAsync(Notation + "local:f('xs:integer')");
        (await act.Should().ThrowAsync<PhoenixmlDb.XQuery.Execution.XQueryRuntimeException>())
            .Which.ErrorCode.Should().Be("XPTY0004");
    }

    [Fact]
    public async Task ATypedQName_IsAccepted()
        => (await _facade.EvaluateAsync(QName + "local:f(xs:QName('xs:integer'))")).Should().Be("reached");
}
