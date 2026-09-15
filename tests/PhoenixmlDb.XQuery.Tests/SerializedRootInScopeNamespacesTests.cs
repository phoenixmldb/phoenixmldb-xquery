using FluentAssertions;
using PhoenixmlDb.XQuery;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// A parsed element serialized on its own declares every namespace in scope for it, including those
/// declared on its ancestors in the source document. A parsed element records only the xmlns
/// attributes physically on it, and the serializers wrote just those, so /book/e from
/// &lt;book xmlns:p="urn:q"&gt; serialized without xmlns:p (#57; QT3 fn-union-node-args-015..017,
/// fn-intersect-node-args-015, -016). The documents avoid descendants that use the prefix themselves:
/// XmlWriter declares a prefix wherever an element name needs it, which would hide the missing
/// declaration. The adaptive, whole-document and undeclaration tests are guards.
/// </summary>
public class SerializedRootInScopeNamespacesTests
{
    private const string UnusedPrefixDoc = "<book xmlns:p=\"urn:q\"><e><f/></e></book>";
    private const string UsedPrefixDoc = "<book xmlns:p=\"urn:q\"><e><p:d/></e></book>";
    private const string UndeclaredDoc = "<book xmlns=\"urn:b\"><e xmlns=\"\"><c/></e></book>";
    private const string XmlMethod = "declare namespace output = 'http://www.w3.org/2010/xslt-xquery-serialization'; "
        + "declare option output:method 'xml'; declare option output:omit-xml-declaration 'yes'; ";

    private static Task<string> Eval(string query, string doc) => new XQueryFacade().EvaluateAsync(query, doc);

    private static int Occurrences(string text, string value)
        => (text.Length - text.Replace(value, "", StringComparison.Ordinal).Length) / value.Length;

    [Fact]
    public async Task Parsed_element_serialized_alone_declares_prefix_from_ancestor()
        => (await Eval(XmlMethod + "/book/e", UnusedPrefixDoc)).Should().Be("<e xmlns:p=\"urn:q\"><f/></e>");

    [Fact]
    public async Task Root_declares_prefix_once_instead_of_on_the_descendant_that_uses_it()
        => (await Eval(XmlMethod + "/book/e", UsedPrefixDoc)).Should().Be("<e xmlns:p=\"urn:q\"><p:d/></e>");

    [Fact]
    public async Task Fn_serialize_of_parsed_element_declares_prefix_from_ancestor()
        => (await Eval("serialize(/book/e)", UnusedPrefixDoc)).Should().Contain("xmlns:p=\"urn:q\"");

    [Fact]
    public async Task Adaptive_serialization_of_parsed_element_declares_prefix_from_ancestor()
        => (await Eval("/book/e", UnusedPrefixDoc)).Should().Contain("xmlns:p=\"urn:q\"");

    [Fact]
    public async Task Whole_document_still_declares_each_binding_once()
        => Occurrences(await Eval(XmlMethod + "/", UsedPrefixDoc), "xmlns:p=").Should().Be(1);

    [Fact]
    public async Task Element_below_an_undeclared_default_declares_no_default()
        => (await Eval(XmlMethod + "//c", UndeclaredDoc)).Should().NotContain("urn:b");
}
