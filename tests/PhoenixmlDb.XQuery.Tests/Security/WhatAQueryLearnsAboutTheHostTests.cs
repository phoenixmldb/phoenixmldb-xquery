using FluentAssertions;
using PhoenixmlDb.XQuery.Security;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Security;

/// <summary>
/// Under a resource policy a query learns nothing about the host that the policy does not
/// give it: not the directory the process runs in, and not the address a server redirected to
/// when the policy refused the redirect. And a parameter document comes from a resolver that
/// is the only source of resources.
/// </summary>
public sealed class WhatAQueryLearnsAboutTheHostTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("phx-host-facts").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static async Task<string> Run(ResourcePolicy? policy, string query, Uri? baseUri = null)
    {
        try
        {
            return (await new XQueryFacade { ResourcePolicy = policy }.EvaluateAsync(query, inputXml: null, baseUri: null, queryBaseUri: baseUri)).Trim();
        }
#pragma warning disable CA1031 // any failure is the outcome under test
        catch (Exception e)
#pragma warning restore CA1031
        {
            return "error: " + e.Message;
        }
    }

    // ── the working directory ──

    [Fact]
    public async Task A_relative_base_uri_declaration_does_not_reveal_the_working_directory()
    {
        var result = await Run(ResourcePolicy.CreateBuilder().Build(), "declare base-uri 'rel/'; string(static-base-uri())");

        result.Should().NotContain(Environment.CurrentDirectory).And.NotContain(Path.GetFileName(Environment.CurrentDirectory.TrimEnd('/', '\\')));
        result.Should().Be("rel/");
    }

    [Fact]
    public async Task With_a_static_base_uri_the_declaration_resolves_against_it_as_before()
        => (await Run(ResourcePolicy.CreateBuilder().Build(), "declare base-uri 'rel/'; string(static-base-uri())", new Uri("urn-x://host/app/main.xq")))
            .Should().StartWith("urn-x://host/").And.EndWith("/rel/").And.NotContain(Environment.CurrentDirectory);

    [Fact]
    public async Task With_no_policy_the_declaration_resolves_against_the_working_directory_as_before()
        => (await Run(null, "declare base-uri 'rel/'; string(static-base-uri())")).Should().StartWith("file:///").And.EndWith("/rel/");

    // ── a refused redirect ──

    [Theory]
    [InlineData("try {{ doc('{0}') }} catch * {{ $err:description }}")]
    [InlineData("try {{ unparsed-text('{0}') }} catch * {{ $err:description }}")]
    [InlineData("try {{ collection('{0}') }} catch * {{ $err:description }}")]
    public async Task A_refused_redirect_does_not_name_its_target(string query)
    {
        using var target = new LoopbackHttpServer().Serve("/internal-name-7f3a.xml", "<r/>");
        using var origin = new LoopbackHttpServer().Redirect("/doc.xml", target.Url("/internal-name-7f3a.xml"));
        var policy = ResourcePolicy.CreateBuilder().AllowReadFrom("http", "127.0.0.1", null, origin.Port).Build();

        var result = await Run(policy, string.Format(System.Globalization.CultureInfo.InvariantCulture, query, origin.Url("/doc.xml")));

        result.Should().NotContain("internal-name-7f3a").And.NotContain(":" + target.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        target.Requests.Should().Be(0);
    }

    // ── the parameter document ──

    private sealed class OnlySource(string? parameters) : ResourceResolverBase
    {
        public List<string> Asked { get; } = [];
        public override bool SuppliesAllContent => true;

        public override ResourceContent? ResolveContent(ResourceRequest request)
        {
            Asked.Add(request.Location);
            return parameters is not null && request.Location.EndsWith("params.xml", StringComparison.Ordinal)
                ? new ResourceContent(parameters, new Uri(request.Location))
                : null;
        }
    }

    private string ParameterFile(string method)
    {
        var path = Path.Combine(_dir, "params.xml");
        File.WriteAllText(path, Parameters(method));
        return new Uri(path).AbsoluteUri;
    }

    private static string Parameters(string method) => $"""
        <output:serialization-parameters xmlns:output="http://www.w3.org/2010/xslt-xquery-serialization">
          <output:method value="{method}"/>
        </output:serialization-parameters>
        """;

    [Fact]
    public async Task The_parameter_document_comes_from_a_resolver_that_is_the_only_source()
    {
        // The file on disk says xml; the resolver's copy says text.
        var location = ParameterFile("xml");
        var resolver = new OnlySource(Parameters("text"));
        var policy = ResourcePolicy.CreateBuilder().AllowReadFrom("file", pathPrefix: _dir).WithResourceResolver(resolver).Build();

        var result = await Run(policy, $"declare option output:parameter-document '{location}'; <a>plain</a>");

        result.Should().Be("plain");
        resolver.Asked.Should().Contain(a => a.EndsWith("params.xml", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_parameter_document_the_only_source_does_not_supply_is_not_read_from_disk()
    {
        var location = ParameterFile("text");
        var policy = ResourcePolicy.CreateBuilder().AllowReadFrom("file", pathPrefix: _dir).WithResourceResolver(new OnlySource(null)).Build();

        var result = await Run(policy, $"declare option output:parameter-document '{location}'; <a>plain</a>");

        result.Should().StartWith("error:").And.NotBe("plain");
    }
}
