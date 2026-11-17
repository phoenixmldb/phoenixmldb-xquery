using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Execution.FullTextMatching;

/// <summary>
/// The positions a match covers (Full Text 3.0 §4.1.2): token, sentence and paragraph numbers.
/// A sentence or paragraph number of 0 means the tokenizer does not report it.
/// </summary>
internal readonly record struct FtTokenInfo(int StartPos, int EndPos, int StartSent, int EndSent, int StartPara, int EndPara)
{
    public int Start(FtUnit unit) => unit switch { FtUnit.Sentences => StartSent, FtUnit.Paragraphs => StartPara, _ => StartPos };
    public int End(FtUnit unit) => unit switch { FtUnit.Sentences => EndSent, FtUnit.Paragraphs => EndPara, _ => EndPos };
}

/// <summary>A StringInclude or StringExclude (§4.2.1.1).</summary>
internal readonly record struct FtStringMatch(int QueryPos, bool IsInclude, bool IsContiguous, FtTokenInfo Info)
{
    public FtStringMatch Inverted => this with { IsInclude = !IsInclude };
}

/// <summary>
/// The AllMatches of an FTSelection (§4.2.1): a disjunction of Matches, each a conjunction of
/// StringIncludes and StringExcludes. The operators are the fts:Apply* functions of §4.2.6.
/// </summary>
internal sealed class FtAllMatches
{
    /// <summary>The most Matches one operator may produce; beyond it the evaluation stops with an error.</summary>
    internal const int MatchLimit = 100_000;

    public FtAllMatches(List<FtStringMatch[]> matches, int stokenNum)
    {
        Matches = matches;
        StokenNum = stokenNum;
    }

    public List<FtStringMatch[]> Matches { get; }

    /// <summary>The number of query tokens consumed (the stokenNum attribute), for queryPos numbering.</summary>
    public int StokenNum { get; }

    /// <summary>True when some Match has no StringExclude (§4.3).</summary>
    public bool IsSatisfied => Matches.Exists(m => Array.TrueForAll(m, s => s.IsInclude));

    /// <summary>
    /// The relevance of a satisfied AllMatches, in (0, 1] (§4.4 leaves the measure to the
    /// implementation): the share of the query's tokens that successful Matches include,
    /// weighted by how many successful Matches there are. Covering more of the query ranks
    /// above repeating one of its words.
    /// </summary>
    public double Score()
    {
        var satisfied = Matches.Where(m => Array.TrueForAll(m, s => s.IsInclude)).ToList();
        if (satisfied.Count == 0)
            return 0.0;
        var covered = satisfied.SelectMany(m => m).Select(s => s.QueryPos).Distinct().Count();
        var coverage = StokenNum > 0 ? Math.Min(1.0, (double)covered / StokenNum) : 1.0;
        return (0.5 + 0.5 * coverage) * (satisfied.Count / (satisfied.Count + 1.0));
    }

    private static void Guard(int count)
    {
        if (count > MatchLimit)
            throw new XQueryRuntimeException("FOER0000",
                $"Full-text evaluation exceeded {MatchLimit:N0} matches; narrow the query (fewer occurrences under occurs/ftnot)");
    }

    // §4.2.6.1
    public static FtAllMatches Or(FtAllMatches a, FtAllMatches b)
    {
        var matches = new List<FtStringMatch[]>(a.Matches.Count + b.Matches.Count);
        matches.AddRange(a.Matches);
        matches.AddRange(b.Matches);
        return new FtAllMatches(matches, Math.Max(a.StokenNum, b.StokenNum));
    }

    // §4.2.6.2: the Cartesian product of the Matches.
    public static FtAllMatches And(FtAllMatches a, FtAllMatches b)
    {
        Guard(a.Matches.Count * b.Matches.Count);
        var matches = new List<FtStringMatch[]>(a.Matches.Count * b.Matches.Count);
        foreach (var m1 in a.Matches)
            foreach (var m2 in b.Matches)
                matches.Add([.. m1, .. m2]);
        return new FtAllMatches(matches, Math.Max(a.StokenNum, b.StokenNum));
    }

