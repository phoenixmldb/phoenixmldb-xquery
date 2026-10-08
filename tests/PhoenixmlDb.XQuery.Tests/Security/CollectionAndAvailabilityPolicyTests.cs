using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.Security;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Security;

/// <summary>
/// fn:collection and fn:doc-available read a document over HTTP through the same policy as
/// fn:doc: every redirect is authorised again, and a resource resolver that is the only source
/// of resources is never passed by.
/// </summary>
public sealed class CollectionAndAvailabilityPolicyTests
{
    private const string Marker = "SECRET-MARKER-c41d";

    private static async Task<string> Run(ResourcePolicy? policy, string query)
    {
        var facade = new XQueryFacade { ResourcePolicy = policy };
        try { return await facade.EvaluateAsync(query) ?? ""; }
        catch (XQueryRuntimeException e) { return "ERR " + e.ErrorCode; }
        catch (ResourceAccessDeniedException) { return "ERR denied"; }
    }

    public static TheoryData<string> Reads() => new()
    {
        "collection('{0}')/r/string()",
        "uri-collection('{0}')",
        "doc-available('{0}')",
        // What an availability check fetched must not then be served by fn:doc from a cache.
        "(doc-available('{0}'), doc('{0}')/r/string())",
        "(count(collection('{0}')), doc('{0}')/r/string())",
    };

    [Theory]
    [MemberData(nameof(Reads))]
    public async Task A_redirect_to_an_origin_the_policy_does_not_allow_is_not_followed(string query)
    {
        using var target = new LoopbackHttpServer().Serve("/doc.xml", $"<r>{Marker}</r>");
        using var origin = new LoopbackHttpServer().Redirect("/doc.xml", target.Url("/doc.xml"));
        var policy = ResourcePolicy.CreateBuilder()
            .AllowReadFrom("http", "127.0.0.1", null, origin.Port).Build();

        var result = await Run(policy, string.Format(System.Globalization.CultureInfo.InvariantCulture, query, origin.Url("/doc.xml")));

        result.Should().NotContain(Marker);
        target.Requests.Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(Reads))]
    public async Task A_redirect_within_what_the_policy_allows_is_followed(string query)
    {
        using var target = new LoopbackHttpServer().Serve("/doc.xml", $"<r>{Marker}</r>");
        using var origin = new LoopbackHttpServer().Redirect("/doc.xml", target.Url("/doc.xml"));
        var policy = ResourcePolicy.CreateBuilder()
            .AllowReadFrom("http", "127.0.0.1", null, origin.Port)
            .AllowReadFrom("http", "127.0.0.1", null, target.Port).Build();

        var result = await Run(policy, string.Format(System.Globalization.CultureInfo.InvariantCulture, query, origin.Url("/doc.xml")));

        result.Should().NotStartWith("ERR");
        target.Requests.Should().BeGreaterThan(0);
    }

    private sealed class OnlySource : ResourceResolverBase
    {
        public override bool SuppliesAllContent => true;
    }

    [Theory]
    [MemberData(nameof(Reads))]
    public async Task A_resolver_that_supplies_all_content_is_not_passed_by(string query)
    {
        using var server = new LoopbackHttpServer().Serve("/doc.xml", $"<r>{Marker}</r>");
        // The policy would allow the engine to fetch from that origin, and it must not.
        var policy = ResourcePolicy.CreateBuilder()
            .AllowReadFrom("http", "127.0.0.1", null, server.Port)
            .WithResourceResolver(new OnlySource()).Build();

        var result = await Run(policy, string.Format(System.Globalization.CultureInfo.InvariantCulture, query, server.Url("/doc.xml")));

        result.Should().NotContain(Marker);
        server.Requests.Should().Be(0);
    }
}
