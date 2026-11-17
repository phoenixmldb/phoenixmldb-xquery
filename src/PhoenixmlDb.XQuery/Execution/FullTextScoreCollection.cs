namespace PhoenixmlDb.XQuery.Execution;

/// <summary>The full-text scores made while one score variable's expression was evaluated.</summary>
internal sealed class FullTextScoreCollection
{
    private readonly Dictionary<object, double> _byItem = [];

    /// <summary>The highest score made; null when no full-text match was made.</summary>
    public double? Best { get; private set; }

    public void Add(object? key, double score)
    {
        if (Best is null || score > Best) Best = score;
        if (key is not null && (!_byItem.TryGetValue(key, out var existing) || score > existing))
            _byItem[key] = score;
    }

    /// <summary>
    /// The relevance of <paramref name="item"/> of the binding sequence: the score of the
    /// full-text match made with it as the search context (a predicate <c>[. contains text …]</c>);
    /// else the best score made while evaluating the sequence; 0 when no match was made.
    /// </summary>
    public double ScoreOf(object? item)
    {
        if (item is not null && _byItem.TryGetValue(item is Xdm.Nodes.XdmNode n ? (n.Document, n.Id) : item, out var own))
            return own;
        return Best ?? 0.0;
    }
}
