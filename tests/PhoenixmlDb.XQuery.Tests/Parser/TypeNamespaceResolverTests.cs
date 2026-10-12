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

    /// <summary>
    /// A host such as XSLT predeclares no prefixes, so its binding for one XQuery predeclares
    /// (<c>local</c>) is the binding. The parse succeeding does not show which was used: the
    /// resolver being asked, and XPST0051 without it for a non-cast position, does.
    /// </summary>
    [Fact]
    public void The_host_binding_takes_precedence_over_a_predeclared_prefix()
    {
        var asked = new List<string>();
        Parser.Parse("'8' cast as local:size", prefix => { asked.Add(prefix); return "urn:t"; });
        asked.Should().Equal("local");
    }

    /// <summary><c>xs</c> is the schema namespace everywhere; the host is not asked for it.</summary>
    [Fact]
    public void The_xs_prefix_is_not_asked_of_the_host()
    {
        var asked = new List<string>();
        Parser.Parse("'8' cast as xs:integer", prefix => { asked.Add(prefix); return "urn:other"; });
        asked.Should().BeEmpty();
    }

    /// <summary>
    /// The name of a declaration in schema-element() and schema-attribute(), and the type name
    /// in element(*, T) and attribute(*, T), take their prefix from the host as well. The
    /// declaration was looked for in no namespace, so in a stylesheet
    /// <c>$v instance of schema-element(o:order)</c> was false for every element, and
    /// <c>element(*, o:orderType)</c> was XPST0081 (xslt#353).
    /// </summary>
    [Theory]
    [InlineData(". instance of schema-element(t:n)", "urn:t", null)]
    [InlineData(". instance of schema-attribute(t:a)", null, "urn:t")]
    public void A_schema_declaration_name_takes_its_prefix_from_the_host(string expression, string? elementNamespace, string? attributeNamespace)
    {
        var type = ((PhoenixmlDb.XQuery.Ast.InstanceOfExpression)Parser.Parse(expression, Host)).TargetType;
        type.SchemaElementNamespace.Should().Be(elementNamespace);
        type.SchemaAttributeNamespace.Should().Be(attributeNamespace);
    }

    [Theory]
    [InlineData(". instance of element(*, t:size)")]
    [InlineData(". instance of element(n, t:size)")]
    [InlineData(". instance of attribute(*, t:size)")]
    [InlineData("self::element(*, t:size)")]
    public void A_type_name_in_a_kind_test_takes_its_prefix_from_the_host(string expression)
        => FluentActions.Invoking(() => Parser.Parse(expression, Host)).Should().NotThrow();

    [Theory]
    [InlineData(". instance of schema-element(u:n)")]
    [InlineData(". instance of element(*, u:size)")]
    public void A_prefix_the_host_does_not_bind_is_unbound_in_a_kind_test(string expression)
        => FluentActions.Invoking(() => Parser.Parse(expression, Host))
            .Should().Throw<XQueryParseException>().WithMessage("XPST0081*");
}
