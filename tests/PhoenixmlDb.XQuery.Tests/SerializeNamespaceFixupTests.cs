using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// fn:serialize writes markup that reads back to the same names: an element (or prefixed
/// attribute) whose namespace differs from the binding in force where it is written gets a
/// declaration, including xmlns="" for an unprefixed no-namespace element under a default.
/// </summary>
/// <remarks>
/// Only the recorded namespace declarations were written. A constructed element records its
/// in-scope set, which need not mention a default namespace its parent printed, so a
/// no-namespace child under &lt;root xmlns="urn:x"&gt; came out as &lt;foo/&gt; and was read
/// back in urn:x. In XQuery any copy-namespaces mode other than the default "preserve,
/// inherit" built that shape; from XSLT, an untyped variable built with xsl:copy-of did.
/// Saxon writes &lt;foo xmlns=""/&gt;.
/// </remarks>
public class SerializeNamespaceFixupTests
{
    private readonly XQueryFacade _facade = new();

    [Theory]
    [InlineData("preserve, no-inherit")]
    [InlineData("no-preserve, no-inherit")]
    [InlineData("no-preserve, inherit")]
    [InlineData("preserve, inherit")]
    public async Task NoNamespaceElement_ReadsBackInNoNamespace(string copyNamespaces)
        => (await _facade.EvaluateAsync($"""
                declare copy-namespaces {copyNamespaces};
                let $p := <x:p xmlns:x="urn:x"><foo/></x:p>
                let $t := <root xmlns="urn:x">{"{"}$p{"}"}</root>
                return namespace-uri(parse-xml(serialize($t))//*:foo)
                """))
            .Should().BeEmpty();

    [Fact]
    public async Task EveryName_RoundTrips()
        => (await _facade.EvaluateAsync("""
                let $a := <a:e xmlns:a="urn:a" b:at="1" xmlns:b="urn:b"><c/></a:e>
                let $t := <r xmlns="urn:d" xmlns:a="urn:other">{$a}</r>
                let $back := parse-xml(serialize($t))
                return deep-equal(
                  $t/descendant-or-self::* ! (local-name() || '=' || namespace-uri()),
                  $back//* ! (local-name() || '=' || namespace-uri()))
                  and $back//@*:at/namespace-uri() = 'urn:b'
                """))
            .Should().Be("true");
}
