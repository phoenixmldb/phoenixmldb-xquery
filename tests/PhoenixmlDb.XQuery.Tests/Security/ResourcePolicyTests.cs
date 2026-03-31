using FluentAssertions;
using PhoenixmlDb.XQuery.Security;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Security;

public class ResourcePolicyTests
{
    [Fact]
    public void ServerDefault_blocks_file_scheme()
    {
        var policy = ResourcePolicy.ServerDefault;
        var uri = new Uri("file:///etc/passwd");

        policy.IsAllowed(uri, ResourceAccessKind.ReadDocument).Should().BeFalse();
    }

    [Fact]
    public void ServerDefault_blocks_all_schemes()
    {
        var policy = ResourcePolicy.ServerDefault;

        policy.IsAllowed(new Uri("file:///tmp/data.xml"), ResourceAccessKind.ReadDocument).Should().BeFalse();
        policy.IsAllowed(new Uri("https://example.com/data.xml"), ResourceAccessKind.ReadDocument).Should().BeFalse();
        policy.IsAllowed(new Uri("http://example.com/data.xml"), ResourceAccessKind.ReadDocument).Should().BeFalse();
    }

    [Fact]
    public void Unrestricted_allows_all_schemes()
    {
        var policy = ResourcePolicy.Unrestricted;

        policy.IsAllowed(new Uri("file:///tmp/data.xml"), ResourceAccessKind.ReadDocument).Should().BeTrue();
        policy.IsAllowed(new Uri("https://example.com/data.xml"), ResourceAccessKind.ReadDocument).Should().BeTrue();
    }

    [Fact]
    public void AllowReadFrom_https_allows_https_blocks_file()
    {
        var policy = ResourcePolicy.CreateBuilder()
            .AllowReadFrom("https")
            .Build();

        policy.IsAllowed(new Uri("https://example.com/data.xml"), ResourceAccessKind.ReadDocument).Should().BeTrue();
        policy.IsAllowed(new Uri("file:///etc/passwd"), ResourceAccessKind.ReadDocument).Should().BeFalse();
    }

    [Fact]
    public void AllowReadFrom_with_host_scopes_to_host()
    {
        var policy = ResourcePolicy.CreateBuilder()
            .AllowReadFrom("https", host: "api.example.com")
            .Build();

        policy.IsAllowed(new Uri("https://api.example.com/data.xml"), ResourceAccessKind.ReadDocument).Should().BeTrue();
        policy.IsAllowed(new Uri("https://evil.com/steal.xml"), ResourceAccessKind.ReadDocument).Should().BeFalse();
    }

    [Fact]
    public void AllowReadFrom_with_wildcard_host()
    {
        var policy = ResourcePolicy.CreateBuilder()
            .AllowReadFrom("https", host: "*.example.com")
            .Build();

        policy.IsAllowed(new Uri("https://api.example.com/data.xml"), ResourceAccessKind.ReadDocument).Should().BeTrue();
        policy.IsAllowed(new Uri("https://cdn.example.com/schema.xsd"), ResourceAccessKind.ReadDocument).Should().BeTrue();
        policy.IsAllowed(new Uri("https://evil.com/data.xml"), ResourceAccessKind.ReadDocument).Should().BeFalse();
    }

    [Fact]
    public void AllowReadFrom_with_path_prefix()
    {
        var policy = ResourcePolicy.CreateBuilder()
            .AllowReadFrom("https", host: "example.com", pathPrefix: "/data/")
            .Build();

        policy.IsAllowed(new Uri("https://example.com/data/file.xml"), ResourceAccessKind.ReadDocument).Should().BeTrue();
        policy.IsAllowed(new Uri("https://example.com/secrets/file.xml"), ResourceAccessKind.ReadDocument).Should().BeFalse();
    }

