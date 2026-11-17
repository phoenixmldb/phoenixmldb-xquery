using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// The match options of one primary or of a <c>declare ft-option</c> [221]: at most one option
/// per group; null means the group is not given here and is inherited
/// (<c>fts:replaceMatchOptions</c>, Full Text 3.0 §4.2.5.2).
/// </summary>
public sealed class FtMatchOptions
{
    /// <summary><c>using language "en"</c>.</summary>
    public string? Language { get; set; }
    /// <summary><c>using wildcards</c> / <c>using no wildcards</c>.</summary>
    public bool? Wildcards { get; set; }
    /// <summary><c>using stemming</c> / <c>using no stemming</c>.</summary>
    public bool? Stemming { get; set; }
    /// <summary>The case option.</summary>
    public FtCaseMode? Case { get; set; }
    /// <summary><c>using diacritics sensitive</c> (true) / <c>insensitive</c> (false).</summary>
    public bool? DiacriticsSensitive { get; set; }
    /// <summary>The stop word option.</summary>
    public FtStopWordOption? StopWords { get; set; }
    /// <summary>The thesaurus option.</summary>
    public FtThesaurusOption? Thesaurus { get; set; }
    /// <summary><c>using option name "value"</c> [234], in order.</summary>
    public List<(QName Name, string Value)> Extensions { get; } = [];

    /// <summary>Case sensitivity, for code that predates the four case options.</summary>
    public bool? CaseSensitive => Case switch
    {
        null => null,
        FtCaseMode.Insensitive => false,
        _ => true,
    };

    /// <summary>
    /// <paramref name="inner"/> over these options: each group the inner options give replaces
    /// this one's (<c>fts:replaceMatchOptions</c>).
    /// </summary>
    public FtMatchOptions With(FtMatchOptions? inner)
    {
        if (inner is null)
            return this;
        var result = new FtMatchOptions
        {
            Language = inner.Language ?? Language,
            Wildcards = inner.Wildcards ?? Wildcards,
            Stemming = inner.Stemming ?? Stemming,
            Case = inner.Case ?? Case,
            DiacriticsSensitive = inner.DiacriticsSensitive ?? DiacriticsSensitive,
            StopWords = inner.StopWords ?? StopWords,
            Thesaurus = inner.Thesaurus ?? Thesaurus,
        };
        result.Extensions.AddRange(Extensions);
        result.Extensions.AddRange(inner.Extensions);
        return result;
    }
}

/// <summary>FTCaseOption [223].</summary>
public enum FtCaseMode
{
    Insensitive,
    Sensitive,
    Lowercase,
    Uppercase,
}

/// <summary>
/// FTStopWordOption [229]: <c>no stop words</c>, or a base list (default, a URI or literal
/// words) followed by <c>union</c>/<c>except</c> lists, applied left to right.
/// </summary>
public sealed class FtStopWordOption
{
    /// <summary><c>using no stop words</c>.</summary>
    public bool None { get; init; }
    /// <summary>The first list; null with <see cref="None"/>.</summary>
    public FtStopWordList? Base { get; init; }
    /// <summary>The <c>union</c> (true) and <c>except</c> (false) lists, in order.</summary>
    public IReadOnlyList<(bool Union, FtStopWordList List)> Operations { get; init; } = [];
}

/// <summary>A stop word list: the default list, one named by URI, or literal words.</summary>
public sealed class FtStopWordList
{
    public bool IsDefault { get; init; }
    public string? Uri { get; init; }
    public IReadOnlyList<string>? Words { get; init; }
}

/// <summary>FTThesaurusOption [226]: <c>no thesaurus</c>, or one or more thesauri.</summary>
public sealed class FtThesaurusOption
{
    public bool None { get; init; }
    public IReadOnlyList<FtThesaurusId> Thesauri { get; init; } = [];
}

/// <summary>FTThesaurusID [227], or <c>default</c> (Uri null).</summary>
public sealed class FtThesaurusId
{
    /// <summary>The thesaurus URI; null for <c>default</c>.</summary>
    public string? Uri { get; init; }
    public string? Relationship { get; init; }
    /// <summary>The levels: the kind and its literal bounds.</summary>
    public (FtRangeKind Kind, long First, long? Second)? Levels { get; init; }
}
