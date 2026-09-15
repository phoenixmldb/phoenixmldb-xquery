using FluentAssertions;
using PhoenixmlDb.XQuery;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// The XML output method writes a declaration for a bare element only when
/// SerializationOptions.ForceXmlDeclaration is set, which its documentation says happens when
/// omit-xml-declaration is explicitly "no" or a standalone value is requested. Nothing set it — not the
/// prolog reader, not fn:serialize's parameter reader — so <c>&lt;a/&gt;</c> under
/// omit-xml-declaration "no" came out as bare <c>&lt;a/&gt;</c> (QT3 K2-Serialization-18/22/23/24).
/// </summary>
public class ForceXmlDeclarationTests
{
    private const string Output = "declare namespace output = \"http://www.w3.org/2010/xslt-xquery-serialization\"; declare option output:method \"xml\"; ";

    private static Task<string> Eval(string query) => new XQueryFacade().EvaluateAsync(query);

    [Theory]
    [InlineData("declare option output:omit-xml-declaration \"no\"; ", true)]
    [InlineData("declare option output:standalone \"yes\"; ", true)]
    [InlineData("declare option output:standalone \"no\"; ", true)]
    [InlineData("declare option output:standalone \"omit\"; ", true)]
    [InlineData("declare option output:omit-xml-declaration \"yes\"; ", false)]
    [InlineData("", false)]
    public void Prolog_reader_sets_force_when_a_declaration_is_requested(string prolog, bool expected)
        => XQueryFacade.DetectSerializationOptions(Output + prolog + "<a/>").ForceXmlDeclaration.Should().Be(expected);

    [Fact]
    public async Task Omit_xml_declaration_no_writes_a_declaration_for_an_element()
    {
        var result = await Eval(Output + "declare option output:omit-xml-declaration \"no\"; <a/>");
        result.Should().StartWith("<?xml").And.EndWith("<a/>");
    }

    [Fact]
    public async Task Standalone_yes_is_written_in_the_declaration()
        => (await Eval(Output + "declare option output:standalone \"yes\"; <a/>"))
            .Should().MatchRegex("^<\\?xml version=\"1\\.0\"[^>]*standalone=\"yes\"\\?>");

    [Fact]
    public async Task Standalone_omit_writes_a_declaration_without_standalone()
    {
        var result = await Eval(Output + "declare option output:standalone \"omit\"; <a/>");
        result.Should().StartWith("<?xml").And.NotContain("standalone");
    }

    // Guard: without a request the facade still returns bare markup for an element.
    [Fact]
    public async Task No_declaration_requested_leaves_element_output_bare()
        => (await Eval(Output + "<a/>")).Should().Be("<a/>");

    [Fact]
    public async Task Fn_serialize_omit_xml_declaration_false_writes_a_declaration()
        => (await Eval("serialize(<a/>, map { 'method': 'xml', 'omit-xml-declaration': false() })"))
            .Should().StartWith("<?xml");

    // Guard: fn:serialize's parameter map defaults omit-xml-declaration to true.
    [Fact]
    public async Task Fn_serialize_without_omit_parameter_leaves_element_bare()
        => (await Eval("serialize(<a/>, map { 'method': 'xml' })")).Should().Be("<a/>");
}
