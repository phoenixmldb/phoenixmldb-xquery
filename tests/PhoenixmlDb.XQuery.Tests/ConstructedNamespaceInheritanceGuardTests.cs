using FluentAssertions;
using PhoenixmlDb.XQuery;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// For a directly constructed element, a namespace used only by an ENCLOSING element's or
/// attribute's name is not in scope for its children; namespace declaration attributes are
/// (XQuery 3.1 §3.9.1.3). The constructor materialises exactly that set on each element, which is
/// why the in-scope walk must not climb into constructed ancestors. Mirrors W3C QT3 K2-NameTest-30
/// and cbcl-directconelem-001/002, which caught an attempt to make every element walk ancestors.
/// </summary>
public class ConstructedNamespaceInheritanceGuardTests
{
    private const string Prolog = "declare namespace a = \"http://example.com/1\"; declare namespace b = \"http://example.com/2\"; ";
    private static Task<string> Eval(string q) => new XQueryFacade().EvaluateAsync(q, "<x/>");

    [Fact]
    public async Task A_parents_attribute_name_prefix_is_not_inherited()
        => (await Eval(Prolog + "let $e := <e a:n1=\"c\" b:n1=\"c\"><a:n1/></e> return string(empty(namespace-uri-for-prefix('b', $e/a:n1)))"))
            .Should().Be("true", "b is used only by the parent's attribute name");

    [Fact]
    public async Task Child_prefixes_exclude_a_parents_name_usage()
        => (await Eval(Prolog + "let $e := <e a:n1=\"c\" b:n1=\"c\"><a:n1/></e> return string-join(sort(in-scope-prefixes($e/a:n1)), ' ')"))
            .Should().Be("a xml");

    [Fact]
    public async Task A_parents_declaration_attribute_is_inherited()
        => (await Eval(Prolog + "let $e := <a:outer xmlns:c=\"http://example.com/3\" b:x=\"y\"><inner/></a:outer> return string-join(sort(in-scope-prefixes($e/*)), ' ')"))
            .Should().Be("c xml", "xmlns:c is a declaration; a and b are only used by the outer names");
}
