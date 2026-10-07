using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Functions;

/// <summary>
/// The error codes of fn:unparsed-text when the content and the encoding disagree
/// (F&amp;O 3.1 §14.8.5): FOUT1190 when an encoding was named, FOUT1200 only when none was and
/// none could be inferred.
/// </summary>
public sealed class UnparsedTextEncodingErrorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "phx-ute-" + Guid.NewGuid().ToString("N"));

    public UnparsedTextEncodingErrorTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private async Task<string> FileAsync(string name, byte[] content)
    {
        var path = Path.Combine(_dir, name);
        await File.WriteAllBytesAsync(path, content);
        return new Uri(path).AbsoluteUri;
    }

    private static async Task<(object? Value, string? Error)> RunAsync(string query)
    {
        var store = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store);
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors));
        try
        {
            object? last = null;
            await foreach (var item in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
                last = item;
            return (last, null);
        }
        catch (XQueryRuntimeException ex)
        {
            return (null, ex.ErrorCode);
        }
    }

    private static readonly byte[] NotUtf8 = [0x61, 0xC3, 0x28];
    private static readonly byte[] NotXml = [0x61, 0x01, 0x62];

    [Fact]
    public async Task Octets_the_named_encoding_cannot_decode_are_FOUT1190()
    {
        var uri = await FileAsync("bad.txt", NotUtf8);
        (await RunAsync($"unparsed-text('{uri}', 'utf-8')")).Error.Should().Be("FOUT1190");
    }

    [Fact]
    public async Task Characters_XML_does_not_permit_under_a_named_encoding_are_FOUT1190()
    {
        var uri = await FileAsync("ctl.txt", NotXml);
        (await RunAsync($"unparsed-text('{uri}', 'utf-8')")).Error.Should().Be("FOUT1190");
    }

    [Fact]
    public async Task With_no_encoding_named_undecodable_content_is_still_FOUT1200()
    {
        var uri = await FileAsync("bad.txt", NotUtf8);
        (await RunAsync($"unparsed-text('{uri}')")).Error.Should().Be("FOUT1200");
    }

    /// <summary>
    /// .NET knows utf-7 by name and refuses to provide it, with NotSupportedException rather
    /// than the ArgumentException an unknown name gives. That escaped both functions raw.
    /// </summary>
    [Fact]
    public async Task An_encoding_the_runtime_refuses_is_unavailable_not_a_crash()
    {
        var uri = await FileAsync("ok.txt", "abc"u8.ToArray());
        (await RunAsync($"unparsed-text-available('{uri}', 'utf-7')")).Value.Should().Be(false);
        (await RunAsync($"unparsed-text('{uri}', 'utf-7')")).Error.Should().Be("FOUT1190");
        (await RunAsync($"unparsed-text-available('{uri}', 'utf-8')")).Value.Should().Be(true);
    }
}
