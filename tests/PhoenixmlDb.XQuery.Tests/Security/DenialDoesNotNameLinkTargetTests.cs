using FluentAssertions;
using PhoenixmlDb.XQuery.Security;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Security;

/// <summary>
/// A file is judged by its canonical path, with every symbolic link resolved. The refusal says
/// what was asked for and not where it leads: the message named the canonical path, so a query
/// could read the target of any link on the machine out of the error it caught.
/// </summary>
public sealed class DenialDoesNotNameLinkTargetTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("phx-link").FullName;
    private readonly string _link;

    public DenialDoesNotNameLinkTargetTests()
    {
        var target = Directory.CreateDirectory(Path.Combine(_dir, "where-the-link-leads")).FullName;
        _link = Path.Combine(_dir, "link");
        Directory.CreateSymbolicLink(_link, target);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData("doc('{0}/x.xml')")]
    [InlineData("unparsed-text('{0}/x.txt')")]
    [InlineData("json-doc('{0}/x.json')")]
    [InlineData("collection('{0}')")]
    [InlineData("uri-collection('{0}')")]
    public async Task A_query_cannot_read_a_link_target_from_the_refusal(string call)
    {
        var query = "try { " + string.Format(System.Globalization.CultureInfo.InvariantCulture, call, new Uri(_link).AbsoluteUri)
                             + " } catch * { $err:description }";
        var facade = new XQueryFacade { ResourcePolicy = ResourcePolicy.CreateBuilder().Build() };

        var result = await facade.EvaluateAsync(query);

        result.Should().Contain("denied").And.NotContain("where-the-link-leads");
    }

    [Fact]
    public void The_exception_does_not_name_the_link_target()
    {
        var act = () => ResourcePolicy.CreateBuilder().Build().Authorize(_link + "/x.xml", ResourceAccessKind.ReadDocument);

        act.Should().Throw<ResourceAccessDeniedException>().Where(e => !e.Message.Contains("where-the-link-leads", StringComparison.Ordinal));
    }
}
