namespace PhoenixmlDb.XQuery.FullText;

/// <summary>
/// The stop word lists and thesauri a query names by URI (<c>using stop words at "…"</c>,
/// <c>using thesaurus at "…"</c>), and the defaults (design 07 §5.2). The query engine resolves a
/// relative URI against the static base URI before asking. A null answer means the resource is
/// not available: FTST0008 for a stop word list, FTST0018 for a thesaurus.
/// </summary>
/// <remarks>
/// A host decides where resources come from (documents in a database, files it trusts, a fixed
/// set). The engine never reads a file for a full-text resource by itself.
/// </remarks>
public interface IFullTextResourceResolver
{
    /// <summary>The stop words at <paramref name="uri"/>, or null when there is no such list.</summary>
    IReadOnlyCollection<string>? GetStopWords(string uri);

    /// <summary>The list of <c>using stop words default</c> for a language; null for the analyzer's built-in list.</summary>
    IReadOnlyCollection<string>? GetDefaultStopWords(string? language);

    /// <summary>The thesaurus at <paramref name="uri"/>, or null when there is no such thesaurus.</summary>
    FullTextThesaurus? GetThesaurus(string uri);

    /// <summary>The thesaurus of <c>using thesaurus default</c> for a language; null for none (no expansion).</summary>
    FullTextThesaurus? GetDefaultThesaurus(string? language);
}

/// <summary>
/// An in-memory <see cref="IFullTextResourceResolver"/>: stop word lists and thesauri registered by URI.
/// </summary>
public sealed class FullTextResources : IFullTextResourceResolver
{
    private readonly Dictionary<string, IReadOnlyCollection<string>> _stopWords = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FullTextThesaurus> _thesauri = new(StringComparer.Ordinal);

    /// <summary>The list of <c>using stop words default</c>; null for the analyzer's built-in list.</summary>
    public IReadOnlyCollection<string>? StopWordsByDefault { get; set; }

    /// <summary>The thesaurus of <c>using thesaurus default</c>; null for none.</summary>
    public FullTextThesaurus? ThesaurusByDefault { get; set; }

    /// <summary>Registers the stop word list at <paramref name="uri"/>.</summary>
    public FullTextResources AddStopWords(string uri, IEnumerable<string> words)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(words);
        _stopWords[uri] = words.ToList();
        return this;
    }

    /// <summary>Registers the thesaurus at <paramref name="uri"/>.</summary>
    public FullTextResources AddThesaurus(string uri, FullTextThesaurus thesaurus)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(thesaurus);
        _thesauri[uri] = thesaurus;
        return this;
    }

    /// <summary>
    /// The words of a stop word list: either the native XML form
    /// (<c>&lt;stop-words&gt;&lt;word&gt;the&lt;/word&gt;…</c>, design 07 §8.1) or plain text, one word per line.
    /// </summary>
    public static IReadOnlyList<string> ParseStopWords(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var trimmed = text.TrimStart();
        if (trimmed.StartsWith('<'))
        {
            var root = System.Xml.Linq.XElement.Parse(trimmed);
            return root.Descendants().Where(e => e.Name.LocalName == "word").Select(e => e.Value.Trim()).Where(w => w.Length > 0).ToList();
        }
        return text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<string>? GetStopWords(string uri) => _stopWords.GetValueOrDefault(uri);

    /// <inheritdoc/>
    public IReadOnlyCollection<string>? GetDefaultStopWords(string? language) => StopWordsByDefault;

    /// <inheritdoc/>
    public FullTextThesaurus? GetThesaurus(string uri) => _thesauri.GetValueOrDefault(uri);

    /// <inheritdoc/>
    public FullTextThesaurus? GetDefaultThesaurus(string? language) => ThesaurusByDefault;
}
