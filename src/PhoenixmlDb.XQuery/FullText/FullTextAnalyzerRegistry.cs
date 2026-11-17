using System.Collections.Concurrent;

namespace PhoenixmlDb.XQuery.FullText;

/// <summary>
/// The analyzers a query engine uses, by language, with a fallback for a language that has
/// none. A language tag is matched exactly first, then by its primary subtag ("en-GB" finds an
/// analyzer registered for "en"), case-insensitively.
/// </summary>
public sealed class FullTextAnalyzerRegistry
{
    private readonly ConcurrentDictionary<string, IFullTextAnalyzer> _byLanguage =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A registry with only the built-in <see cref="StandardFullTextAnalyzer"/>.</summary>
    public FullTextAnalyzerRegistry()
        : this(StandardFullTextAnalyzer.Instance)
    {
    }

    /// <summary>A registry whose fallback is <paramref name="fallback"/>.</summary>
    public FullTextAnalyzerRegistry(IFullTextAnalyzer fallback)
    {
        ArgumentNullException.ThrowIfNull(fallback);
        Fallback = fallback;
    }

    /// <summary>The registry every engine uses when it is given none. Do not change it.</summary>
    internal static FullTextAnalyzerRegistry Default { get; } = new();

    /// <summary>The analyzer for a language with no registration of its own, and for no language.</summary>
    public IFullTextAnalyzer Fallback { get; }

    /// <summary>Registers <paramref name="analyzer"/> for <paramref name="language"/>, replacing any earlier one.</summary>
    public FullTextAnalyzerRegistry Register(string language, IFullTextAnalyzer analyzer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        ArgumentNullException.ThrowIfNull(analyzer);
        _byLanguage[language.Trim()] = analyzer;
        return this;
    }

    /// <summary>The analyzer for <paramref name="language"/>.</summary>
    public IFullTextAnalyzer Resolve(string? language)
    {
        if (string.IsNullOrWhiteSpace(language) || _byLanguage.IsEmpty)
            return Fallback;
        language = language.Trim();
        if (_byLanguage.TryGetValue(language, out var exact))
            return exact;
        var dash = language.IndexOf('-', StringComparison.Ordinal);
        if (dash > 0 && _byLanguage.TryGetValue(language[..dash], out var primary))
            return primary;
        return Fallback;
    }
}