    // §4.2.6.3: the negation of a DNF, back into DNF.
    public static FtAllMatches UnaryNot(FtAllMatches a)
    {
        var result = new List<FtStringMatch[]> { Array.Empty<FtStringMatch>() };
        foreach (var match in a.Matches)
        {
            if (match.Length == 0)
                return new FtAllMatches([], a.StokenNum);   // ftnot of an always-true match
            Guard(result.Count * match.Length);
            var next = new List<FtStringMatch[]>(result.Count * match.Length);
            foreach (var sm in match)
                foreach (var rest in result)
                    next.Add([sm.Inverted, .. rest]);
            result = next;
        }
        return new FtAllMatches(result, a.StokenNum);
    }

    private static HashSet<int> CoveredIncludePositions(FtStringMatch[] match)
    {
        var set = new HashSet<int>();
        foreach (var sm in match)
            if (sm.IsInclude)
                for (var p = sm.Info.StartPos; p <= sm.Info.EndPos; p++)
                    set.Add(p);
        return set;
    }

    // §4.2.6.4
    public static FtAllMatches MildNot(FtAllMatches a, FtAllMatches b)
    {
        if (a.Matches.Exists(m => m.Any(s => !s.IsInclude)))
            throw new XQueryRuntimeException("FTDY0017", "Invalid expression on the left-hand side of a not-in: it contains ftnot");
        if (b.Matches.Exists(m => m.Any(s => !s.IsInclude)))
            throw new XQueryRuntimeException("FTDY0017", "Invalid expression on the right-hand side of a not-in: it contains ftnot");
        if (!b.Matches.Exists(m => m.Length > 0))
            return a;
        var excluded = b.Matches.Select(CoveredIncludePositions).ToList();
        var kept = a.Matches.Where(m1 =>
        {
            var covered = CoveredIncludePositions(m1);
            return excluded.TrueForAll(set => covered.Any(p => !set.Contains(p)));
        }).ToList();
        return new FtAllMatches(kept, a.StokenNum);
    }

    // §4.2.6.5
    public static FtAllMatches Order(FtAllMatches a)
    {
        static bool InOrder(FtStringMatch x, FtStringMatch y)
            => (x.Info.StartPos <= y.Info.StartPos && x.QueryPos <= y.QueryPos)
               || (x.Info.StartPos >= y.Info.StartPos && x.QueryPos >= y.QueryPos);

        var result = new List<FtStringMatch[]>();
        foreach (var match in a.Matches)
        {
            var includes = match.Where(s => s.IsInclude).ToArray();
            if (!includes.All(x => includes.All(y => InOrder(x, y))))
                continue;
            var excludes = match.Where(s => !s.IsInclude && includes.All(i => InOrder(s, i)));
            result.Add([.. includes, .. excludes]);
        }
        return new FtAllMatches(result, a.StokenNum);
    }

    // §4.2.6.6
    public static FtAllMatches Scope(bool same, FtUnit unit, FtAllMatches a)
    {
        var result = new List<FtStringMatch[]>();
        foreach (var match in a.Matches)
        {
            var includes = match.Where(s => s.IsInclude).ToArray();
            var excludes = match.Where(s => !s.IsInclude).ToArray();
            if (same)
            {
                // Every StringInclude within one sentence (paragraph), and all the same one.
                if (!includes.All(i => i.Info.Start(unit) > 0 && i.Info.Start(unit) == i.Info.End(unit)
                        && i.Info.Start(unit) == includes[0].Info.Start(unit)))
                    continue;
                var kept = excludes.Where(e => e.Info.Start(unit) == 0
                    || (e.Info.Start(unit) == e.Info.End(unit) && includes.All(i => i.Info.Start(unit) == e.Info.Start(unit))));
                result.Add([.. includes, .. kept]);
            }
            else
            {
                if (includes.Length <= 1)
                    continue;
                var differ = true;
                for (var i = 0; i < includes.Length && differ; i++)
                    for (var j = 0; j < includes.Length && differ; j++)
                    {
                        if (i == j) continue;
                        var x = includes[i].Info;
                        var y = includes[j].Info;
                        differ = (x.Start(unit) != y.Start(unit) || x.Start(unit) != x.End(unit) || y.Start(unit) != y.End(unit))
                                 && x.Start(unit) > 0 && y.End(unit) > 0;
                    }
                if (!differ)
                    continue;
                var kept = excludes.Where(e => includes.All(i =>
                    (i.Info.Start(unit) != e.Info.Start(unit) || i.Info.Start(unit) != i.Info.End(unit) || e.Info.Start(unit) != e.Info.End(unit))
                    && i.Info.Start(unit) > 0 && e.Info.End(unit) > 0));
                result.Add([.. includes, .. kept]);
            }
        }
        return new FtAllMatches(result, a.StokenNum);
    }

