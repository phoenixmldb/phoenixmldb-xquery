using AwesomeAssertions;
using PhoenixmlDb.XQuery.Security;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Security;

/// <summary>
/// Under a resource policy a file is judged by the file actually opened. The policy resolved every
/// link on a path and judged where it led, and the reader then opened the path again by name: a
/// link put in place of the file between the two was followed out of the allowed roots, and the
/// query read a file the policy refuses.
/// </summary>
public sealed class OpenedFileIsJudgedTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("phx-opened").FullName;
    private readonly string _allowed;
    private readonly string _secret;

    public OpenedFileIsJudgedTests()
    {
        _allowed = Directory.CreateDirectory(Path.Combine(_dir, "allowed")).FullName;
        var outside = Directory.CreateDirectory(Path.Combine(_dir, "outside")).FullName;
        _secret = Path.Combine(outside, "secret.txt");
        File.WriteAllText(_secret, "SECRET");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private ResourcePolicy Policy() => ResourcePolicy.CreateBuilder()
        .AllowReadFrom("file", null, _allowed + Path.DirectorySeparatorChar)
        .Build();

    [Fact]
    public void A_link_put_on_the_path_after_the_check_is_refused()
    {
        var file = Path.Combine(_allowed, "a.txt");
        File.WriteAllText(file, "inside");
        var policy = Policy();
        var authorized = policy.Authorize(file, ResourceAccessKind.ReadText);

        File.Delete(file);
        File.CreateSymbolicLink(file, _secret);

        var act = () => AuthorizedFile.OpenRead(policy, authorized, ResourceAccessKind.ReadText, "a.txt");
        act.Should().Throw<ResourceAccessDeniedException>()
            .Where(e => !e.Message.Contains("secret", StringComparison.Ordinal)
                && !e.Message.Contains("outside", StringComparison.Ordinal));
    }

    [Fact]
    public void The_file_authorised_is_read()
    {
        var file = Path.Combine(_allowed, "a.txt");
        File.WriteAllText(file, "inside");
        var policy = Policy();
        var authorized = policy.Authorize(file, ResourceAccessKind.ReadText);

        using var stream = AuthorizedFile.OpenRead(policy, authorized, ResourceAccessKind.ReadText, "a.txt");
        new StreamReader(stream).ReadToEnd().Should().Be("inside");
    }

    [Theory]
    [InlineData("unparsed-text('{0}')")]
    [InlineData("string(doc('{0}'))")]
    [InlineData("string(json-doc('{0}'))")]
    public async Task A_link_swapped_in_and_out_during_reads_is_never_followed(string call)
    {
        // A file inside the allowed root that another thread keeps replacing with a link to a
        // file outside it, and back. A read gives the file inside or a refusal, never the secret.
        var json = call.Contains("json", StringComparison.Ordinal);
        var xml = call.Contains("doc(", StringComparison.Ordinal) && !json;
        string Wrap(string text) => json ? $"\"{text}\"" : xml ? $"<r>{text}</r>" : text;
        await File.WriteAllTextAsync(_secret, Wrap("SECRET"));
        var file = Path.Combine(_allowed, "a.txt");
        var regular = Path.Combine(_allowed, ".regular");
        var link = Path.Combine(_allowed, ".link");
        await File.WriteAllTextAsync(file, Wrap("inside"));

        using var stop = new CancellationTokenSource();
        var swapper = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    File.WriteAllText(regular, Wrap("inside"));
                    File.Move(regular, file, overwrite: true);
                    if (File.Exists(link) || new FileInfo(link).LinkTarget != null) File.Delete(link);
                    File.CreateSymbolicLink(link, _secret);
                    File.Move(link, file, overwrite: true);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        });

        var query = "try { " + string.Format(System.Globalization.CultureInfo.InvariantCulture, call, new Uri(file).AbsoluteUri)
                             + " } catch * { 'refused' }";
        var leaked = 0;
        var reads = 0;
        try
        {
            for (var i = 0; i < 400; i++)
            {
                var facade = new XQueryFacade { ResourcePolicy = Policy() };
                var result = await facade.EvaluateAsync(query);
                reads++;
                if (result.Contains("SECRET", StringComparison.Ordinal))
                    leaked++;
            }
        }
        finally
        {
            await stop.CancelAsync();
            await swapper;
        }

        leaked.Should().Be(0, $"{reads} reads of a file swapped with a link to one outside the allowed root");
    }
}
