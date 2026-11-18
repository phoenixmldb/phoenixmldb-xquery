using AwesomeAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// serialize() with indent=yes and an XML declaration: the markup that follows the declaration
/// starts on a line of its own, as Saxon-HE 12.10 writes it and as the XSLT engine does since
/// xslt#559. A document node did so; an element with a declaration it was asked for followed the
/// declaration on the same line. Text that follows the declaration (a document whose first child
/// is text) is not given a newline: there it would be content. Expected values are Saxon's.
/// </summary>
public sealed class IndentedXmlDeclarationTests
{
    private readonly XQueryFacade _facade = new();

    private const string Declaration = """<?xml version="1.0" encoding="UTF-8"?>""";

    private Task<string> Serialize(string node, bool indent)
        => _facade.EvaluateAsync($"serialize({node}, map{{'indent': {(indent ? "true" : "false")}(), 'omit-xml-declaration': false()}})");

    [Fact]
    public async Task An_element_starts_on_the_line_after_the_declaration()
        => (await Serialize("<root><e/></root>", indent: true)).Should().Be(Declaration + "\n<root>\n  <e/>\n</root>");

    [Fact]
    public async Task A_document_that_starts_with_a_comment_starts_on_the_next_line()
        => (await Serialize("document{<!--c-->, 't', <root/>}", indent: true)).Should().StartWith(Declaration + "\n<!--c-->");

    [Fact]
    public async Task Text_after_the_declaration_is_not_given_a_newline()
        => (await Serialize("document{'t', <root/>}", indent: true)).Should().Be(Declaration + "t<root/>");

    [Fact]
    public async Task Without_indent_the_declaration_and_the_element_stay_on_one_line()
        => (await Serialize("<root><e/></root>", indent: false)).Should().Be(Declaration + "<root><e/></root>");
}
