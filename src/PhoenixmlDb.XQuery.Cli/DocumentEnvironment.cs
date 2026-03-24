using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Cli;

/// <summary>
/// Wraps <see cref="XdmDocumentStore"/> with CLI-specific helpers:
/// URL loading, directory scanning, and file resolution by path.
/// The underlying <see cref="Store"/> is used as the node provider
/// so that element constructors produce real XDM nodes (not strings).
/// </summary>
internal sealed class DocumentEnvironment
{
    public XdmDocumentStore Store { get; } = new();

    public IReadOnlyList<XdmDocument> Documents => Store.Documents;

    public XdmDocument LoadFile(string filePath) => Store.LoadFile(filePath);

    public XdmDocument LoadFromString(string xml, string? documentUri = null) =>
        Store.LoadFromString(xml, documentUri);

    /// <summary>
    /// Loads an XML document from a URL.
    /// </summary>
    public XdmDocument LoadFromUrl(string url)
    {
        var existing = Store.ResolveDocument(url);
        if (existing != null)
            return existing;

        using var client = new HttpClient();
        using var stream = client.GetStreamAsync(new Uri(url)).GetAwaiter().GetResult();
        return Store.LoadFromStream(stream, url);
    }

    /// <summary>
    /// Loads all XML files from a directory.
    /// </summary>
    public IReadOnlyList<XdmDocument> LoadDirectory(string directoryPath)
    {
        var docs = new List<XdmDocument>();
        var xmlFiles = Directory.GetFiles(directoryPath, "*.xml", SearchOption.AllDirectories);

        foreach (var file in xmlFiles.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                docs.Add(Store.LoadFile(file));
            }
            catch (System.Xml.XmlException)
            {
                // Skip malformed XML files silently in directory scan
            }
        }

        return docs;
    }
}
