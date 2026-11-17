using System.Xml.Linq;

namespace PhoenixmlDb.XQuery.FullText;

/// <summary>
/// A thesaurus: terms (tokens or phrases) and their related terms, each by a relationship
/// (Full Text 3.0 §3.4.3; ISO 2788 names such as UF, USE, BT, NT, RT, or any other name).
/// </summary>
/// <remarks>
/// Terms compare without case and with runs of white space as one space. The hierarchical
/// relationships (BT, NT, BTG, NTG, BTP, NTP, TT) are followed through as many levels as a
/// query's <c>levels</c> allow; any other relationship is one level.
/// </remarks>
public sealed class FullTextThesaurus
{
    private static readonly HashSet<string> Hierarchical =
        new(["BT", "NT", "BTG", "NTG", "BTP", "NTP", "TT"], StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, List<(string Relationship, string Term)>> _entries = new(StringComparer.Ordinal);

    /// <summary>The language the thesaurus is for, when it says; informational.</summary>
    public string? Language { get; init; }

    /// <summary>Adds <paramref name="related"/> to <paramref name="term"/> by <paramref name="relationship"/>.</summary>
    public FullTextThesaurus Add(string term, string relationship, string related)
    {
        ArgumentNullException.ThrowIfNull(term);
        ArgumentNullException.ThrowIfNull(relationship);
        ArgumentNullException.ThrowIfNull(related);
        var key = Key(term);
        if (!_entries.TryGetValue(key, out var list))
            _entries[key] = list = [];
        list.Add((relationship.Trim(), Normalize(related)));
        return this;
    }

    /// <summary>
    /// The terms related to <paramref name="phrase"/> by <paramref name="relationship"/> (every
    /// relationship when null), from level <paramref name="minLevel"/> to
    /// <paramref name="maxLevel"/> of a hierarchical relationship. The phrase itself is level 0.
    /// </summary>
    public IReadOnlyList<string> Expand(string phrase, string? relationship, long minLevel = 1, long maxLevel = long.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(phrase);
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal) { Key(phrase) };
        var frontier = new List<string> { Normalize(phrase) };
        if (minLevel <= 0)
            result.Add(Normalize(phrase));
        for (long level = 1; level <= maxLevel && frontier.Count > 0; level++)
        {
            var next = new List<string>();
            foreach (var term in frontier)
            {
                if (!_entries.TryGetValue(Key(term), out var related))
                    continue;
                foreach (var (rel, other) in related)
                {
                    if (relationship is not null && !string.Equals(rel, relationship.Trim(), StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (level > 1 && !Hierarchical.Contains(rel))
                        continue;
                    if (seen.Add(Key(other)))
                        next.Add(other);
                }
            }
            if (level >= minLevel)
                result.AddRange(next);
            frontier = next;
            // Only a hierarchical relationship has levels below the first.
            if (relationship is not null && !Hierarchical.Contains(relationship.Trim()))
                break;
        }
        return result;
    }

    /// <summary>
    /// Loads a thesaurus document: the W3C test suite form (<c>entry/term</c>, <c>entry/synonym/term</c>,
    /// <c>synonym/relationship</c>) or the native form of design 07 §8.2
    /// (<c>entry/@term</c>, <c>entry/related/@term</c>, <c>related/@relationship</c>).
    /// </summary>
    public static FullTextThesaurus Load(XDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var root = document.Root ?? throw new ArgumentException("The thesaurus document has no root element", nameof(document));
        var thesaurus = new FullTextThesaurus { Language = (string?)root.Attribute("language") };
        foreach (var entry in root.Elements().Where(e => e.Name.LocalName == "entry"))
            AddRelated(thesaurus, entry);
        return thesaurus;

        // A related term may hold related terms of its own (the next level down, as in the W3C
        // test suite's usability thesaurus: infrastructure > networks > Web).
        static void AddRelated(FullTextThesaurus thesaurus, XElement entry)
        {
            var term = TermOf(entry);
            if (string.IsNullOrWhiteSpace(term))
                return;
            foreach (var related in entry.Elements().Where(e => e.Name.LocalName is "synonym" or "related"))
            {
                var other = TermOf(related);
                var relationship = (string?)related.Attribute("relationship")
                                   ?? related.Elements().FirstOrDefault(e => e.Name.LocalName == "relationship")?.Value
                                   ?? "";
                if (string.IsNullOrWhiteSpace(other))
                    continue;
                thesaurus.Add(term, relationship, other);
                AddRelated(thesaurus, related);
            }
        }

        static string? TermOf(XElement e)
            => (string?)e.Attribute("term") ?? e.Elements().FirstOrDefault(c => c.Name.LocalName == "term")?.Value;
    }

    private static string Normalize(string s) => string.Join(' ', s.Split((char[])[' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));

    private static string Key(string s) => Normalize(s).ToLowerInvariant();
}
