using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.FullText;

/// <summary>
/// Narrows the search context items a <c>contains text</c> evaluates (design 07 §5.4): a host
/// with a full-text index says which items cannot match, and the evaluator skips them. The
/// evaluator still decides every other item, so a source can change only speed, never answers,
/// as long as it never rules out an item that could match.
/// </summary>
/// <remarks>
/// The engine implements it over its index. An index that leaves out stop words, does not fold
/// diacritics, or analyzes differently from the query cannot give that guarantee for every
/// query; such a source returns null from <see cref="Prepare"/> for the queries it cannot serve.
/// </remarks>
public interface IFullTextCandidateSource
{
    /// <summary>
    /// A filter for one evaluation of <paramref name="query"/> in <paramref name="container"/>,
    /// or null to evaluate every item.
    /// </summary>
    IFullTextCandidateFilter? Prepare(FtCandidateQuery query, ContainerId container);
}

/// <summary>The items one evaluation may skip.</summary>
public interface IFullTextCandidateFilter
{
    /// <summary>
    /// False only when <paramref name="item"/> cannot match: no alternative of the query has all
    /// its terms in the item. True for any item the source does not know (a constructed node, a
    /// document it does not index, an atomic value).
    /// </summary>
    bool MayMatch(object? item);
}

/// <summary>
/// What a <c>contains text</c> requires of an item, for a candidate source: it can match only if
/// it holds every term of at least one alternative. Terms are the query's tokens as written
/// (lowercase not applied), for the source to analyze as its index does.
/// </summary>
/// <param name="Alternatives">One or more alternatives, each one or more terms.</param>
/// <param name="Language">The query's language option, if any.</param>
/// <param name="Stemming">Whether the query stems (an index that stems can serve both).</param>
/// <param name="CaseSensitive">Whether the query is case sensitive (a case-folded index can serve both).</param>
/// <param name="DiacriticsSensitive">Whether the query is diacritics sensitive (a folded index can serve both).</param>
public sealed record FtCandidateQuery(
    IReadOnlyList<IReadOnlyList<string>> Alternatives,
    string? Language,
    bool Stemming,
    bool CaseSensitive,
    bool DiacriticsSensitive);
