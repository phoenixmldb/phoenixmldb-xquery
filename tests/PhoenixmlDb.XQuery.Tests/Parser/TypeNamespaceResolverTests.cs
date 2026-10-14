using AwesomeAssertions;
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

    /// <summary>
    /// In an expression of a host language (<see cref="XQueryParserFacade.AllowNamespaceAxis"/>),
    /// a type name with no prefix that is no built-in type is a type in the default namespace
    /// for elements and types, which the host gives under the empty prefix. It was XPST0051,
    /// so a type of an imported schema with no target namespace could not be named (xslt#359).
    /// </summary>
    private static readonly XQueryParserFacade HostParser = new() { AllowNamespaceAxis = true };

    [Theory]
    [InlineData("'7' cast as small")]
    [InlineData("'7' castable as small")]
    [InlineData("7 instance of small")]
    [InlineData("7 treat as small")]
    public void A_type_name_with_no_prefix_is_in_the_default_namespace_of_the_host(string expression)
    {
        var asked = new List<string>();
        FluentActions.Invoking(() => HostParser.Parse(expression, prefix => { asked.Add(prefix); return prefix.Length == 0 ? "urn:d" : null; }))
            .Should().NotThrow();
        asked.Should().Contain("");
        // and with no default namespace, the type is in no namespace
        FluentActions.Invoking(() => HostParser.Parse(expression, _ => null)).Should().NotThrow();
    }

    [Fact]
    public void Outside_a_host_language_it_is_still_an_error()
        => FluentActions.Invoking(() => Parser.Parse("'7' cast as small", Host))
            .Should().Throw<XQueryParseException>().WithMessage("XPST0051*");

    [Fact]
    public void A_built_in_type_name_with_no_prefix_is_not_sent_to_the_schema()
    {
        // The host decides what `integer` with no prefix means (xpath-default-namespace).
        var cast = (PhoenixmlDb.XQuery.Ast.CastExpression)HostParser.Parse("'7' cast as integer", _ => null);
        cast.TargetType.ItemType.Should().Be(PhoenixmlDb.XQuery.Ast.ItemType.Integer);
    }

    /// <summary>
    /// The same in a kind test: <c>element(*, moneyType)</c> with a type of a schema that has no
    /// target namespace was XPST0008 "unknown schema type" in a stylesheet. 125 cases of the W3C
    /// XSLT suite that need a schema-aware processor stopped there.
    /// </summary>
    [Theory]
    [InlineData(". instance of element(*, moneyType)")]
    [InlineData(". instance of element(n, moneyType)")]
    [InlineData(". instance of attribute(*, moneyType)")]
    [InlineData("self::element(*, moneyType)")]
    [InlineData("child::attribute(*, moneyType)")]
    public void A_type_name_with_no_prefix_in_a_kind_test_is_in_the_default_namespace_of_the_host(string expression)
    {
        FluentActions.Invoking(() => HostParser.Parse(expression, _ => null)).Should().NotThrow();
        FluentActions.Invoking(() => HostParser.Parse(expression, prefix => prefix.Length == 0 ? "urn:d" : null)).Should().NotThrow();
    }

    [Fact]
    public void In_a_kind_test_too_it_is_an_error_outside_a_host_language()
        => FluentActions.Invoking(() => Parser.Parse(". instance of element(*, moneyType)", Host))
            .Should().Throw<XQueryParseException>().WithMessage("XPST0008*");

    [Fact]
    public void A_built_in_type_name_with_no_prefix_in_a_kind_test_is_the_built_in_type()
    {
        var type = ((PhoenixmlDb.XQuery.Ast.InstanceOfExpression)HostParser.Parse(". instance of element(*, integer)", _ => null)).TargetType;
        type.TypeAnnotation!.Value.Namespace.Should().Be(PhoenixmlDb.Core.NamespaceId.Xsd);
    }
}
