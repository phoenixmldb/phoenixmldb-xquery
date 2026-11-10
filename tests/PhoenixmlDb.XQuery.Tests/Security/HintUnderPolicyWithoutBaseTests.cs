using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.Security;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Security;

/// <summary>
/// A relative schema location hint in a query with no base URI is refused under a resource
/// policy too. The policy made it absolute against the process working directory, so with a
/// file root that contains the working directory, <c>import schema ... at 'x.xsd'</c> loaded
/// <c>&lt;cwd&gt;/x.xsd</c>. Only the route without a policy refused it.
/// </summary>
[Collection(WorkingDirectoryTests.Name)]
public sealed class HintUnderPolicyWithoutBaseTests : IDisposable
{
    private readonly string _name = "phx-hint-" + Guid.NewGuid().ToString("N") + ".xsd";
    private readonly string _path;

    public HintUnderPolicyWithoutBaseTests()
    {
        _path = Path.Combine(Environment.CurrentDirectory, _name);
        File.WriteAllText(_path, """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" targetNamespace="urn:h">
              <xs:simpleType name="t"><xs:restriction base="xs:string"/></xs:simpleType>
            </xs:schema>
            """);
    }

    public void Dispose() => File.Delete(_path);

    private sealed class Nothing : ResourceResolverBase;

    private static ResourcePolicy CwdRoot(bool resolver)
    {
        var builder = ResourcePolicy.CreateBuilder()
            .AllowReadFrom("file", pathPrefix: Environment.CurrentDirectory)
            .AllowImportFrom("file", pathPrefix: Environment.CurrentDirectory);
        return (resolver ? builder.WithResourceResolver(new Nothing()) : builder).Build();
    }

    private QueryCompilationResult Compile(ResourcePolicy policy, string? baseUri)
    {
        var engine = new QueryEngine(schemaProvider: new XsdSchemaProvider()) { ResourcePolicy = policy };
        return engine.Compile($"import schema namespace h = 'urn:h' at '{_name}'; h:t('x')",
            new CompilationOptions { BaseUri = baseUri });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_relative_hint_with_no_base_is_refused_under_a_policy_whose_root_holds_the_working_directory(bool resolver)
    {
        var result = Compile(CwdRoot(resolver), baseUri: null);

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "XQST0059");
        result.Errors.Should().NotContain(e => e.Message.Contains(Environment.CurrentDirectory, StringComparison.Ordinal));
    }

    [Fact]
    public void A_relative_hint_resolves_against_the_base_URI_under_a_policy()
    {
        var baseUri = new Uri(Path.Combine(Environment.CurrentDirectory, "q.xq")).AbsoluteUri;

        var result = Compile(CwdRoot(resolver: false), baseUri);

        result.Success.Should().BeTrue(string.Join("; ", result.Errors.Select(e => e.Message)));
    }
}
