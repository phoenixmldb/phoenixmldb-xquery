using System.Collections;
using System.Diagnostics;
using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// OrderedXdmMap starts as a flat Dictionary and converts to a persistent trie the first time
/// a map larger than <see cref="OrderedXdmMap.FlatCopyLimit"/> is copied; after that the copy
/// constructor shares structure and each map copies only the path it changes. So the failures
/// to fear are not a wrong answer from a single map, but one map's edit becoming visible
/// through another. Most of these tests fork maps and then mutate both sides, and the tests
/// meant to exercise the trie assert that they reached it.
/// </summary>
[Collection(TimingSensitiveTests.Name)]
public sealed class OrderedXdmMapTests
{
    private static OrderedXdmMap Fork(OrderedXdmMap source) => new(source, XdmMapKeyComparer.Instance);

    private static OrderedXdmMap Build(int n)
    {
        var map = new OrderedXdmMap(XdmMapKeyComparer.Instance);
        for (var i = 0; i < n; i++) map[$"k{i}"] = i;
        return map;
    }

    [Fact]
    public void NewKeysAppend_ExistingKeysKeepPosition_ReinsertedKeysMoveToEnd()
    {
        var map = new OrderedXdmMap { ["a"] = 1, ["b"] = 2, ["c"] = 3 };
        map["a"] = 10;
        map.Keys.Should().Equal("a", "b", "c");
        map.Remove("a");
        map["a"] = 100;
        map.Keys.Should().Equal("b", "c", "a");
        map.Values.Should().Equal(2, 3, 100);
    }

    [Fact]
    public void UpdateRetainsTheOriginalKeyObject()
    {
        // op:same-key matches xs:integer 1 with xs:double 1.0; the stored key stays the first one.
        var map = new OrderedXdmMap { [1L] = "first" };
        map[1.0d] = "second";
        map.Should().HaveCount(1);
        map.Keys.Single().Should().Be(1L);
        map[1L].Should().Be("second");
    }

    [Fact]
    public void AMapThatIsNeverCopied_StaysFlat()
    {
        var map = Build(10_000);
        map.Remove("k5");
        _ = map["k6"];
        map.IsTrie.Should().BeFalse("only sharing converts: a built-and-read map keeps Dictionary performance");
    }

    [Fact]
    public void CopyOfASmallMap_IsFlatOnBothSides_AndIndependent()
    {
        var source = Build(OrderedXdmMap.FlatCopyLimit);
        var copy = Fork(source);
        copy["k1"] = "changed";
        copy["new"] = 0;
        source.IsTrie.Should().BeFalse();
        copy.IsTrie.Should().BeFalse();
        source["k1"].Should().Be(1);
        source.ContainsKey("new").Should().BeFalse();
    }

    [Fact]
    public void CopyOfALargerMap_ConvertsTheSource_SoLaterCopiesShareToo()
    {
        var source = Build(OrderedXdmMap.FlatCopyLimit + 1);
        var copy = Fork(source);
        source.IsTrie.Should().BeTrue();
        copy.IsTrie.Should().BeTrue();
        Fork(source).IsTrie.Should().BeTrue();
        source.Keys.Should().Equal(copy.Keys);
    }

    [Fact]
    public void ConcurrentCopiesOfOneFlatMap_AreAllCorrect()
    {
        // Two queries sharing a constant map may both map:put it at once, and both find it
        // flat: each converts it, one conversion wins, and every copy must still be right.
        for (var round = 0; round < 20; round++)
        {
            var source = Build(2000);
            var copies = new OrderedXdmMap[16];
            Parallel.For(0, copies.Length, i =>
            {
                var c = Fork(source);
                c[$"k{i}"] = $"copy{i}";
                copies[i] = c;
            });
            for (var i = 0; i < copies.Length; i++)
            {
                copies[i][$"k{i}"].Should().Be($"copy{i}");
                copies[i][$"k{(i + 1) % copies.Length}"].Should().Be((i + 1) % copies.Length);
                copies[i].Should().HaveCount(2000);
            }
            source.Values.Should().Equal(Enumerable.Range(0, 2000).Cast<object?>());
        }
    }

