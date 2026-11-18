using System.Text.Json;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Functions;

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

    private readonly List<object?> _jsonItems = [];

    /// <summary>The maps and arrays of the JSON sources, in load order.</summary>
    public IReadOnlyList<object?> JsonItems => _jsonItems;

    /// <summary>Whether <paramref name="path"/> names a JSON file, by its extension.</summary>
    public static bool IsJsonFile(string path) => Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase);

    /// <summary>A JSON file: its map or array, as json-doc() gives it.</summary>
    public object? LoadJsonFile(string filePath) => LoadJsonFromString(File.ReadAllText(filePath), filePath);

    /// <summary>JSON text: its map or array, as parse-json() gives it.</summary>
    public object? LoadJsonFromString(string json, string source)
    {
        object? item;
        try
        {
            using var doc = JsonDocument.Parse(JsonToXdmConverter.PreProcessSurrogates(json));
            item = JsonToXdmConverter.Convert(doc.RootElement);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"'{source}' is not valid JSON: {e.Message}", e);
        }
        _jsonItems.Add(item);
        return item;
    }

    /// <summary>
    /// When there are JSON sources, collection() gives the XML documents and then the JSON items;
    /// otherwise it is left as it was (every loaded document).
    /// </summary>
    public void PublishDefaultCollection()
    {
        if (_jsonItems.Count == 0)
            return;
        var items = new List<object?>(Store.Documents.Count + _jsonItems.Count);
        items.AddRange(Store.Documents);
        items.AddRange(_jsonItems);
        Store.SetDefaultCollection(items);
    }

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
    /// Loads all XML files from a directory, and its JSON files into <see cref="JsonItems"/>.
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

        var jsonFiles = Directory.GetFiles(directoryPath, "*.json", SearchOption.AllDirectories);
        foreach (var file in jsonFiles.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                LoadJsonFile(file);
            }
            catch (InvalidDataException)
            {
                // Skipped, as a malformed XML file is
            }
        }

        return docs;
    }
}
