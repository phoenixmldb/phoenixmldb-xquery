using FluentAssertions;
using PhoenixmlDb.Core.Xml;
using PhoenixmlDb.XQuery.Security;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Security;

/// <summary>
/// A host can switch XInclude on for the documents a store loads. Under a resource policy,
/// what a document includes is read under the policy as the document was: an include of a
/// file the policy refuses is not read. It used to be opened by a resolver that knows no
/// policy, so a query could read any file through an allowed document that includes it.
/// </summary>
public sealed class XIncludeUnderPolicyTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("phx-xinclude-policy").FullName;
    private readonly string _allowed;
    private readonly string _outside;

    public XIncludeUnderPolicyTests()
    {
        _allowed = Directory.CreateDirectory(Path.Combine(_dir, "allowed")).FullName;
        _outside = Directory.CreateDirectory(Path.Combine(_dir, "outside")).FullName;
        File.WriteAllText(Path.Combine(_outside, "secret.xml"), "<s>outside-content</s>");
        File.WriteAllText(Path.Combine(_outside, "secret.txt"), "outside-text");
        File.WriteAllText(Path.Combine(_allowed, "part.xml"), "<p>inside-content</p>");
        Write("ok.xml", "<xi:include href='part.xml'/>");
        Write("leak-xml.xml", $"<xi:include href='{new Uri(Path.Combine(_outside, "secret.xml")).AbsoluteUri}'/>");
        Write("leak-text.xml", $"<xi:include parse='text' href='{new Uri(Path.Combine(_outside, "secret.txt")).AbsoluteUri}'/>");
        Write("leak-relative.xml", "<xi:include href='../outside/secret.xml'/>");
    }

    private void Write(string name, string include)
        => File.WriteAllText(Path.Combine(_allowed, name), $"<r xmlns:xi='http://www.w3.org/2001/XInclude'>{include}</r>");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Load(string file, ResourcePolicy? policy, IResourceResolver? custom = null)
    {
        var store = new XdmDocumentStore { XInclude = new XIncludeOptions { Enabled = true } };
        IDocumentResolver resolver = policy is null ? store : new PolicyEnforcingResolver(store, policy);
        try
        {
            var document = resolver.ResolveDocument(new Uri(Path.Combine(_allowed, file)).AbsoluteUri);
            return document is null ? "(none)" : document.StringValue;
        }
#pragma warning disable CA1031 // any failure is the outcome under test
        catch (Exception e)
#pragma warning restore CA1031
        {
            return "error: " + e.Message;
        }
    }

    private ResourcePolicy AllowedOnly => ResourcePolicy.CreateBuilder().AllowReadFrom("file", pathPrefix: _allowed).Build();

    [Theory]
    [InlineData("leak-xml.xml")]
    [InlineData("leak-text.xml")]
    [InlineData("leak-relative.xml")]
    public void An_include_of_a_file_the_policy_refuses_is_not_read(string file)
        => Load(file, AllowedOnly).Should().NotContain("outside-content").And.NotContain("outside-text");

    [Fact]
    public void An_include_of_a_file_the_policy_allows_is_read()
        => Load("ok.xml", AllowedOnly).Should().Be("inside-content");

    [Theory]
    [InlineData("leak-xml.xml", "outside-content")]
    [InlineData("leak-text.xml", "outside-text")]
    public void With_no_policy_the_include_is_read_as_before(string file, string expected)
        => Load(file, null).Should().Be(expected);

    private sealed class OnlySource : ResourceResolverBase
    {
        public override bool SuppliesAllContent => true;

        public override ResourceContent? ResolveContent(ResourceRequest request)
            => request.Location.EndsWith("main.xml", StringComparison.Ordinal)
                ? new ResourceContent("<r xmlns:xi='http://www.w3.org/2001/XInclude'><xi:include href='part.xml'/><xi:include href='gone.xml'><xi:fallback>no</xi:fallback></xi:include></r>", new Uri(request.Location))
                : request.Location.EndsWith("part.xml", StringComparison.Ordinal)
                    ? new ResourceContent("<p>from-the-host</p>", new Uri(request.Location))
                    : null;
    }

    [Fact]
    public void Under_a_resolver_that_is_the_only_source_an_include_comes_from_it()
    {
        // part.xml exists on disk in the allowed directory; the resolver's copy is what is read.
        var policy = ResourcePolicy.CreateBuilder().AllowReadFrom("file", pathPrefix: _allowed).WithResourceResolver(new OnlySource()).Build();

        Load("main.xml", policy).Should().StartWith("from-the-host").And.NotContain("inside-content");
    }
}