    // §4.2.6.7
    public static FtAllMatches Content(FtPositionFilterKind kind, int lowest, int highest, FtAllMatches a)
    {
        bool Keep(FtStringMatch[] match) => kind switch
        {
            FtPositionFilterKind.EntireContent => Enumerable.Range(lowest, Math.Max(0, highest - lowest + 1)).All(pos =>
                match.Any(s => s.IsInclude && s.IsContiguous && s.Info.StartPos <= pos && pos <= s.Info.EndPos)),
            FtPositionFilterKind.AtStart => match.Any(s => s.IsInclude && s.Info.StartPos <= lowest && lowest <= s.Info.EndPos),
            _ => match.Any(s => s.IsInclude && s.Info.StartPos <= highest && highest <= s.Info.EndPos),
        };
        return new FtAllMatches(a.Matches.Where(Keep).ToList(), a.StokenNum);
    }

    /// <summary>fts:joinIncludes: the StringIncludes of a Match as one (§4.2.6.8).</summary>
    private static FtStringMatch? JoinIncludes(FtStringMatch[] includes)
    {
        if (includes.Length == 0)
            return null;
        var covered = CoveredIncludePositions(includes);
        var minPos = includes.Min(s => s.Info.StartPos);
        var maxPos = includes.Max(s => s.Info.EndPos);
        var contiguous = Enumerable.Range(minPos, maxPos - minPos + 1).All(covered.Contains) && includes.All(s => s.IsContiguous);
        return new FtStringMatch(includes[0].QueryPos, true, contiguous, new FtTokenInfo(
            minPos, maxPos,
            includes.Min(s => s.Info.StartSent), includes.Max(s => s.Info.EndSent),
            includes.Min(s => s.Info.StartPara), includes.Max(s => s.Info.EndPara)));
    }

    // §4.2.6.8
    public static FtAllMatches Window(FtUnit unit, long size, FtAllMatches a)
    {
        var result = new List<FtStringMatch[]>();
        foreach (var match in a.Matches)
        {
            var includes = match.Where(s => s.IsInclude).ToArray();
            if (includes.Length == 0)
                continue;
            var minPos = includes.Min(s => s.Info.Start(unit));
            var maxPos = includes.Max(s => s.Info.End(unit));
            var joined = JoinIncludes(includes)!.Value;
            for (var windowStart = maxPos - size + 1; windowStart <= minPos; windowStart++)
            {
                var windowEnd = windowStart + size - 1;
                var excludes = match.Where(s => !s.IsInclude && s.Info.Start(unit) >= windowStart && s.Info.End(unit) <= windowEnd);
                result.Add([joined, .. excludes]);
                Guard(result.Count);
            }
        }
        return new FtAllMatches(result, a.StokenNum);
    }

    /// <summary>fts:wordDistance and its sentence and paragraph forms.</summary>
    private static long Distance(FtUnit unit, FtTokenInfo x, FtTokenInfo y)
    {
        var (first, second) = (x.StartPos, x.EndPos).CompareTo((y.StartPos, y.EndPos)) <= 0 ? (x, y) : (y, x);
        return second.Start(unit) - first.End(unit) - 1;
    }

