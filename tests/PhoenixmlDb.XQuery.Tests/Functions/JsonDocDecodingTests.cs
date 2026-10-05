using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Functions;

/// <summary>
/// fn:json-doc reads its resource as fn:unparsed-text does: bytes that are not valid in the
/// inferred encoding are FOUT1200, and a resource that cannot be retrieved is FOUT1170.
/// </summary>
/// <remarks>
/// The file was decoded leniently: invalid UTF-8 became U+FFFD, and the JSON parser then reported
/// FOJS0001 about a character the file does not contain (QT3 misc-JsonTestSuite). A missing file
/// was FOJS0001 too (json-doc-error-028..032).
/// </remarks>
public sealed class JsonDocDecodingTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "phx-jdd-" + Guid.NewGuid().ToString("N"));
    private readonly XQueryFacade _facade = new();

    public JsonDocDecodingTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private async Task<string> Outcome(byte[] content, string fn = "json-doc")
    {
        var path = Path.Combine(_dir, "f.json");
        await File.WriteAllBytesAsync(path, content);
        try { return "value " + await _facade.EvaluateAsync($"{fn}('{new Uri(path).AbsoluteUri}') => serialize(map{{'method':'json'}})"); }
        catch (XQueryRuntimeException e) { return e.ErrorCode; }
    }

    [Fact]
    public async Task InvalidUtf8_IsFOUT1200()
        => (await Outcome([(byte)'[', (byte)'"', 0xFF, (byte)'"', (byte)']'])).Should().Be("FOUT1200");

    [Fact]
    public async Task InvalidUtf8_IsFOUT1200_ThroughTheTwoArgumentForm()
    {
        var path = Path.Combine(_dir, "g.json");
        await File.WriteAllBytesAsync(path, [(byte)'[', 0xC3, (byte)']']);
        var act = () => _facade.EvaluateAsync($"json-doc('{new Uri(path).AbsoluteUri}', map{{}})");
        (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be("FOUT1200");
    }

    [Fact]
    public async Task AMissingResource_IsFOUT1170()
    {
        var act = () => _facade.EvaluateAsync($"json-doc('{new Uri(Path.Combine(_dir, "absent.json")).AbsoluteUri}')");
        (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be("FOUT1170");
    }

    // Controls: valid UTF-8, with and without a byte order mark, still parses.
    [Fact]
    public async Task ValidUtf8_Parses()
        => (await Outcome("[\"é\"]"u8.ToArray())).Should().Be("value [\"é\"]");

    [Fact]
    public async Task ValidUtf8WithABom_Parses()
        => (await Outcome([0xEF, 0xBB, 0xBF, .. "[1]"u8.ToArray()])).Should().Be("value [1]");
}
