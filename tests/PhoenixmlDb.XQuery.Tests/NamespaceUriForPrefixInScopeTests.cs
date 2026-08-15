using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// <c>fn:namespace-uri-for-prefix($prefix, $element)</c> resolves against the element's
/// IN-SCOPE namespaces (F&amp;O §14), which include bindings inherited from ancestors — not
/// only the bindings the element declares itself.
///
/// It previously read just <c>element.NamespaceDeclarations</c> plus the element's own prefix,
/// so an inherited prefix returned the empty sequence while <c>fn:in-scope-prefixes</c> — which
/// does walk ancestors — happily listed it. Pairing the two is the idiomatic way to copy
/// namespaces:
/// <code>
///   for-each(in-scope-prefixes($e)) { xsl:namespace name="{.}"
///                                     select="namespace-uri-for-prefix(., $e)" }
/// </code>
/// and it raised <c>XTDE0930</c> ("zero-length string, but a prefix was specified") on the first
/// inherited binding. That is XSpec's <c>x:copy-of-namespaces</c>; the failure accounted for 103
/// of the 162 XSLT suites in the census.
///
/// Both functions and the <c>namespace::</c> axis now share
/// <c>GatherInScopeNamespaces</c>, so they cannot disagree.
/// </summary>
public class NamespaceUriForPrefixInScopeTests
{
    private readonly XQueryFacade _facade = new();

    private const string Doc =
        """parse-xml('<doc xmlns:a="urn:a"><inner xmlns:c="urn:c"><deep/></inner></doc>')//deep""";

    [Theory]
    // Declared two levels up, one level up, and the always-bound xml prefix.
    [InlineData("a", "urn:a")]
    [InlineData("c", "urn:c")]
    [InlineData("xml", "http://www.w3.org/XML/1998/namespace")]
    public async Task Resolves_a_prefix_inherited_from_an_ancestor(string prefix, string expected)
    {
        var result = await _facade.EvaluateAsync(
            $"""string(namespace-uri-for-prefix('{prefix}', {Doc}))""");
        result.Should().Be(expected);
    }

    [Fact]
    public async Task Unbound_prefix_is_still_the_empty_sequence()
    {
        var result = await _facade.EvaluateAsync(
            $"""count(namespace-uri-for-prefix('nope', {Doc}))""");
        result.Should().Be("0");
    }

    /// <summary>
    /// The invariant behind the bug: every prefix in-scope-prefixes reports must resolve. This is
    /// the shape XSpec's x:copy-of-namespaces relies on.
    /// </summary>
    [Fact]
    public async Task Every_in_scope_prefix_resolves_to_a_uri()
    {
        var result = await _facade.EvaluateAsync(
            $"""
            let $e := {Doc}
            return count(in-scope-prefixes($e)[empty(namespace-uri-for-prefix(., $e))])
            """);
        result.Should().Be("0");
    }

    /// <summary>An inner re-binding of the same prefix must win over the ancestor's.</summary>
    [Fact]
    public async Task Nearest_binding_wins_over_an_ancestor_binding()
    {
        var result = await _facade.EvaluateAsync(
            """
            string(namespace-uri-for-prefix('p',
              parse-xml('<doc xmlns:p="urn:outer"><inner xmlns:p="urn:inner"><deep/></inner></doc>')//deep))
            """);
        result.Should().Be("urn:inner");
    }

    /// <summary>
    /// An absent default namespace is the empty sequence, not a zero-length URI — including when
    /// an ancestor's default has been undeclared with xmlns="".
    /// </summary>
    [Theory]
    // No namespaces at all, and an ancestor default explicitly undeclared with xmlns="".
    // The undeclared case reaches the shared gather as an in-scope prefix bound to the empty
    // URI, so it must be filtered on the RESOLVED value, not on NamespaceId.None alone.
    [InlineData("""parse-xml('<doc><deep/></doc>')//deep""")]
    [InlineData("""parse-xml('<doc xmlns="urn:d"><inner xmlns=""><deep/></inner></doc>')//*[local-name()='deep']""")]
    public async Task Absent_default_namespace_is_empty_sequence(string doc)
    {
        var result = await _facade.EvaluateAsync($"count(namespace-uri-for-prefix('', {doc}))");
        result.Should().Be("0");
    }

    /// <summary>A default namespace that IS in scope resolves for the empty prefix.</summary>
    [Fact]
    public async Task Inherited_default_namespace_resolves_for_the_empty_prefix()
    {
        var result = await _facade.EvaluateAsync(
            """
            string(namespace-uri-for-prefix('',
              parse-xml('<doc xmlns="urn:d"><inner><deep/></inner></doc>')//*[local-name()='deep']))
            """);
        result.Should().Be("urn:d");
    }
}