    // §4.2.6.9
    public static FtAllMatches DistanceFilter(FtUnit unit, FtRangeKind kind, long first, long second, FtAllMatches a)
    {
        bool InRange(long d) => kind switch
        {
            FtRangeKind.Exactly => d == first,
            FtRangeKind.AtLeast => d >= first,
            FtRangeKind.AtMost => d <= first,
            _ => d >= first && d <= second,
        };

        var result = new List<FtStringMatch[]>();
        foreach (var match in a.Matches)
        {
            var includes = match.Where(s => s.IsInclude).ToArray();
            var sorted = includes.OrderBy(s => s.Info.Start(unit)).ThenBy(s => s.Info.End(unit)).ToArray();
            var ok = true;
            for (var i = 0; i + 1 < sorted.Length && ok; i++)
                ok = InRange(Distance(unit, sorted[i].Info, sorted[i + 1].Info));
            if (!ok)
                continue;
            var excludes = match.Where(s => !s.IsInclude && includes.Any(i => InRange(Distance(unit, i.Info, s.Info))));
            result.Add(JoinIncludes(includes) is { } joined ? [joined, .. excludes] : [.. excludes]);
        }
        return new FtAllMatches(result, a.StokenNum);
    }

    /// <summary>
    /// FTTimes (§4.2.6.10). The specification builds the Matches as every combination of at least
    /// L occurrences, conjoined with the negation of every combination of at least U + 1. That
    /// is exponential in the number of occurrences, so this builds the equivalent minimal form:
    /// each Match is L occurrences included and, of the others, all but U − L excluded, one
    /// StringInclude of each inverted. A larger combination only adds conditions, so it cannot
    /// make a Match succeed that a minimal one does not; the verdict is the same.
    /// </summary>
    public static FtAllMatches Times(FtRangeKind kind, long first, long second, FtAllMatches a)
    {
        if (a.Matches.Exists(m => m.Any(s => !s.IsInclude)))
            throw new XQueryRuntimeException("XPST0003", "occurs … times applies only to search words");
        var occurrences = a.Matches;
        var n = occurrences.Count;
        var (low, high) = kind switch
        {
            FtRangeKind.Exactly => (first, first),
            FtRangeKind.AtLeast => (first, long.MaxValue),
            FtRangeKind.AtMost => (0L, first),
            _ => (first, second),
        };
        if (low > high || low > n)
            return new FtAllMatches([], a.StokenNum);
        low = Math.Max(0, low);

        var result = new List<FtStringMatch[]>();
        foreach (var included in Combinations(n, (int)low))
        {
            var rest = Enumerable.Range(0, n).Where(i => !included.Contains(i)).ToList();
            var includes = included.SelectMany(i => occurrences[i]).ToArray();
            // At most `high` occurrences in all: of the rest, at least n − high are absent.
            var mustExclude = high >= n ? 0 : (int)(n - high);
            if (mustExclude > rest.Count)
                continue;
            foreach (var excludedSet in Combinations(rest.Count, mustExclude))
            {
                foreach (var excludes in OneEach(excludedSet.Select(i => occurrences[rest[i]]).ToList()))
                {
                    result.Add([.. includes, .. excludes]);
                    Guard(result.Count);
                }
            }
        }
        return new FtAllMatches(result, a.StokenNum);

        // One StringInclude of each excluded occurrence, inverted: the occurrence is absent when any of its parts is.
        static IEnumerable<FtStringMatch[]> OneEach(List<FtStringMatch[]> excluded)
        {
            IEnumerable<FtStringMatch[]> acc = [Array.Empty<FtStringMatch>()];
            foreach (var occurrence in excluded)
            {
                var current = acc;
                acc = current.SelectMany(prefix => occurrence.Select(sm => (FtStringMatch[])[.. prefix, sm.Inverted]));
            }
            return acc;
        }
    }

    private static IEnumerable<int[]> Combinations(int n, int k)
    {
        if (k == 0)
        {
            yield return [];
            yield break;
        }
        if (k > n)
            yield break;
        var indices = Enumerable.Range(0, k).ToArray();
        while (true)
        {
            yield return (int[])indices.Clone();
            var i = k - 1;
            while (i >= 0 && indices[i] == n - k + i) i--;
            if (i < 0) yield break;
            indices[i]++;
            for (var j = i + 1; j < k; j++) indices[j] = indices[j - 1] + 1;
        }
    }
}