    [Fact]
    public void Copy_OfAnOrderedXdmMap_IsIndependentInBothDirections()
    {
        var source = Build(5000);
        var copy = Fork(source);
        copy.IsTrie.Should().BeTrue();

        copy["k10"] = "changed";
        copy.Remove("k20");
        copy["new"] = "added";
        source["k30"] = "source-changed";
        source.Remove("k40");

        source["k10"].Should().Be(10);
        source.ContainsKey("k20").Should().BeTrue();
        source.ContainsKey("new").Should().BeFalse();
        copy["k30"].Should().Be(30);
        copy.ContainsKey("k40").Should().BeTrue();
        source.Should().HaveCount(4999);
        copy.Should().HaveCount(5000);
        copy.Keys.Last().Should().Be("new");
    }

    [Fact]
    public void ManyCopiesOfOneBase_DoNotSeeEachOther()
    {
        // same-key-023's shape: every iteration derives a new map from the SAME base.
        var basis = Build(3000);
        var derived = new List<OrderedXdmMap>();
        for (var i = 0; i < 3000; i += 7)
        {
            var d = Fork(basis);
            d[$"k{i}"] = "x";
            derived.Add(d);
        }
        for (var j = 0; j < derived.Count; j++)
        {
            var i = j * 7;
            derived[j][$"k{i}"].Should().Be("x");
            derived[j][$"k{(i + 1) % 3000}"].Should().Be((i + 1) % 3000);
        }
        basis.Values.Should().Equal(Enumerable.Range(0, 3000).Cast<object?>());
    }

    [Fact]
    public void SharingATailThatIsFullAndSmall_DoesNotLeakAppends()
    {
        // The order vector's tail is shared on copy; appends to a copy that hit the
        // in-place fast path would overwrite the other map's next slot. 63 entries: one full
        // leaf plus a tail with one free slot.
        var basis = Build(63);
        var a = Fork(basis);
        var b = Fork(basis);
        a.IsTrie.Should().BeTrue();
        a["a"] = "a";   // fills the shared tail's last slot
        b["b"] = "b";
        a["a2"] = "a2"; // pushes a's full tail into its trie
        b["b2"] = "b2";
        a.Keys.Skip(63).Should().Equal("a", "a2");
        b.Keys.Skip(63).Should().Equal("b", "b2");
        basis.Should().HaveCount(63);
        basis.Keys.Last().Should().Be("k62");
    }

    [Fact]
    public void Compaction_PreservesOrderAndDoesNotDisturbCopies()
    {
        var map = Build(1000);
        var snapshot = Fork(map);
        map.IsTrie.Should().BeTrue();
        for (var i = 0; i < 1000; i += 3) map.Remove($"k{i}");
        for (var i = 0; i < 1000; i += 3) map.Remove($"k{i + 1}"); // passes the compaction threshold
        map.OrderSlotCount.Should().BeLessThan(1000, "667 of 1000 slots are dead, so the map must have compacted");
        map["tail"] = "t";

        var expected = Enumerable.Range(0, 1000).Where(i => i % 3 == 2).Select(i => (object)$"k{i}")
            .Append("tail");
        map.Keys.Should().Equal(expected);
        map["k2"].Should().Be(2);
        snapshot.Should().HaveCount(1000);
        snapshot.Keys.Should().Equal(Enumerable.Range(0, 1000).Select(i => (object)$"k{i}"));
    }