    [Fact]
    public void Separate_read_and_write_policies()
    {
        var policy = ResourcePolicy.CreateBuilder()
            .AllowReadFrom("https")
            .AllowWriteTo("s3")
            .Build();

        policy.IsAllowed(new Uri("https://example.com/data.xml"), ResourceAccessKind.ReadDocument).Should().BeTrue();
        policy.IsAllowed(new Uri("https://example.com/output.xml"), ResourceAccessKind.WriteDocument).Should().BeFalse();
        policy.IsAllowed(new Uri("s3://bucket/output.xml"), ResourceAccessKind.WriteDocument).Should().BeTrue();
    }

    [Fact]
    public void InMemoryOnly_blocks_everything()
    {
        var policy = ResourcePolicy.InMemoryOnly;

        policy.IsAllowed(new Uri("file:///tmp/data.xml"), ResourceAccessKind.ReadDocument).Should().BeFalse();
        policy.IsAllowed(new Uri("https://example.com/data.xml"), ResourceAccessKind.ReadDocument).Should().BeFalse();
    }
}

public class PolicyEnforcingResolverTests
{
    [Fact]
    public void Blocks_file_access_with_ServerDefault()
    {
        var resolver = new PolicyEnforcingResolver(inner: null, ResourcePolicy.ServerDefault);

        var act = () => resolver.ResolveDocument("file:///etc/passwd");
        act.Should().Throw<ResourceAccessDeniedException>()
            .Which.RequestedAccess.Should().Be(ResourceAccessKind.ReadDocument);
    }

    [Fact]
    public void Allows_access_with_Unrestricted()
    {
        var resolver = new PolicyEnforcingResolver(inner: null, ResourcePolicy.Unrestricted);

        // Should not throw — inner is null so result is null, but no policy violation
        var result = resolver.ResolveDocument("file:///tmp/data.xml");
        result.Should().BeNull();
    }

    [Fact]
    public void Enforces_document_load_budget()
    {
        var policy = ResourcePolicy.CreateBuilder()
            .AllowScheme("*")
            .WithMaxDocumentLoads(3)
            .Build();
        var resolver = new PolicyEnforcingResolver(inner: null, policy);

        resolver.ResolveDocument("file:///1.xml");
        resolver.ResolveDocument("file:///2.xml");
        resolver.ResolveDocument("file:///3.xml");

        var act = () => resolver.ResolveDocument("file:///4.xml");
        act.Should().Throw<ResourceAccessDeniedException>()
            .Which.Message.Should().Contain("budget exceeded");
    }

    [Fact]
    public void Custom_resolver_takes_precedence()
    {
        var customResolver = new TestResourceResolver();
        var policy = ResourcePolicy.CreateBuilder()
            .AllowScheme("*")
            .WithResourceResolver(customResolver)
            .Build();
        var resolver = new PolicyEnforcingResolver(inner: null, policy);

        var result = resolver.ResolveDocument("custom://test/doc.xml");
        result.Should().NotBeNull();
    }

    [Fact]
    public void IsDocumentAvailable_does_not_throw_when_denied()
    {
        var resolver = new PolicyEnforcingResolver(inner: null, ResourcePolicy.ServerDefault);

        // Should return false, not throw
        resolver.IsDocumentAvailable("file:///etc/passwd").Should().BeFalse();
    }

    private sealed class TestResourceResolver : ResourceResolverBase
    {
        public override Xdm.Nodes.XdmDocument? ResolveDocument(string uri, ResourceAccessKind access)
        {
            return new Xdm.Nodes.XdmDocument
            {
                Id = new Core.NodeId(1),
                Document = new Core.DocumentId(1),
                Children = Xdm.Nodes.XdmDocument.EmptyChildren
            };
        }

        public override bool IsDocumentAvailable(string uri) => true;
    }
}

public class XQueryFacadeResourcePolicyTests
{
    [Fact]
    public async Task ServerDefault_blocks_doc_function()
    {
        var facade = new XQueryFacade { ResourcePolicy = ResourcePolicy.ServerDefault };

        var act = async () => await facade.EvaluateAsync("doc('file:///etc/passwd')");
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task No_policy_allows_evaluation_without_doc()
    {
        var facade = new XQueryFacade();

        var result = await facade.EvaluateAsync("1 + 1");
        result.Should().Be("2");
    }
}
