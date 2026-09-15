using FluentAssertions;
using PhoenixmlDb.XQuery;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// An element copied into a constructor inherits (copy-namespaces inherit) the constructor's in-scope
/// namespaces — including those added by a computed namespace constructor in its content. Those were
/// collected for the constructor itself but not passed to the copy, so
/// <c>element e { namespace new {"…"}, $nested }/outer/inner</c> had no <c>new</c> binding
/// (QT3 nscons-031, -033, -035..-037, -039). The no-inherit and own-binding tests are guards.
/// </summary>
public class ComputedNamespaceInheritanceTests
{
    private const string Nested = "let $nested := element outer { element inner {} } ";

    private static Task<string> Eval(string query) => new XQueryFacade().EvaluateAsync(query, "<x/>");

    [Fact]
    public async Task Copy_inherits_computed_namespace_under_preserve_inherit()
        => (await Eval("declare copy-namespaces preserve, inherit; " + Nested
                + "return (element e { namespace new {'urn:new'}, $nested }/outer/inner) ! string-join(sort(in-scope-prefixes(.)), ' ')"))
            .Should().Be("new xml");

    [Fact]
    public async Task Copy_inherits_computed_namespace_under_no_preserve_inherit()
        => (await Eval("declare copy-namespaces no-preserve, inherit; " + Nested
                + "return (element e { namespace new {'urn:new'}, $nested }/outer/inner) ! string-join(sort(in-scope-prefixes(.)), ' ')"))
            .Should().Be("new xml");

    [Fact]
    public async Task Copy_resolves_inherited_computed_prefix()
        => (await Eval(Nested + "return (element e { namespace new {'urn:new'}, $nested }/outer) ! namespace-uri-for-prefix('new', .)"))
            .Should().Be("urn:new");

    [Fact]
    public async Task Recursively_built_element_inherits_every_level_computed_namespace()
        => (await Eval("""
                declare copy-namespaces preserve, inherit;
                declare function local:f($level as xs:integer) as element() {
                  if ($level > 0)
                  then element { concat('e', $level) } { namespace { concat('p', $level) } { concat('urn:', $level) }, local:f($level - 1) }
                  else element e0 {}
                };
                local:f(2)/e1/e0 ! string-join(sort(in-scope-prefixes(.)), ' ')
                """))
            .Should().Be("p1 p2 xml");

    [Fact]
    public async Task Copy_under_no_inherit_does_not_see_computed_namespace()
        => (await Eval("declare copy-namespaces preserve, no-inherit; " + Nested
                + "return (element e { namespace new {'urn:new'}, $nested }/outer) ! string-join(sort(in-scope-prefixes(.)), ' ')"))
            .Should().Be("xml");

    [Fact]
    public async Task Copy_keeps_its_own_binding_over_a_computed_one_for_the_same_prefix()
        => (await Eval("let $own := <outer xmlns:p='urn:own'/> return (element e { namespace p {'urn:new'}, $own }/outer) ! namespace-uri-for-prefix('p', .)"))
            .Should().Be("urn:own");

    [Fact]
    public async Task Serialized_copy_declares_inherited_computed_namespace()
        => (await Eval("declare copy-namespaces no-preserve, inherit; " + Nested
                + "return element e { namespace new {'urn:new'}, $nested }/outer/inner"))
            .Should().Contain("xmlns:new=\"urn:new\"");
}
