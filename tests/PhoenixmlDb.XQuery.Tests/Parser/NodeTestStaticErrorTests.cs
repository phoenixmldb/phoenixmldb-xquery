using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.Parser;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Parser;

/// <summary>
/// Node tests whose static errors went unreported: the query compiled and then failed at run
/// time with "context item is absent" (QT3 K2-Axes-5..16, K2-NodeTest-19..25).
/// </summary>
public class NodeTestStaticErrorTests
{
    private static string? StaticError(string query)
    {
        try
        {
            var compiled = new QueryEngine().Compile(query);
            return compiled.Success ? null : compiled.Errors[0].Code;
        }
        catch (XQueryParseException e)
        {
            // The parser reports some static errors by throwing, with the code leading the message.
            return e.Message.Split(':')[0];
        }
    }

    [Theory]
    // *:NCName and NCName:* are single tokens: no whitespace or comment inside
    [InlineData("* :ncname", "XPST0003")]
    [InlineData("*(:c:):ncname", "XPST0003")]
    [InlineData("*:(:c:)ncname", "XPST0003")]
    [InlineData("p: *", "XPST0003")]
    // The inner test of document-node() is checked like a bare one
    [InlineData("document-node(element(notBound:ncname))", "XPST0081")]
    [InlineData("document-node(schema-element(notBound:ncname))", "XPST0081")]
    [InlineData("document-node(schema-element(undeclared))", "XPST0008")]
    public void The_static_error_is_reported(string query, string code) =>
        StaticError(query).Should().Be(code);

    [Theory]
    [InlineData("<a><b/></a>/*:b")]
    [InlineData("declare namespace p = 'urn:p'; <a/>/p:*")]
    [InlineData("<a/>/*")]
    [InlineData("document { <a/> }/self::document-node(element(a))")]
    [InlineData("document { <a/> } instance of document-node()")]
    public void Valid_node_tests_still_compile(string query) =>
        StaticError(query).Should().BeNull();
}
