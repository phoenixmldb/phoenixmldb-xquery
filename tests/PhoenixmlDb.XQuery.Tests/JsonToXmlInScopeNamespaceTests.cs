using AwesomeAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// Every element json-to-xml makes has the default namespace http://www.w3.org/2005/xpath-functions
/// in scope. The elements are constructed, so the in-scope set is read from each element's own
/// declarations, and only the root had one: a child's in-scope prefixes were "xml" alone (W3C XSLT
/// static-030, json-to-xml(...)//namespace::*). Expected values are Saxon-HE 12.10's.
/// </summary>
public sealed class JsonToXmlInScopeNamespaceTests
{
    private readonly XQueryFacade _facade = new();

    [Fact]
    public async Task Every_element_has_the_default_namespace_in_scope()
        => (await _facade.EvaluateAsync("""
                string-join(for $e in json-to-xml('{"a":1, "b":[true, null, "s"]}')//*
                  return local-name($e) || '(' || string-join(sort(in-scope-prefixes($e)), ',') || ')', ' ')
                """))
            .Should().Be("map(,xml) number(,xml) array(,xml) boolean(,xml) null(,xml) string(,xml)");

    [Fact]
    public async Task The_namespace_is_declared_once_when_serialized()
        => (await _facade.EvaluateAsync("""serialize(json-to-xml('{"a":1, "b":[true, null, "s"]}'))"""))
            .Should().Be("""<map xmlns="http://www.w3.org/2005/xpath-functions"><number key="a">1</number><array key="b"><boolean>true</boolean><null/><string>s</string></array></map>""");
}
