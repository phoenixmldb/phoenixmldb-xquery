using System.Text.Json;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Functions;

namespace PhoenixmlDb.XQuery;

/// <summary>What <see cref="JsonXmlConverter"/> does with an object that has the same key twice.</summary>
public enum JsonDuplicateKeys
{
    /// <summary>Every entry is kept, in order. The result is then not valid against the schema for the XML representation of JSON.</summary>
    Retain,

    /// <summary>The first entry with a key is kept and the later ones are dropped.</summary>
    UseFirst,

    /// <summary>A repeated key is an error (<c>FOJS0003</c>).</summary>
    Reject,
}

/// <summary>The options of <c>fn:json-to-xml</c> (XPath and XQuery Functions and Operators 3.1 §17.4.3).</summary>
public sealed record JsonToXmlOptions
{
    /// <summary>The options used when none are given: those of <c>fn:json-to-xml</c> called with no options.</summary>
    public static JsonToXmlOptions Default { get; } = new();

    /// <summary>
    /// Whether input that departs from the JSON grammar is accepted where the parser can make
    /// sense of it (a trailing comma). False by default.
    /// </summary>
    public bool Liberal { get; init; }

    /// <summary>What to do with a repeated key. <see cref="JsonDuplicateKeys.Retain"/> by default, as the function's is.</summary>
    public JsonDuplicateKeys Duplicates { get; init; } = JsonDuplicateKeys.Retain;

    /// <summary>
    /// Whether strings keep their JSON escapes. With true, a string that has any is written
    /// escaped and marked <c>escaped="true"</c> (a key, <c>escaped-key="true"</c>), so that a
    /// character XML cannot hold survives a round trip. With false, the default, escapes are
    /// decoded and such a character becomes U+FFFD or goes to <see cref="Fallback"/>.
    /// </summary>
    public bool Escape { get; init; }

    /// <summary>
    /// Called with the escaped form (<c>\uXXXX</c>) of each character that XML 1.0 cannot hold;
    /// what it returns takes the character's place. Null, the default, puts U+FFFD there. It
    /// cannot be combined with <see cref="Escape"/>.
    /// </summary>
    public Func<string, string>? Fallback { get; init; }

    /// <summary>The base URI of the document that is built; null for none.</summary>
    public Uri? BaseUri { get; init; }
}

/// <summary>
/// The one mapping of JSON to XML: the XML representation of JSON that <c>fn:json-to-xml</c>
/// defines (XPath and XQuery Functions and Operators 3.1 §17.4.2), as an API. It is the code the
/// function itself runs, so a host that stores or exchanges JSON as XML gets the tree a query
/// gets, and <c>fn:xml-to-json</c> turns it back.
/// </summary>
/// <remarks>
/// <code>
/// {"tags":["a"],"n":1}
/// </code>
/// becomes
/// <code>
/// &lt;map xmlns="http://www.w3.org/2005/xpath-functions"&gt;
///   &lt;array key="tags"&gt;&lt;string&gt;a&lt;/string&gt;&lt;/array&gt;
///   &lt;number key="n"&gt;1&lt;/number&gt;
/// &lt;/map&gt;
/// </code>
/// (without the white space). An array of one item stays an array, and a number stays a number.
/// </remarks>
public static class JsonXmlConverter
{
    /// <summary>Builds the XML representation of <paramref name="json"/> in <paramref name="builder"/>.</summary>
    /// <param name="json">The JSON text.</param>
    /// <param name="builder">The store the document's nodes are made in.</param>
    /// <param name="options"><see cref="JsonToXmlOptions.Default"/> when null.</param>
    /// <returns>The document node; its one child is the element for the JSON value.</returns>
    /// <exception cref="XQueryException">
    /// <c>FOJS0001</c>: the text is not JSON. <c>FOJS0003</c>: a key is repeated and
    /// <see cref="JsonToXmlOptions.Duplicates"/> is <see cref="JsonDuplicateKeys.Reject"/>.
    /// </exception>
    /// <exception cref="ArgumentException">Both <see cref="JsonToXmlOptions.Escape"/> and a fallback are set.</exception>
    public static XdmDocument ToXml(string json, INodeBuilder builder, JsonToXmlOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(builder);
        options ??= JsonToXmlOptions.Default;
        if (options.Escape && options.Fallback is not null)
            throw new ArgumentException("A fallback cannot be used when Escape is true (FOJS0005).", nameof(options));
        var duplicates = options.Duplicates switch
        {
            JsonDuplicateKeys.Retain => "retain",
            JsonDuplicateKeys.UseFirst => "use-first",
            JsonDuplicateKeys.Reject => "reject",
            _ => throw new ArgumentOutOfRangeException(nameof(options), "Unknown value for Duplicates."),
        };
        Func<string, Task<string>>? fallback = options.Fallback is { } replace
            ? escaped => Task.FromResult(replace(escaped) ?? "")
            : null;
        try
        {
            return JsonToXmlConverter.Convert(json, builder, options.Liberal, duplicates, options.Escape, fallback,
                options.BaseUri?.AbsoluteUri);
        }
        catch (JsonException ex)
        {
            throw new XQueryException("FOJS0001", $"Invalid JSON: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// The XML representation of <paramref name="json"/> as markup, with no XML declaration and
    /// no added white space.
    /// </summary>
    /// <inheritdoc cref="ToXml" path="/exception"/>
    public static string ToXmlText(string json, JsonToXmlOptions? options = null)
    {
        var store = new XdmDocumentStore();
        var document = ToXml(json, store, options);
        return SerializeFunction.SerializeNodeToXml(document, store);
    }
}
