using FluentAssertions;
using PhoenixmlDb.XQuery;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// A copied element keeps the in-scope namespaces of the element it copies (copy-namespaces preserve).
/// The copy ROOT had its source's ancestor bindings materialized, but its descendants kept only the
/// xmlns attributes physically on their source element. A constructed element's declarations are
/// read as its complete in-scope set, so a copied &lt;e&gt; under &lt;book xmlns:p="urn:q"&gt; lost p.
/// The serialization tests guard the other direction: materializing bindings on descendants must
/// not print them again.
/// </summary>
public class CopiedDescendantInScopeNamespacesTests
{
    private const string PrefixedDoc = "<book xmlns:p=\"urn:q\"><e><p:d/></e></book>";
    private const string DefaultDoc = "<book xmlns=\"urn:b\"><e><p:d xmlns:p=\"urn:q\"/></e></book>";
    private const string UndeclaredDoc = "<book xmlns=\"urn:b\"><e xmlns=\"\"><c/></e></book>";
    private const string UndeclaredPrefixedDoc = "<book xmlns=\"urn:b\"><e xmlns=\"\"><p:d xmlns:p=\"urn:q\"/></e></book>";

    private static Task<string> Eval(string query, string doc) => new XQueryFacade().EvaluateAsync(query, doc);

    [Fact]
    public async Task Copied_descendant_has_source_ancestor_prefix_in_scope()
        => (await Eval("(<ctor>{/book}</ctor>//e) ! string-join(sort(in-scope-prefixes(.)), ' ')", PrefixedDoc))
            .Should().Be("p xml");

    // Guard, not a reproduction: fn:parse-xml trees already carried their bindings before this fix.
    [Fact]
    public async Task Copied_descendant_of_parse_xml_tree_has_source_ancestor_prefix_in_scope()
        => (await Eval("(<ctor>{parse-xml('" + PrefixedDoc + "')/*}</ctor>//e) ! string-join(sort(in-scope-prefixes(.)), ' ')", "<x/>"))
            .Should().Be("p xml");

    [Fact]
    public async Task Copied_descendant_resolves_source_ancestor_prefix()
        => (await Eval("(<ctor>{/book}</ctor>//e) ! namespace-uri-for-prefix('p', .)", PrefixedDoc))
            .Should().Be("urn:q");

    [Fact]
    public async Task Copied_descendant_in_scope_namespaces_include_source_ancestor_prefix()
        => (await Eval("(<ctor>{/book}</ctor>//e) ! string(in-scope-namespaces(.)?p)", PrefixedDoc))
            .Should().Be("urn:q");

    // The enclosing constructor binds p to urn:p; the copied <p:d> is still in urn:q and must say so.
    [Fact]
    public async Task Copied_grandchild_under_conflicting_enclosing_prefix_resolves_its_own_binding()
        => (await Eval("(<ctor xmlns:p=\"urn:p\">{/book/e}</ctor>//*:d) ! namespace-uri-for-prefix('p', .)", PrefixedDoc))
            .Should().Be("urn:q");

    [Fact]
    public async Task Copied_descendant_has_source_ancestor_default_namespace_in_scope()
        => (await Eval("(<ctor>{/*:book}</ctor>//*:d) ! namespace-uri-for-prefix('', .)", DefaultDoc))
            .Should().Be("urn:b");

    [Fact]
    public async Task Copied_descendant_below_undeclared_default_has_no_default()
        => (await Eval("(<ctor>{/*:book}</ctor>//c) ! string-join(sort(in-scope-prefixes(.)), ' ')", UndeclaredDoc))
            .Should().Be("xml");

    [Fact]
    public async Task Copied_prefixed_tree_serializes_each_declaration_once()
        => (await Eval("<ctor>{/book}</ctor>", PrefixedDoc))
            .Should().Be("<ctor><book xmlns:p=\"urn:q\"><e><p:d/></e></book></ctor>");

    [Fact]
    public async Task Copied_default_namespace_tree_serializes_each_declaration_once()
        => (await Eval("<ctor>{/*:book}</ctor>", DefaultDoc))
            .Should().Be("<ctor><book xmlns=\"urn:b\"><e><p:d xmlns:p=\"urn:q\"/></e></book></ctor>");

    [Fact]
    public async Task Copied_undeclared_default_tree_serializes_each_declaration_once()
        => (await Eval("<ctor>{/*:book}</ctor>", UndeclaredDoc))
            .Should().Be("<ctor><book xmlns=\"urn:b\"><e xmlns=\"\"><c/></e></book></ctor>");

    [Fact]
    public async Task Copied_prefixed_element_below_undeclared_default_serializes_no_extra_undeclaration()
        => (await Eval("<ctor>{/*:book}</ctor>", UndeclaredPrefixedDoc))
            .Should().Be("<ctor><book xmlns=\"urn:b\"><e xmlns=\"\"><p:d xmlns:p=\"urn:q\"/></e></book></ctor>");

    [Fact]
    public async Task Copied_undeclared_default_tree_fn_serialize_prints_each_declaration_once()
        => (await Eval("serialize(<ctor>{/*:book}</ctor>)", UndeclaredPrefixedDoc))
            .Should().Be("<ctor><book xmlns=\"urn:b\"><e xmlns=\"\"><p:d xmlns:p=\"urn:q\"/></e></book></ctor>");

    [Fact]
    public async Task Copied_tree_fn_serialize_prints_each_declaration_once()
        => (await Eval("serialize(<ctor>{/*:book}</ctor>)", DefaultDoc))
            .Should().Be("<ctor><book xmlns=\"urn:b\"><e><p:d xmlns:p=\"urn:q\"/></e></book></ctor>");
}
