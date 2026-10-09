using FluentAssertions;
using PhoenixmlDb.XQuery.Security;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// <c>file:/abs/path</c> is a file URI (RFC 8089), the same location as <c>file:///abs/path</c>.
/// .NET takes it for neither an absolute URI nor a relative reference, and fn:doc,
/// fn:doc-available and fn:json-doc threw UriFormatException for it when the query had a base
/// URI: not an XQuery error, so a host could not catch it as one, and its resolver was not asked.
/// </summary>
public sealed class FileUriWithoutAuthorityTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "phx-fileuri-" + Guid.NewGuid().ToString("N"));

    public FileUriWithoutAuthorityTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "d.xml"), "<r>7</r>");
        File.WriteAllText(Path.Combine(_dir, "t.txt"), "7");
        File.WriteAllText(Path.Combine(_dir, "j.json"), """{ "n": "7" }""");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static readonly Uri HttpBase = new("http://host.invalid/app/main.xq");

    /// <summary>file:/abs/path for a file in the test directory.</summary>
    private string OneSlash(string file) => "file:" + new Uri(Path.Combine(_dir, file)).AbsolutePath;

    [Theory]
    [InlineData("string(doc('{0}'))", "d.xml")]
    [InlineData("string(doc-available('{0}'))", "d.xml", "true")]
    [InlineData("unparsed-text('{0}')", "t.txt")]
    [InlineData("string-join(unparsed-text-lines('{0}'))", "t.txt")]
    [InlineData("string(unparsed-text-available('{0}'))", "t.txt", "true")]
    [InlineData("json-doc('{0}')?n", "j.json")]
    public async Task The_file_is_read(string expression, string file, string expected = "7")
    {
        var query = string.Format(System.Globalization.CultureInfo.InvariantCulture, expression, OneSlash(file));

        (await new XQueryFacade().EvaluateAsync(query, inputXml: null, baseUri: null, queryBaseUri: HttpBase)).Trim().Should().Be(expected);
    }

    private sealed class Recorder : ResourceResolverBase
    {
        public List<string> Asked { get; } = [];
        public override bool SuppliesAllContent => true;

        public override ResourceContent? ResolveContent(ResourceRequest request)
        {
            Asked.Add(request.Location);
            return null;
        }
    }

    [Theory]
    [InlineData("doc('file:/abs/path')", "FODC0002")]
    [InlineData("unparsed-text('file:/abs/path')", "FOUT1170")]
    [InlineData("unparsed-text-lines('file:/abs/path')", "FOUT1170")]
    [InlineData("json-doc('file:/abs/path')", "FOUT1170")]
    public async Task Under_a_policy_the_host_is_asked_and_its_refusal_is_an_xquery_error(string expression, string code)
    {
        var host = new Recorder();
        var facade = new XQueryFacade { ResourcePolicy = ResourcePolicy.CreateBuilder().WithResourceResolver(host).Build() };

        var act = () => facade.EvaluateAsync(expression, inputXml: null, baseUri: null, queryBaseUri: HttpBase);

        (await act.Should().ThrowAsync<PhoenixmlDb.XQuery.Execution.XQueryRuntimeException>()).Which.ErrorCode.Should().Be(code);
        host.Asked.Should().ContainSingle().Which.Should().BeOneOf("file:///abs/path", "file:/abs/path");
    }

    [Theory]
    [InlineData("doc-available('file:/abs/path')")]
    [InlineData("unparsed-text-available('file:/abs/path')")]
    [InlineData("doc-available('file:abs')")]
    public async Task Availability_is_false_and_does_not_fail(string expression)
    {
        var facade = new XQueryFacade { ResourcePolicy = ResourcePolicy.CreateBuilder().WithResourceResolver(new Recorder()).Build() };

        (await facade.EvaluateAsync($"string({expression})", inputXml: null, baseUri: null, queryBaseUri: HttpBase)).Trim().Should().Be("false");
    }

    /// <summary>Not a URI at all: an XQuery error, not a .NET exception.</summary>
    [Theory]
    [InlineData("doc('file:abs')")]
    [InlineData("json-doc('file:abs')")]
    public async Task What_is_not_a_uri_is_an_xquery_error(string expression)
    {
        var act = () => new XQueryFacade().EvaluateAsync(expression, inputXml: null, baseUri: null, queryBaseUri: HttpBase);

        await act.Should().ThrowAsync<PhoenixmlDb.XQuery.Execution.XQueryRuntimeException>();
    }
}
