using PhoenixmlDb.Core.Xml;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// SP2 Task 5: opt-in XInclude expansion on the file-parse <c>fn:doc</c> path.
/// <see cref="XdmDocumentStore.XInclude"/> bridges parsed text through
/// <see cref="XIncludeProcessor.Expand"/> before XDM conversion when enabled; a fatal
/// <see cref="XIncludeException"/> propagates through <c>fn:doc</c>'s existing resolver
/// try/catch and surfaces as <c>FODC0002</c> (see DocumentFunctions.cs DocFunction).
/// </summary>
public sealed class XIncludeDocTests
{
    /// <summary>Disposable scratch directory for on-disk XInclude fixtures.</summary>
    private sealed class TempDir : IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "phoenixmldb-xinclude-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public string Write(string fileName, string content)
        {
            var fullPath = System.IO.Path.Combine(Path, fileName);
            File.WriteAllText(fullPath, content);
            return fullPath;
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }

    private static async Task<string?> RunQueryAsync(string query, XdmDocumentStore store)
    {
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store);
        var compilation = engine.Compile(query);
        Assert.True(compilation.Success, string.Join("; ", compilation.Errors.Select(e => e.Message)));

        var context = engine.CreateContext();

        object? result = null;
        await foreach (var item in compilation.ExecutionPlan!.ExecuteAsync(context))
        {
            result = item;
            break;
        }
        return result?.ToString();
    }

    [Fact]
    public void Store_expands_xinclude_when_enabled()
    {
        using var dir = new TempDir();
        dir.Write("part.xml", "<part>included</part>");
        var mainPath = dir.Write("main.xml",
            "<main xmlns:xi='http://www.w3.org/2001/XInclude'><xi:include href='part.xml'/></main>");

        var store = new XdmDocumentStore { XInclude = new XIncludeOptions { Enabled = true } };
        var doc = store.LoadFile(mainPath);

        // The included <part> content is now present in the parsed XDM document's string value.
        Assert.Contains("included", doc.StringValue);
    }

    [Fact]
    public void Store_leaves_document_unchanged_when_disabled()
    {
        using var dir = new TempDir();
        dir.Write("part.xml", "<part>included</part>");
        var mainPath = dir.Write("main.xml",
            "<main xmlns:xi='http://www.w3.org/2001/XInclude'><xi:include href='part.xml'/></main>");

        var store = new XdmDocumentStore(); // XInclude off (default)
        var doc = store.LoadFile(mainPath);

        // xi:include element is left intact; no expansion occurred.
        Assert.DoesNotContain("included", doc.StringValue);
    }

    [Fact]
    public async Task Doc_expands_xinclude_end_to_end_via_fn_doc()
    {
        using var dir = new TempDir();
        dir.Write("part.xml", "<part>included</part>");
        var mainPath = dir.Write("main.xml",
            "<main xmlns:xi='http://www.w3.org/2001/XInclude'><xi:include href='part.xml'/></main>");
        var uri = new Uri(mainPath).AbsoluteUri;

        var store = new XdmDocumentStore { XInclude = new XIncludeOptions { Enabled = true } };
        var result = await RunQueryAsync($"string(doc('{uri}'))", store);

        Assert.Equal("included", result);
    }

    [Fact]
    public async Task Fatal_xinclude_on_fn_doc_maps_to_FODC0002()
    {
        using var dir = new TempDir();
        var mainPath = dir.Write("main.xml",
            "<main xmlns:xi='http://www.w3.org/2001/XInclude'><xi:include href='missing.xml'/></main>");
        var uri = new Uri(mainPath).AbsoluteUri;

        var store = new XdmDocumentStore { XInclude = new XIncludeOptions { Enabled = true } };

        var ex = await Assert.ThrowsAsync<XQueryRuntimeException>(
            () => RunQueryAsync($"doc('{uri}')", store));
        Assert.Equal("FODC0002", ex.ErrorCode);
    }

    [Fact]
    public void Store_honors_declared_encoding_when_expanding_xinclude()
    {
        // A BOM-less document that declares a non-UTF-8 encoding must be decoded per its XML
        // declaration on the XInclude-enabled path, not blindly as UTF-8. (Regression guard: the
        // bridge loads the intermediate DOM via an encoding-aware XmlReader, not a bare StreamReader.)
        using var dir = new TempDir();
        dir.Write("part.xml", "<part>inc</part>");
        var mainPath = System.IO.Path.Combine(dir.Path, "main.xml");
        var latin1 = System.Text.Encoding.GetEncoding("iso-8859-1");
        File.WriteAllBytes(mainPath, latin1.GetBytes(
            "<?xml version='1.0' encoding='iso-8859-1'?>" +
            "<main xmlns:xi='http://www.w3.org/2001/XInclude'><n>café</n>" +
            "<xi:include href='part.xml'/></main>"));

        var store = new XdmDocumentStore { XInclude = new XIncludeOptions { Enabled = true } };
        var doc = store.LoadFile(mainPath);

        Assert.Contains("café", doc.StringValue); // correctly decoded (é), not mojibake
        Assert.Contains("inc", doc.StringValue);        // include still expanded
    }

    [Fact]
    public void Fatal_xinclude_on_LoadFile_throws_XIncludeException()
    {
        using var dir = new TempDir();
        var mainPath = dir.Write("main.xml",
            "<main xmlns:xi='http://www.w3.org/2001/XInclude'><xi:include href='missing.xml'/></main>");

        var store = new XdmDocumentStore { XInclude = new XIncludeOptions { Enabled = true } };

        var ex = Assert.Throws<XIncludeException>(() => store.LoadFile(mainPath));
        Assert.True(ex.IsFatal);
    }
}
