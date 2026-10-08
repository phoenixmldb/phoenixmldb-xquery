using FluentAssertions;
using PhoenixmlDb.XQuery.Parser;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Parser;

/// <summary>
/// <see cref="XQueryParserFacade.Parse(string, Func{string, string?})"/>: an expression embedded
/// in a host language (XPath in XSLT) has no prolog, so the prefix of a type name comes from the
/// host's static context. Without the resolver such a name was XPST0081 although the stylesheet
/// declared the prefix (xslt#316).
/// </summary>
public sealed class TypeNamespaceResolverTests
{
    private static readonly XQueryParserFacade Parser = new();

    private static string? Host(string prefix) => prefix switch
    {
        "t" => "urn:t",
        "x" => "http://www.w3.org/2001/XMLSchema",
        _ => null,
    };

    [Theory]
    [InlineData("'8' cast as t:size")]
    [InlineData("'8' castable as t:size")]
    [InlineData("'8' cast as x:integer")]
    [InlineData("8 instance of x:integer")]
    [InlineData("8 instance of t:size")]
    [InlineData("8 treat as t:size")]
    public void A_type_name_takes_its_prefix_from_the_host(string expression)
        => FluentActions.Invoking(() => Parser.Parse(expression, Host)).Should().NotThrow();

    [Theory]
    [InlineData("'8' cast as t:size")]
    [InlineData("'8' cast as x:integer")]
    public void Without_a_resolver_the_prefix_is_unbound(string expression)
    {
        FluentActions.Invoking(() => Parser.Parse(expression)).Should().Throw<XQueryParseException>().WithMessage("XPST0081*");
        FluentActions.Invoking(() => Parser.Parse(expression, null)).Should().Throw<XQueryParseException>().WithMessage("XPST0081*");
    }

    [Fact]
    public void A_prefix_the_host_does_not_bind_is_unbound()
        => FluentActions.Invoking(() => Parser.Parse("'8' cast as u:size", Host))
            .Should().Throw<XQueryParseException>().WithMessage("XPST0081*");

    /// <summary>What the expression binds itself wins; the host is asked last.</summary>
    [Fact]
    public void The_built_in_prefixes_are_not_asked_of_the_host()
    {
        var asked = new List<string>();
        Parser.Parse("'8' cast as xs:integer", prefix => { asked.Add(prefix); return "urn:other"; });
        asked.Should().BeEmpty();
    }
}
