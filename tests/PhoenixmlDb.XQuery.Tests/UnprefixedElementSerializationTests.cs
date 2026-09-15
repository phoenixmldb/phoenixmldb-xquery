using FluentAssertions;
using PhoenixmlDb.XQuery;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// Serializing with the xml method under an element that binds a prefix to the same URI as an unprefixed
/// element. XmlWriter.WriteStartElement(localName, ns) reuses a prefix already in scope for ns, so
/// &lt;x xmlns="urn:m"&gt; copied into &lt;Q xmlns:m="urn:m"&gt; was written &lt;m:x&gt; (#57, QT3
/// ns-queries-results-q5). And the skip for a declaration already in scope asked XmlWriter.LookupPrefix(uri),
/// which names only one prefix per URI, so once the default namespace shared the URI, xmlns:m was written
/// again on every copied descendant. The prefixed-element test is a guard.
/// </summary>
public class UnprefixedElementSerializationTests
{
    private const string Doc = "<doc><x xmlns=\"urn:m\"><y/></x></doc>";
    private const string XmlMethod = "declare namespace output = 'http://www.w3.org/2010/xslt-xquery-serialization'; "
        + "declare option output:method 'xml'; declare option output:omit-xml-declaration 'yes'; ";

    private static Task<string> Eval(string query, string doc) => new XQueryFacade().EvaluateAsync(query, doc);

    [Fact]
    public async Task Unprefixed_element_under_prefix_bound_to_its_namespace_stays_unprefixed()
        => (await Eval(XmlMethod + "<Q xmlns:m=\"urn:m\">{/doc/*}</Q>", Doc))
            .Should().NotContain("<m:x").And.NotContain("<m:y").And.Contain("<x ");

    [Fact]
    public async Task Copied_descendants_do_not_redeclare_a_prefix_already_in_scope()
        => (await Eval(XmlMethod + "<Q xmlns:m=\"urn:m\">{/doc/*}</Q>", Doc))
            .Should().Be("<Q xmlns:m=\"urn:m\"><x xmlns=\"urn:m\"><y/></x></Q>");

    [Fact]
    public async Task Prefixed_element_keeps_its_prefix()
        => (await Eval(XmlMethod + "<Q xmlns:m=\"urn:m\">{<m:z/>}</Q>", "<x/>"))
            .Should().Contain("<m:z");
}
