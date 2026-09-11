using System.Diagnostics;
using System.Numerics;
using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// XdmMapKeyComparer must never call two keys equal and hash them apart: that is a map lookup
/// that silently misses. It did, for BigInteger (every xs:integer cast from text) and for
/// integral decimals above 15 significant digits, and a linear scan in MapKeyHelper then hid
/// the miss at O(n) per lookup.
/// </summary>
[Collection(TimingSensitiveTests.Name)]
public sealed class XdmMapKeyComparerHashTests
{
    private static readonly XdmMapKeyComparer Comparer = XdmMapKeyComparer.Instance;

    /// <summary>
    /// Every numeric CLR type the engine uses, at the values where representations part
    /// company: small integers, 2^53 +/- 1 (double precision), 2^63 (long range), beyond long,
    /// dyadic fractions (exact in both double and decimal), and non-dyadic ones (exact in
    /// decimal only).
    /// </summary>
    private static List<object> Values()
    {
        var integers = new BigInteger[]
        {
            0, 1, -1, 10, 16777218, (BigInteger)1 << 53, ((BigInteger)1 << 53) + 1,
            ((BigInteger)1 << 50) + 1, long.MaxValue, long.MinValue, (BigInteger)long.MaxValue + 1,
            BigInteger.Pow(10, 20), BigInteger.Pow(10, 20) + 1,
        };
        var values = new List<object>();
        foreach (var n in integers)
        {
            values.Add(n);
            if (n >= long.MinValue && n <= long.MaxValue) values.Add((long)n);
            if (n >= int.MinValue && n <= int.MaxValue) values.Add((int)n);
            if (BigInteger.Abs(n) < BigInteger.Pow(10, 28)) values.Add((decimal)n);
            values.Add((double)n);
            values.Add((float)n);
        }
        foreach (var m in new[] { 0.5m, -0.5m, 1.5m, 1.50m, 0.25m, 0.1m, 1.1m, 3.00000095367431640625m, 0.00000095367431640625m })
        {
            values.Add(m);
            values.Add((double)m);
            values.Add((float)m);
        }
        values.Add(-0.0d);
        return values;
    }

    [Fact]
    public void KeysTheComparerCallsEqual_HashEqually()
    {
        var values = Values();
        var equalPairs = 0;
        foreach (var x in values)
        {
            foreach (var y in values)
            {
                if (!Comparer.Equals(x, y)) continue;
                equalPairs++;
                Comparer.GetHashCode(x).Should().Be(Comparer.GetHashCode(y),
                    $"{x} ({x.GetType().Name}) equals {y} ({y.GetType().Name})");
            }
        }
        // The check is only as good as the pairs it reaches: cross-type equal pairs must exist.
        equalPairs.Should().BeGreaterThan(values.Count * 3);
    }

    [Fact]
    public void ABigIntegerKey_IsFoundByTheEqualLong_WithoutAnyFallback()
    {
        // Straight Dictionary lookup, no MapKeyHelper: only the hash can make this work.
        var map = new Dictionary<object, object?>(Comparer) { [new BigInteger(16777218)] = 1 };
        map.ContainsKey(16777218L).Should().BeTrue();
        map.ContainsKey(16777218d).Should().BeTrue();
        map.ContainsKey(16777218m).Should().BeTrue();
    }

    [Fact]
    public void IntegralDecimalBeyondDoublePrecision_IsFoundByTheEqualLong()
    {
        var big = (1L << 50) + 1; // 16 significant digits; (decimal)(double) rounds it
        var map = new Dictionary<object, object?>(Comparer) { [(decimal)big] = "d" };
        map.ContainsKey(big).Should().BeTrue();
    }

    /// <summary>
    /// A missed numeric lookup scanned the whole map. Asserted by scale: 16x the misses on a
    /// 16x larger map must cost nowhere near 256x.
    /// </summary>
    [Fact]
    public async Task MissedNumericLookups_AreNotLinearInTheMapSize()
    {
        static async Task<double> Measure(int n)
        {
            var query = $"let $m := map:merge((1 to {n}) ! map:entry(., .)) " +
                        $"return count((1 to {n}) ! map:contains($m, . + 0.5)[.])";
            var env = new XdmDocumentStore();
            var engine = new QueryEngine(nodeProvider: env, documentResolver: env);
            var compiled = engine.Compile(query);
            compiled.Success.Should().BeTrue();
            var best = double.MaxValue;
            for (var rep = 0; rep < 3; rep++)
            {
                using var ctx = engine.CreateContext();
                var sw = Stopwatch.StartNew();
                var results = new List<object?>();
                await foreach (var item in compiled.ExecutionPlan!.ExecuteAsync(ctx)) results.Add(item);
                best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
                results.Should().Equal(0L);
            }
            return best;
        }

        // 16x the input: linear predicts ~16x, quadratic ~256x. The bound sits a factor of 4 from
        // each, so noise has room either way (4x input with a bound of 8 did not: see
        // TimingSensitiveTests).
        await Measure(1000);
        var small = await Measure(2000);
        var large = await Measure(32000);
        (large / small).Should().BeLessThan(64, $"2,000 misses took {small:F0} ms, 32,000 took {large:F0} ms");
    }
}
