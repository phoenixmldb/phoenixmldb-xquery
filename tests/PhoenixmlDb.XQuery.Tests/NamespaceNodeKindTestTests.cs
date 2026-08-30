using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// <c>namespace-node()</c> had no member in <c>ItemType</c>, so the parser mapped it to
/// <c>ItemType.Node</c> - the test that matches ANY node. Every one of these returned true.
/// </summary>
/// <remarks>
/// The cost was out of all proportion to the line that caused it. XSpec decides whether it can
/// wrap a result in a document node with
/// <c>$item instance of node() and not($item instance of attribute() or $item instance of
/// namespace-node())</c>. With the second test always true the whole predicate was always false,
/// so no result was ever wrapped, the predicate context item stayed a parentless element, and
/// every assertion written as <c>//foo</c> - the idiomatic XSpec style - could not match. It also
/// produced XPDY0050 for any assertion using <c>/foo</c>, since a parentless element is not
/// rooted at a document node.
/// </remarks>
public class NamespaceNodeKindTestTests
{
    private readonly XQueryFacade _facade = new();
    private async Task<string> Eval(string xq) => await _facade.EvaluateAsync(xq);

    [Theory]
    [InlineData("<e/>", "element")]
    [InlineData("<e a='1'/>/@a", "attribute")]
    [InlineData("<e>t</e>/text()", "text")]
    [InlineData("<e><!--c--></e>/comment()", "comment")]
    [InlineData("document { <e/> }", "document")]
    public async Task NamespaceNodeTest_DoesNotMatchOtherNodeKinds(string expr, string kind)
        => (await Eval($"({expr}) instance of namespace-node()"))
            .Should().Be("false", $"a {kind} node is not a namespace node");

    /// <summary>
    /// The positive case cannot be written here: XQuery has no namespace axis (XQST0134) and no
    /// other way to obtain a namespace node, which is why this kind test is so easy to get wrong
    /// and so hard to notice. Pinning the constraint instead, so the reason stays recorded.
    /// Positive matching belongs in the XSLT engine's tests, where the axis exists.
    /// </summary>
    [Fact]
    public async Task NamespaceAxis_IsRejectedInXQuery()
    {
        var act = async () => await Eval("<e xmlns:p='urn:x'/>/namespace::*");
        (await act.Should().ThrowAsync<Exception>()).Which.Message
            .Should().Contain("XQST0134");
    }

    /// <summary>
    /// XSpec's wrappability predicate, verbatim. This is the expression the bug broke, and it
    /// must be true for an element.
    /// </summary>
    [Fact]
    public async Task XSpecWrappabilityPredicate_IsTrueForAnElement()
        => (await Eval("let $item := <doc>hello</doc> return "
                     + "$item instance of node() and not($item instance of attribute() "
                     + "or $item instance of namespace-node())"))
            .Should().Be("true");
}
