namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Full-text match options (stemming, language, case, etc.).
/// </summary>
public sealed class FtMatchOptions
{
    /// <summary>Enable/disable stemming (null = default).</summary>
    public bool? Stemming { get; set; }
    /// <summary>Language for stemming/tokenization (e.g., "en", "de").</summary>
    public string? Language { get; set; }
    /// <summary>Enable/disable wildcards (null = default).</summary>
    public bool? Wildcards { get; set; }
    /// <summary>Case sensitivity (null = default/insensitive).</summary>
    public bool? CaseSensitive { get; set; }
    /// <summary>Diacritics sensitivity (null = default/insensitive).</summary>
    public bool? DiacriticsSensitive { get; set; }
    /// <summary>Custom stop words list.</summary>
    public IReadOnlyList<string>? StopWords { get; set; }
    /// <summary>Disable all stop words.</summary>
    public bool NoStopWords { get; set; }
    /// <summary>Thesaurus file path.</summary>
    public string? Thesaurus { get; set; }
}