    [Fact]
    public void FullHashCollisions_AreStoredLookedUpAndRemoved()
    {
        var comparer = new ConstantHashComparer();
        var map = new OrderedXdmMap(comparer);
        for (var i = 0; i < 50; i++) map[i] = i;
        var copy = new OrderedXdmMap(map, comparer);
        copy.IsTrie.Should().BeTrue();
        copy.Remove(25);
        copy[7] = "seven";

        map.Should().HaveCount(50);
        map[25].Should().Be(25);
        map[7].Should().Be(7);
        copy.ContainsKey(25).Should().BeFalse();
        copy[7].Should().Be("seven");
        copy.Keys.Should().Equal(Enumerable.Range(0, 50).Where(i => i != 25).Cast<object>());
    }

    [Fact]
    public void ModifyingDuringEnumeration_Throws_AsDictionaryDoes()
    {
        var map = Build(10);
        var act = () => { foreach (var kv in map) map[kv.Key + "!"] = 0; };
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void NonGenericDictionarySurface_Works()
    {
        IDictionary map = new OrderedXdmMap { ["a"] = 1, ["b"] = 2 };
        map.Contains("a").Should().BeTrue();
        map["missing"].Should().BeNull();
        var keys = new List<object>();
        foreach (DictionaryEntry e in map) keys.Add(e.Key);
        keys.Should().Equal("a", "b");
        map.Keys.Count.Should().Be(2);
    }

    [Fact]
    public void AddOfExistingKey_ThrowsAndLeavesTheMapUnchanged()
    {
        var map = new OrderedXdmMap { ["a"] = 1 };
        var act = () => map.Add("a", 2);
        act.Should().Throw<ArgumentException>();
        map["a"].Should().Be(1);
        map.Should().HaveCount(1);
    }

    /// <summary>
    /// Model-based: random puts, removes and forks, every live map checked against a naive
    /// list every 97 steps. Keys mix xs:integer, xs:double, xs:decimal and xs:string, so
    /// op:same-key's cross-type matches are exercised, and the retained key object is checked.
    /// </summary>
    /// <remarks>
    /// A random test that never reaches the interesting states passes just as green as one
    /// that does, so the run asserts it got there: maps larger than the vector's first level
    /// (1056 slots), compactions, and edits made after a fork.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5394:Do not use insecure randomness",
        Justification = "A seeded Random is the point: each failing seed must replay exactly.")]
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void RandomOperations_MatchANaiveModel_AcrossForks(int seed)
    {
        var rng = new Random(seed);
        var comparer = XdmMapKeyComparer.Instance;
        var maps = new List<(OrderedXdmMap Map, List<ModelEntry> Model)>
        {
            (new OrderedXdmMap(comparer), []),
        };
        int maxSlots = 0, compactions = 0, forks = 0, trieForks = 0, flatForks = 0, clears = 0, editsAfterFork = 0;

        for (var step = 0; step < 80_000; step++)
        {
            var which = rng.Next(maps.Count);
            var (map, model) = maps[which];
            var slotsBefore = map.OrderSlotCount;
            // Alternate long growing and shrinking phases, over few long-lived maps: a steady
            // mix settles at an equilibrium size and never accumulates enough dead slots to
            // compact. (Measured: with a fixed 55/40 mix over 8 maps, zero compactions.)
            var putShare = step / 6000 % 2 == 0 ? 75 : 20;
            var op = rng.Next(100);
            if (step is 3 or 11 or 25)
            {
                op = 99; // fork while the maps are still small, so flat copies are covered too
            }
            else if (which != 0 && rng.Next(1000) == 0)
            {
                // Back to empty and flat; it will grow and convert again. Never map 0: one
                // long-lived trie is what accumulates enough removals to compact.
                map.Clear();
                model.Clear();
                clears++;
                continue;
            }
            if (op < 98)
            {
                // Values 0..1499 as integer, double, decimal or string: the three numeric
                // spellings of one value are the SAME key under op:same-key.
                var n = rng.Next(1500);
                object key = rng.Next(4) switch
                {
                    0 => (long)n,
                    1 => (double)n,
                    2 => (decimal)n,
                    _ => $"s{n}",
                };
                var id = key is string ? $"s{n}" : $"n{n}";
                var at = model.FindIndex(e => e.Id == id);
                if (op < putShare)
                {
                    map[key] = step;
                    if (at >= 0) model[at] = model[at] with { Value = step };
                    else model.Add(new ModelEntry(id, key, step));
                }
                else
                {
                    Assert.Equal(at >= 0, map.Remove(key));
                    if (at >= 0) model.RemoveAt(at);
                }
                if (forks > 0) editsAfterFork++;
            }
            else if (maps.Count < 4 || op == 99 && maps.Count < 6)
            {
                var fork = new OrderedXdmMap(map, comparer);
                maps.Add((fork, [.. model]));
                forks++;
                if (fork.IsTrie) trieForks++; else flatForks++;
            }
            else
            {
                maps.RemoveAt(which == 0 ? 1 : which);
            }

            if (map.OrderSlotCount < slotsBefore) compactions++;
            maxSlots = Math.Max(maxSlots, map.OrderSlotCount);

            if (step % 97 == 0 || step == 79_999)
            {
                foreach (var (m, expected) in maps)
                {
                    Assert.Equal(expected.Count, m.Count);
                    var i = 0;
                    foreach (var kv in m)
                    {
                        // Same position, same retained key OBJECT (type included), same value.
                        Assert.Equal(expected[i].Key, kv.Key);
                        Assert.Equal(expected[i].Key.GetType(), kv.Key.GetType());
                        Assert.Equal(expected[i].Value, kv.Value);
                        i++;
                    }
                    Assert.Equal(expected.Count, i);
                    foreach (var e in expected)
                    {
                        Assert.True(m.TryGetValue(e.Key, out var v));
                        Assert.Equal(e.Value, v);
                    }
                }
            }
        }

        maxSlots.Should().BeGreaterThan(1056, "the order vector must grow past its first trie level");
        var counters = $"slots={maxSlots} compactions={compactions} forks={forks} trieForks={trieForks} flatForks={flatForks} clears={clears}";
        compactions.Should().BeGreaterThanOrEqualTo(4, "removal must have triggered compaction repeatedly (measured 5-8 per seed); " + counters);
        forks.Should().BeGreaterThan(600);
        trieForks.Should().BeGreaterThan(500, "most forks happen on maps large enough to share");
        flatForks.Should().BeGreaterThan(20, "the first forks happen while the maps are still small");
        clears.Should().BeGreaterThan(30);
        editsAfterFork.Should().BeGreaterThan(60_000);
    }

    private sealed record ModelEntry(string Id, object Key, object? Value);

    /// <summary>
    /// The defect this type was rewritten for: every map:put copied the whole map, so N puts
    /// derived from an N-entry map cost O(N²). Asserted by SCALE rather than by wall clock
    /// against a fixed budget: 16x the input must not come close to 256x the time.
    /// </summary>
    [Fact]
    public void CopyThenPut_FromOneLargeBase_IsNotQuadratic()
    {
        static double Measure(int n)
        {
            var basis = Build(n);
            var best = double.MaxValue;
            for (var rep = 0; rep < 3; rep++)
            {
                var sw = Stopwatch.StartNew();
                for (var i = 0; i < n; i++)
                {
                    var copy = new OrderedXdmMap(basis, XdmMapKeyComparer.Instance);
                    copy[$"k{i}"] = "x";
                }
                best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
            }
            return best;
        }

        Measure(20_000); // warm up
        var small = Measure(20_000);
        var large = Measure(320_000);
        // 16x the input. n log n predicts ~20x; the old quadratic copy predicts 256x. A 4x input
        // with a bound of 9 left too little room for noise on a shared CI runner.
        (large / small).Should().BeLessThan(64,
            $"20k puts took {small:F1} ms and 320k took {large:F1} ms");
    }

    private sealed class ConstantHashComparer : IEqualityComparer<object>
    {
        public new bool Equals(object? x, object? y) => object.Equals(x, y);
        public int GetHashCode(object obj) => 42;
    }
}
