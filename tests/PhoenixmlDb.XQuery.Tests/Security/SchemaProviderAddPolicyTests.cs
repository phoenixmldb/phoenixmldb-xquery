using FluentAssertions;
using PhoenixmlDb.XQuery.Security;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Security;

/// <summary>
/// A host adds a schema to the provider from text it has vetted. The schema's own xs:include,
/// xs:import and xs:redefine references were then resolved with no restriction at all — any file,
/// any URL — because only <c>import schema</c> in a query went through the resource policy. The
/// policy-taking overloads put the host's own loads under the same rule.
/// </summary>
public sealed class SchemaProviderAddPolicyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "phx-spa-" + Guid.NewGuid().ToString("N"));
    private readonly string _allowed;
    private readonly string _outside;

    public SchemaProviderAddPolicyTests()
    {
        _allowed = Path.Combine(_root, "allowed");
        _outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(_allowed);
        Directory.CreateDirectory(_outside);
        File.WriteAllText(Path.Combine(_outside, "canary.xsd"), """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" targetNamespace="urn:s" elementFormDefault="qualified">
              <xs:element name="canary" type="xs:string"/>
            </xs:schema>
            """);
        File.WriteAllText(Path.Combine(_allowed, "inside.xsd"), """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" targetNamespace="urn:s" elementFormDefault="qualified">
              <xs:element name="inside" type="xs:string"/>
            </xs:schema>
            """);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string Including(string directory, string file, string reference = "include") =>
        $"""
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" targetNamespace="urn:s" elementFormDefault="qualified">
          <xs:{reference} schemaLocation="{new Uri(Path.Combine(directory, file)).AbsoluteUri}"/>
        </xs:schema>
        """;

    private ResourcePolicy AllowedOnly => ResourcePolicy.CreateBuilder().AllowImportFrom("file", pathPrefix: _allowed).Build();

    [Theory]
    [InlineData("include")]
    [InlineData("redefine")]
    public void A_reference_outside_the_policy_is_not_loaded(string reference)
    {
        var provider = new XsdSchemaProvider();
        var act = () => provider.AddFromString("urn:s", Including(_outside, "canary.xsd", reference), AllowedOnly);
        act.Should().Throw<SchemaException>();
        provider.HasElementDeclaration("urn:s", "canary").Should().BeFalse();
    }

    [Fact]
    public void A_reference_inside_the_policy_is_loaded()
    {
        var provider = new XsdSchemaProvider();
        provider.AddFromString("urn:s", Including(_allowed, "inside.xsd"), AllowedOnly);
        provider.HasElementDeclaration("urn:s", "inside").Should().BeTrue();
    }

    [Fact]
    public void InMemoryOnly_resolves_no_reference_at_all()
    {
        var provider = new XsdSchemaProvider();
        var act = () => provider.AddFromString("urn:s", Including(_allowed, "inside.xsd"), ResourcePolicy.InMemoryOnly);
        act.Should().Throw<SchemaException>();
        provider.HasElementDeclaration("urn:s", "inside").Should().BeFalse();
    }

    [Fact]
    public void Without_a_policy_the_reference_is_followed_as_before()
    {
        // The unrestricted overload is unchanged; this is the behaviour the new one guards.
        var provider = new XsdSchemaProvider();
        provider.AddFromString("urn:s", Including(_outside, "canary.xsd"));
        provider.HasElementDeclaration("urn:s", "canary").Should().BeTrue();
    }
}
