using FluentAssertions;
using PhoenixmlDb.XQuery;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// fn:in-scope-namespaces read only an element's OWN declarations and resolved ids through the
/// static well-known table, so a binding declared on an ancestor, or any namespace interned by the
/// document rather than predefined, was missing — even for an uncopied parsed element. It now uses
/// the shared in-scope walk and namespace-uri-for-prefix's id resolution.
/// </summary>
public class InScopeNamespacesFunctionTests
{
    [Fact]
    public async Task Parsed_element_sees_a_binding_declared_on_its_ancestor()
        => (await new XQueryFacade().EvaluateAsync("/*:book/*:e/*:d ! string(in-scope-namespaces(.)?p)",
                "<book xmlns:p=\"urn:q\"><e><p:d/></e></book>"))
            .Should().Be("urn:q");

    [Fact]
    public async Task Agrees_with_in_scope_prefixes_for_a_parsed_element()
        => (await new XQueryFacade().EvaluateAsync(
                "/*:book/*:e/*:d ! (string-join(sort(map:keys(in-scope-namespaces(.))), ' ') = string-join(sort(in-scope-prefixes(.)), ' '))",
                "<book xmlns:p=\"urn:q\"><e><p:d/></e></book>"))
            .Should().Be("true");

    [Fact]
    public async Task Undeclared_default_is_not_an_in_scope_namespace()
        => (await new XQueryFacade().EvaluateAsync("/*:a/*:b ! string-join(sort(map:keys(in-scope-namespaces(.))), ' ')",
                "<a xmlns=\"urn:a\"><b xmlns=\"\"/></a>"))
            .Should().Be("xml");

    [Fact]
    public async Task Constructed_element_does_not_inherit_a_parents_name_usage()
        => (await new XQueryFacade().EvaluateAsync(
                "declare namespace a = \"http://example.com/1\"; declare namespace b = \"http://example.com/2\"; let $e := <e a:n1=\"c\" b:n1=\"c\"><a:n1/></e> return string-join(sort(map:keys(in-scope-namespaces($e/a:n1))), ' ')",
                "<x/>"))
            .Should().Be("a xml");
}
