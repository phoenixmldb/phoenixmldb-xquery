using FluentAssertions;
using PhoenixmlDb.XQuery;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// Copying a subtree into a constructor under copy-namespaces inherit (phoenixmldb-xquery#42). The
/// copy ROOT's declarations were merged correctly with the enclosing constructor's, but descendants
/// were handed the RAW enclosing bindings. A descendant then declared a namespace contradicting the
/// one it actually has — "The prefix '' cannot be redefined" when that was the default namespace.
/// Descendants must inherit what the root ended up with.
/// </summary>
public class CopiedRootDefaultNamespaceTests
{
    private static Task<string> Eval(string query, string doc) => new XQueryFacade().EvaluateAsync(query, doc);

    [Fact]
    public async Task For_return_copy_of_no_namespace_subtree_into_default_namespace()
        => (await Eval("for $b in /book return <ctor xmlns=\"urn:x\">{$b}</ctor>", "<book><c/></book>"))
            .Should().Be("<ctor xmlns=\"urn:x\"><book xmlns=\"\"><c/></book></ctor>");

    [Fact]
    public async Task Simple_map_copy_of_no_namespace_subtree_into_default_namespace()
        => (await Eval("/book ! <ctor xmlns=\"urn:x\">{.}</ctor>", "<book><c/></book>"))
            .Should().Be("<ctor xmlns=\"urn:x\"><book xmlns=\"\"><c/></book></ctor>");

    [Fact]
    public async Task Copied_descendant_stays_in_no_namespace()
        => (await Eval("/book ! (let $c := <ctor xmlns=\"urn:x\">{.}</ctor>/book/c return concat(count($c), ':', namespace-uri($c)))",
                "<book><c/></book>"))
            .Should().Be("1:", "the copied <c> exists and is in no namespace");

    // /*:book, not /book: the source book is in urn:b, so an unprefixed /book matches nothing and
    // the constructor never runs — which is how the first version of this test passed vacuously.
    [Fact]
    public async Task Root_with_its_own_default_namespace_keeps_it_for_descendants()
        => (await Eval("/*:book ! <ctor xmlns=\"urn:x\">{.}</ctor>", "<book xmlns=\"urn:b\"><c/></book>"))
            .Should().Be("<ctor xmlns=\"urn:x\"><book xmlns=\"urn:b\"><c/></book></ctor>");

    [Fact]
    public async Task Root_with_its_own_default_namespace_round_trips()
        => (await Eval("/*:book ! string-join(parse-xml(serialize(<ctor xmlns=\"urn:x\">{.}</ctor>))//*:c ! namespace-uri(.), ',')",
                "<book xmlns=\"urn:b\"><c/></book>"))
            .Should().Be("urn:b", "the copied <c> must still be in urn:b once serialized and reparsed");

    // The descendant that matters is <c>: it does NOT use p, so nothing stops the propagation from
    // giving it the enclosing p. <p:d> below it must still be in urn:q.
    [Fact]
    public async Task Root_with_its_own_prefix_binding_keeps_it_for_descendants()
        => (await Eval("/*:book ! string-join(<ctor xmlns:p=\"urn:p\">{.}</ctor>//*:d ! namespace-uri(.), ',')",
                "<book xmlns:p=\"urn:q\"><c><p:d/></c></book>"))
            .Should().Be("urn:q");

    [Fact]
    public async Task Root_with_its_own_prefix_binding_round_trips()
        => (await Eval("/*:book ! string-join(parse-xml(serialize(<ctor xmlns:p=\"urn:p\">{.}</ctor>))//*:d ! namespace-uri(.), ',')",
                "<book xmlns:p=\"urn:q\"><c><p:d/></c></book>"))
            .Should().Be("urn:q", "<p:d> must still be in urn:q once serialized and reparsed");

    [Fact]
    public async Task Leaf_copy_bound_outside_the_constructor()
        => (await Eval("let $c := /book/c return <ctor xmlns=\"urn:x\">{$c}</ctor>", "<book><c/></book>"))
            .Should().Be("<ctor xmlns=\"urn:x\"><c xmlns=\"\"/></ctor>");
}
