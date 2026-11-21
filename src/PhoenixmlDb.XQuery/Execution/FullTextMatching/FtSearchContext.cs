using System.Globalization;
using System.Text;
using PhoenixmlDb.XQuery.FullText;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Execution.FullTextMatching;

/// <summary>
/// One search context item, its text, and its tokenizations (Full Text 3.0 §4.1).
/// </summary>
/// <remarks>
/// <para>
/// Tokens come from the string value: the text of the item's descendant text nodes, without
/// those under a node of <c>without content</c>. Comments, processing instructions and attributes
/// of descendants contribute nothing (§4.1.1).
/// </para>
/// <para>
/// Sentences and paragraphs are implementation-defined; this numbering follows the
/// specification's sample tokenization (§4.1.1): a paragraph ends at an element start or end
/// tag, and a sentence ends at a paragraph end or at ".", "!" or "?". Numbers start at 1.
/// </para>
/// </remarks>
internal sealed class FtSearchContext
{
    private readonly List<int> _boundaries;   // offsets in Text where an element tag was
    private readonly Dictionary<(string? Language, bool Stemming, bool CaseSensitive), FtTokenizedText> _cache = [];

    private FtSearchContext(string text, List<int> boundaries)
    {
        Text = text;
        _boundaries = boundaries;
    }

    public string Text { get; }

    /// <summary>The search context of <paramref name="item"/> with the nodes of <paramref name="ignored"/> pruned; null when the item itself is ignored.</summary>
    public static FtSearchContext? Of(object? item, HashSet<object>? ignored, QueryExecutionContext context)
    {
        if (item is XdmNode node)
        {
            if (ignored is not null && ignored.Contains(Key(node)))
                return null;
            var text = new StringBuilder();
            var boundaries = new List<int>();
            switch (node)
            {
                case XdmElement or XdmDocument:
                    Walk(node, text, boundaries, ignored, context, isRoot: true);
                    break;
                default:
                    text.Append(node.StringValue);
                    break;
            }
            return new FtSearchContext(text.ToString(), boundaries);
        }
        var atomized = context.AtomizeWithNodes(item);
        return new FtSearchContext(atomized is null ? "" : Convert.ToString(atomized, CultureInfo.InvariantCulture) ?? "", []);
    }

    internal static object Key(XdmNode node) => (node.Document, node.Id);

    private static void Walk(XdmNode node, StringBuilder text, List<int> boundaries, HashSet<object>? ignored, QueryExecutionContext context, bool isRoot)
    {
        if (!isRoot && ignored is not null && ignored.Contains(Key(node)))
            return;
        switch (node)
        {
            case XdmText t:
                text.Append(t.Value);
                break;
            case XdmElement e:
                if (!isRoot) boundaries.Add(text.Length);
                foreach (var childId in e.Children)
                    if (context.LoadNode(e, childId) is { } child)
                        Walk(child, text, boundaries, ignored, context, isRoot: false);
                if (!isRoot) boundaries.Add(text.Length);
                break;
            case XdmDocument d:
                foreach (var childId in d.Children)
                    if (context.LoadNode(d, childId) is { } child)
                        Walk(child, text, boundaries, ignored, context, isRoot: false);
                break;
        }
    }

    /// <summary>
    /// The tokens of the text, each segment between two element tags analyzed by itself: a
    /// token never crosses a tag (§4.1: tokenization takes the structure into account). The
    /// string value of &lt;p&gt;simple example.&lt;/p&gt;&lt;p&gt;It&lt;/p&gt; reads "example.It", which is one
    /// word to UAX #29. Offsets are into <see cref="Text"/>; positions run on across segments.
    /// </summary>
    private List<FtToken> Segmented(IFullTextAnalyzer analyzer, FtAnalysisRequest request)
    {
        var result = new List<FtToken>();
        var next = 0;
        var start = 0;
        foreach (var end in _boundaries.Where(SplitsTokens).Append(Text.Length).Distinct())
        {
            if (end > start)
            {
                var segment = analyzer.Analyze(Text[start..end], request);
                if (segment.Count > 0)
                {
                    var shift = next - segment[0].Position;
                    foreach (var token in segment)
                        result.Add(token with
                        {
                            Position = token.Position + shift,
                            StartOffset = token.StartOffset + start,
                            EndOffset = token.EndOffset + start,
                        });
                    next = result[^1].Position + 1;
                }
            }
            start = Math.Max(start, end);
        }
        return result;
    }

    /// <summary>
    /// Whether the tag at <paramref name="offset"/> ends a token. Not when a letter or digit
    /// touches it on both sides: highlighting markup inside a word ("paper &lt;b&gt;p&lt;/b&gt;rototypes",
    /// the use case of a query across descendant elements) leaves one word.
    /// </summary>
    private bool SplitsTokens(int offset)
        => offset <= 0 || offset >= Text.Length
           || !(char.IsLetterOrDigit(Text[offset - 1]) && char.IsLetterOrDigit(Text[offset]));

    /// <summary>The tokens of the text under one set of analysis settings, cached.</summary>
    public FtTokenizedText Tokenize(IFullTextAnalyzer analyzer, FtAnalysisRequest request)
    {
        var key = (request.Language, request.Stemming, request.CaseSensitive);
        if (_cache.TryGetValue(key, out var cached))
            return cached;
        var result = new FtTokenizedText(Text, Segmented(analyzer, request), _boundaries);
        _cache[key] = result;
        return result;
    }
}

/// <summary>The tokens of one search context, by position, with their sentence and paragraph numbers.</summary>
internal sealed class FtTokenizedText
{
    private readonly Dictionary<int, int> _indexByPosition = [];

    public FtTokenizedText(string text, IReadOnlyList<FtToken> tokens, List<int> boundaries)
    {
        Tokens = tokens;
        Sentences = new int[tokens.Count];
        Paragraphs = new int[tokens.Count];
        int sentence = 1, paragraph = 1, boundary = 0, previousEnd = -1;
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (i > 0)
            {
                var newParagraph = false;
                while (boundary < boundaries.Count && boundaries[boundary] <= token.StartOffset)
                {
                    if (boundaries[boundary] >= previousEnd) newParagraph = true;
                    boundary++;
                }
                var gap = text.AsSpan(previousEnd, Math.Max(0, token.StartOffset - previousEnd));
                if (newParagraph)
                {
                    paragraph++;
                    sentence++;
                }
                else if (gap.IndexOfAny(".!?") >= 0)
                {
                    sentence++;
                }
            }
            else
            {
                while (boundary < boundaries.Count && boundaries[boundary] <= token.StartOffset)
                    boundary++;
            }
            Sentences[i] = sentence;
            Paragraphs[i] = paragraph;
            previousEnd = token.EndOffset;
            _indexByPosition[token.Position] = i;
        }
    }

    public IReadOnlyList<FtToken> Tokens { get; }
    public int[] Sentences { get; }
    public int[] Paragraphs { get; }

    public int LowestPosition => Tokens.Count == 0 ? 0 : Tokens[0].Position;
    public int HighestPosition => Tokens.Count == 0 ? -1 : Tokens[^1].Position;

    public int IndexOf(int position) => _indexByPosition.TryGetValue(position, out var i) ? i : -1;
}
