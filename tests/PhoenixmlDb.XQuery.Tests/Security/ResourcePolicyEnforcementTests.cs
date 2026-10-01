using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.Security;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Security;

/// <summary>
/// Every function that dereferences a URI or path obeys the resource policy. Each read below
/// used to reach the file system or network under <see cref="ResourcePolicy.ServerDefault"/>
/// while fn:doc was correctly denied; each "leak" check plants a marker and asserts it never
/// comes back.
/// </summary>
public sealed class ResourcePolicyEnforcementTests : IDisposable
{
    private const string Marker = "SECRET-MARKER-7f3a";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "phx-rp-" + Guid.NewGuid().ToString("N"));
    private readonly string _allowed;
    private readonly string _outside;

    public ResourcePolicyEnforcementTests()
    {
        _allowed = Directory.CreateDirectory(Path.Combine(_dir, "allowed")).FullName;
        _outside = Directory.CreateDirectory(Path.Combine(_dir, "allowed-not")).FullName;
        foreach (var d in new[] { _allowed, _outside })
        {
            File.WriteAllText(Path.Combine(d, "secret.txt"), Marker);
            File.WriteAllText(Path.Combine(d, "secret.json"), $$"""{"k":"{{Marker}}"}""");
            File.WriteAllText(Path.Combine(d, "m.xqm"), $"module namespace m = \"urn:m\"; declare function m:f() {{ \"{Marker}\" }};");
            File.WriteAllText(Path.Combine(d, "s.xsd"), """
                <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" targetNamespace="urn:s">
                  <xs:simpleType name="t"><xs:restriction base="xs:string"/></xs:simpleType>
                </xs:schema>
                """);
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private static string FileUri(string path) => new Uri(path).AbsoluteUri;

    private static async Task<string> Run(ResourcePolicy? policy, string query)
    {
        var facade = new XQueryFacade { ResourcePolicy = policy };
        try { return await facade.EvaluateAsync(query) ?? ""; }
        catch (XQueryRuntimeException e) { return "ERR " + e.ErrorCode; }
    }

    private string Secret(string dir, string name) => Path.Combine(dir, name);

    public static TheoryData<string> DeniedReads() => new()
    {
        "unparsed-text('{file}/secret.txt')",
        "unparsed-text('{path}/secret.txt')",
        "string-join(unparsed-text-lines('{file}/secret.txt'))",
        "json-doc('{file}/secret.json')?k",
        "json-doc('{path}/secret.json')?k",
        "import module namespace m = 'urn:m' at '{file}/m.xqm'; m:f()",
        "import module namespace m = 'urn:m' at '{path}/m.xqm'; m:f()",
        "load-xquery-module('urn:m', map { 'location-hints': '{file}/m.xqm' })?functions(QName('urn:m', 'f'))?0()",
        "parse-xml(concat('<!DOCTYPE r [<!ENTITY e SYSTEM \"{file}/secret.txt\">]><r>', '&amp;', 'e;</r>'))/string()",
    };

    [Theory]
    [MemberData(nameof(DeniedReads))]
    public async Task ServerDefault_never_returns_local_content(string template)
    {
        var query = template.Replace("{file}", FileUri(_allowed), StringComparison.Ordinal)
            .Replace("{path}", _allowed, StringComparison.Ordinal);
        (await Run(ResourcePolicy.ServerDefault, query)).Should().NotContain(Marker);
        // Control: the same query does read the file with no policy.
        (await Run(null, query)).Should().Contain(Marker);
    }

    [Fact]
    public async Task ServerDefault_denies_with_the_functions_own_error_codes()
    {
        (await Run(ResourcePolicy.ServerDefault, $"unparsed-text('{Secret(_allowed, "secret.txt")}')")).Should().Be("ERR FOUT1170");
        (await Run(ResourcePolicy.ServerDefault, $"json-doc('{Secret(_allowed, "secret.json")}')")).Should().Be("ERR FOUT1170");
        (await Run(ResourcePolicy.ServerDefault, $"import module namespace m = 'urn:m' at '{Secret(_allowed, "m.xqm")}'; m:f()")).Should().Be("ERR XQST0059");
        (await Run(ResourcePolicy.ServerDefault, $"import schema namespace s = 'urn:s' at '{Secret(_allowed, "s.xsd")}'; s:t('x')")).Should().StartWith("ERR XQST0059");
    }

    [Fact]
    public async Task Availability_checks_do_not_reveal_denied_files()
    {
        (await Run(ResourcePolicy.ServerDefault, $"unparsed-text-available('{Secret(_allowed, "secret.txt")}')")).Should().Be("false");
        (await Run(ResourcePolicy.ServerDefault, $"doc-available('{FileUri(_allowed)}/nope.xml')")).Should().Be("false");
    }

    [Fact]
    public async Task Parse_xml_does_not_fetch_an_external_dtd_under_a_policy()
    {
        var dtd = Path.Combine(_allowed, "d.dtd");
        await File.WriteAllTextAsync(dtd, $"<!ATTLIST r a CDATA \"{Marker}\">");
        var query = $"parse-xml('<!DOCTYPE r SYSTEM \"{FileUri(dtd)}\"><r/>')/r/@a/string()";
        (await Run(ResourcePolicy.ServerDefault, query)).Should().NotContain(Marker);
        (await Run(null, query)).Should().Be(Marker);
    }

    private ResourcePolicy AllowAllowedDir(bool import = false)
    {
        var b = ResourcePolicy.CreateBuilder().AllowReadFrom("file", pathPrefix: _allowed);
        if (import)
            b.AllowImportFrom("file", pathPrefix: _allowed);
        return b.Build();
    }

    [Fact]
    public async Task A_path_prefix_admits_its_directory_and_nothing_beside_it()
    {
        var policy = AllowAllowedDir();
        (await Run(policy, $"unparsed-text('{Secret(_allowed, "secret.txt")}')")).Should().Be(Marker);
        // "allowed-not" starts with the same characters but is a different directory.
        (await Run(policy, $"unparsed-text('{Secret(_outside, "secret.txt")}')")).Should().Be("ERR FOUT1170");
        // ".." cannot climb out.
        (await Run(policy, $"unparsed-text('{_allowed}/../allowed-not/secret.txt')")).Should().Be("ERR FOUT1170");
    }

    [Fact]
    public async Task A_symbolic_link_is_judged_by_where_it_points()
    {
        var link = Path.Combine(_allowed, "link.txt");
        try { File.CreateSymbolicLink(link, Secret(_outside, "secret.txt")); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return; } // no symlink rights (Windows)
        (await Run(AllowAllowedDir(), $"unparsed-text('{link}')")).Should().Be("ERR FOUT1170");
    }

    [Fact]
    public async Task Path_prefixes_are_case_sensitive_where_the_file_system_is()
    {
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
            return;
        var upper = Directory.CreateDirectory(Path.Combine(_dir, "ALLOWED")).FullName;
        await File.WriteAllTextAsync(Path.Combine(upper, "secret.txt"), Marker);
        (await Run(AllowAllowedDir(), $"unparsed-text('{Path.Combine(upper, "secret.txt")}')")).Should().Be("ERR FOUT1170");
    }

    [Fact]
    public async Task Read_access_does_not_grant_import_access()
    {
        var query = $"import module namespace m = 'urn:m' at '{Secret(_allowed, "m.xqm")}'; m:f()";
        (await Run(AllowAllowedDir(), query)).Should().Be("ERR XQST0059");
        (await Run(AllowAllowedDir(import: true), query)).Should().Be(Marker);
    }

    [Fact]
    public async Task QueryEngine_accepts_a_policy_directly()
    {
        var engine = new QueryEngine { ResourcePolicy = ResourcePolicy.ServerDefault };
        var compiled = engine.Compile($"unparsed-text('{Secret(_allowed, "secret.txt")}')");
        compiled.Success.Should().BeTrue();
        var act = async () =>
        {
            await foreach (var _ in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext())) { }
        };
        (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be("FOUT1170");
    }

    [Fact]
    public void A_host_rule_admits_only_the_default_port_unless_one_is_given()
    {
        var hostOnly = ResourcePolicy.CreateBuilder().AllowReadFrom("http", "127.0.0.1").Build();
        hostOnly.IsAllowed(new Uri("http://127.0.0.1/x"), ResourceAccessKind.ReadDocument).Should().BeTrue();
        hostOnly.IsAllowed(new Uri("http://127.0.0.1:8081/x"), ResourceAccessKind.ReadDocument).Should().BeFalse();

        var withPort = ResourcePolicy.CreateBuilder().AllowReadFrom("http", "127.0.0.1", null, 8081).Build();
        withPort.IsAllowed(new Uri("http://127.0.0.1:8081/x"), ResourceAccessKind.ReadDocument).Should().BeTrue();
        withPort.IsAllowed(new Uri("http://127.0.0.1:8082/x"), ResourceAccessKind.ReadDocument).Should().BeFalse();

        var anyPort = ResourcePolicy.CreateBuilder().AllowReadFrom("http", "127.0.0.1", null, UriRule.AnyPort).Build();
        anyPort.IsAllowed(new Uri("http://127.0.0.1:9/x"), ResourceAccessKind.ReadDocument).Should().BeTrue();
    }

    [Fact]
    public void A_rooted_path_is_a_file_not_a_relative_reference()
    {
        ResourcePolicy.Resolve("/etc/hostname")!.IsFile.Should().BeTrue();
        ResourcePolicy.ServerDefault.TryAuthorize("/etc/hostname", ResourceAccessKind.ReadText).Should().BeNull();
        ResourcePolicy.ServerDefault.TryAuthorize("relative.txt", ResourceAccessKind.ReadText).Should().BeNull();
    }

    [Fact]
    public void An_empty_import_rule_list_allows_no_import()
    {
        var policy = ResourcePolicy.CreateBuilder().AllowReadFrom("file", pathPrefix: "/data").Build();
        policy.IsAllowed(new Uri("file:///data/m.xqm"), ResourceAccessKind.ReadText).Should().BeTrue();
        policy.IsAllowed(new Uri("file:///anywhere/m.xqm"), ResourceAccessKind.ImportStylesheet).Should().BeFalse();
        policy.IsAllowed(new Uri("file:///data/m.xqm"), ResourceAccessKind.ImportStylesheet).Should().BeFalse();
    }

    [Fact]
    public void A_scheme_allowed_outright_is_still_narrowed_by_its_rules()
    {
        var policy = ResourcePolicy.CreateBuilder().AllowScheme("https").AllowReadFrom("https", "api.example.com").Build();
        policy.IsAllowed(new Uri("https://api.example.com/x"), ResourceAccessKind.ReadDocument).Should().BeTrue();
        policy.IsAllowed(new Uri("https://evil.example/x"), ResourceAccessKind.ReadDocument).Should().BeFalse();
        // No import rule scopes https, so the outright allowance applies to imports.
        policy.IsAllowed(new Uri("https://evil.example/m.xqm"), ResourceAccessKind.ImportStylesheet).Should().BeTrue();
    }

    [Fact]
    public async Task Redirects_are_re_authorised()
    {
        using var target = new LoopbackHttpServer().Serve("/doc.xml", $"<r>{Marker}</r>");
        using var origin = new LoopbackHttpServer().Redirect("/doc.xml", target.Url("/doc.xml"));
        var policy = ResourcePolicy.CreateBuilder().AllowReadFrom("http", "127.0.0.1", null, origin.Port).Build();

        (await Run(policy, $"doc('{origin.Url("/doc.xml")}')/r/string()")).Should().NotContain(Marker);
        target.Requests.Should().Be(0);
        // Control: without a policy the redirect is followed.
        (await Run(null, $"doc('{origin.Url("/doc.xml")}')/r/string()")).Should().Be(Marker);
    }

    [Fact]
    public async Task A_module_redirect_is_re_authorised_and_the_module_cache_is_not_a_bypass()
    {
        var module = $"module namespace m = \"urn:m\"; declare function m:f() {{ \"{Marker}\" }};";
        using var target = new LoopbackHttpServer().Serve("/m.xqm", module);
        using var origin = new LoopbackHttpServer().Redirect("/m.xqm", target.Url("/m.xqm"));
        var query = $"import module namespace m = 'urn:m' at '{origin.Url("/m.xqm")}'; m:f()";

        // Warm the process-wide cache with no policy (the redirect is followed).
        (await Run(null, query)).Should().Be(Marker);
        var before = target.Requests;

        var policy = ResourcePolicy.CreateBuilder().AllowImportFrom("http", "127.0.0.1", null, origin.Port).Build();
        (await Run(policy, query)).Should().Be("ERR XQST0059");
        target.Requests.Should().Be(before);
        (await Run(ResourcePolicy.ServerDefault, query)).Should().Be("ERR XQST0059");
    }

    [Fact]
    public async Task A_schema_import_redirect_is_re_authorised()
    {
        var xsd = await File.ReadAllTextAsync(Path.Combine(_allowed, "s.xsd"));
        using var target = new LoopbackHttpServer().Serve("/s.xsd", xsd);
        using var origin = new LoopbackHttpServer().Redirect("/s.xsd", target.Url("/s.xsd"));
        var policy = ResourcePolicy.CreateBuilder().AllowImportFrom("http", "127.0.0.1", null, origin.Port).Build();
        var engine = new QueryEngine(schemaProvider: new XsdSchemaProvider()) { ResourcePolicy = policy };
        var compiled = engine.Compile($"import schema namespace s = 'urn:s' at '{origin.Url("/s.xsd")}'; 1");
        compiled.Success.Should().BeFalse();
        target.Requests.Should().Be(0);
    }

    [Fact]
    public async Task A_schema_include_outside_the_policy_is_not_read()
    {
        var outsideXsd = Path.Combine(_outside, "inc.xsd");
        await File.WriteAllTextAsync(outsideXsd, """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" targetNamespace="urn:i">
              <xs:simpleType name="leak"><xs:restriction base="xs:string"/></xs:simpleType>
            </xs:schema>
            """);
        var main = Path.Combine(_allowed, "inc-main.xsd");
        await File.WriteAllTextAsync(main, $"""
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" targetNamespace="urn:i">
              <xs:include schemaLocation="{new Uri(outsideXsd).AbsoluteUri}"/>
            </xs:schema>
            """);
        var policy = ResourcePolicy.CreateBuilder().AllowImportFrom("file", pathPrefix: _allowed).Build();
        var query = $"import schema namespace i = 'urn:i' at '{main}'; i:leak('x')";
        (await Run(policy, query)).Should().StartWith("ERR");
        (await Run(null, query)).Should().Be("x");
    }

    [Fact]
    public async Task Import_schema_over_http_is_not_fetched_under_ServerDefault()
    {
        using var server = new LoopbackHttpServer().Serve("/s.xsd", await File.ReadAllTextAsync(Path.Combine(_allowed, "s.xsd")));
        (await Run(ResourcePolicy.ServerDefault, $"import schema namespace s = 'urn:s' at '{server.Url("/s.xsd")}'; 1"))
            .Should().StartWith("ERR");
        server.Requests.Should().Be(0);
    }
}
