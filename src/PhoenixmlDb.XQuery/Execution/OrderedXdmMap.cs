using System.Collections;
using System.Runtime.InteropServices;

namespace PhoenixmlDb.XQuery.Execution;

/// <summary>
/// An XDM map with guaranteed entry/insertion-order iteration, per XPath 4.0
/// (XQuery 4.0 / XSLT 4.0). XPath 3.1 left map order unspecified; 4.0 makes it a
/// contract. This type makes the ordering a structural invariant rather than an
/// incidental property of <see cref="Dictionary{TKey,TValue}"/> enumeration
/// (which the BCL documents as undefined).
/// </summary>
/// <remarks>
/// <para>Ordering rules (XPath 4.0 §17.1):</para>
/// <list type="bullet">
///   <item>A newly added key is appended after all existing keys.</item>
///   <item>Assigning a value to an <em>existing</em> key updates the value and
///         keeps the key in its original position.</item>
///   <item>A removed-then-reinserted key is a new insertion — it moves to the end.</item>
/// </list>
/// <para>
/// Implements <see cref="IDictionary{TKey,TValue}"/> so every existing consumer
/// that pattern-matches <c>IDictionary&lt;object, object?&gt;</c> works unchanged.
/// Key equality uses the supplied comparer (normally
/// <see cref="XdmMapKeyComparer.Instance"/>) so cross-type XDM key matches behave
/// as in <c>op:same-key</c>; the original key object is retained on value update,
/// matching <see cref="Dictionary{TKey,TValue}"/> semantics.
/// </para>
/// <para><b>Two representations.</b> XDM maps are immutable values, so <c>map:put</c>,
/// <c>map:remove</c> and friends are "copy, then change one entry" — which is the copy
/// constructor followed by one mutation. When every copy duplicated all n entries, building
/// a map in a loop was quadratic: QT3 <c>same-key-023</c> ran for about 49 minutes.</para>
/// <list type="bullet">
///   <item><b>Flat</b> — a <see cref="Dictionary{TKey,TValue}"/> plus an order list. Every
///         map starts flat, and a map that is built and then only read stays flat, so it
///         costs exactly what it did before.</item>
///   <item><b>Trie</b> — persistent structures (a hash array mapped trie for lookup, a 32-way
///         vector trie for order; see <c>OrderedXdmMap.Trie.cs</c>) that two maps can share.
///         The first time a map larger than <see cref="FlatCopyLimit"/> is copied, it converts
///         (once, O(n)); from then on a copy is O(1) and each map copies only the O(log n)
///         path it changes.</item>
/// </list>
/// <para>The trie is slower than a Dictionary to build and to look up in — measured, at 1M
/// entries, several times slower per operation, mostly GC cost from per-entry objects — which
/// is why a map is not converted until it is shared. This is the same trade Saxon makes
/// (DictionaryMap → HashTrieMap on first update).</para>
/// <para>Not thread-safe for writers, as before. Concurrent READERS are safe, including a
/// reader racing a conversion (the representation is swapped as one reference) and readers
/// of maps that share structure (shared nodes are never written).</para>
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1710:Identifiers should have correct suffix",
    Justification = "Domain type: an XDM map per XPath 4.0, not a general-purpose Dictionary/Collection. The 'Map' suffix matches XDM/XPath terminology.")]
public sealed partial class OrderedXdmMap : IDictionary<object, object?>, IReadOnlyDictionary<object, object?>, IDictionary
{
    /// <summary>
    /// A flat map this small is copied flat rather than converted: copying 32 entries costs
    /// less than converting them, and small maps (records, options) are the common case.
    /// </summary>
    internal const int FlatCopyLimit = 32;

    private readonly IEqualityComparer<object> _comparer;

    // Either a Flat or a Trie. One reference, so a conversion is published atomically.
    private object _rep;
    private int _version;

    public OrderedXdmMap()
        : this(XdmMapKeyComparer.Instance) { }

    public OrderedXdmMap(IEqualityComparer<object> comparer)
    {
        _comparer = comparer ?? EqualityComparer<object>.Default;
        _rep = new Flat(_comparer);
    }

    /// <summary>
    /// Copy constructor. Enumerates <paramref name="source"/> in its own iteration
    /// order and re-inserts, so an ordered source stays ordered and a legacy
    /// <see cref="Dictionary{TKey,TValue}"/> source keeps its (insertion) order.
    /// An <see cref="OrderedXdmMap"/> source with the same comparer is not re-inserted:
    /// a small one is copied flat, and a larger one shares its structure with the copy,
    /// which costs O(1) once the source is in its trie representation.
    /// </summary>
    public OrderedXdmMap(IEnumerable<KeyValuePair<object, object?>> source, IEqualityComparer<object> comparer)
        : this(comparer)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source is OrderedXdmMap other && ReferenceEquals(other._comparer, _comparer))
        {
            var rep = other.Rep;
            if (rep is Flat flat)
            {
                if (flat.Count <= FlatCopyLimit)
                {
                    _rep = new Flat(flat, _comparer);
                    return;
                }
                // Convert the SOURCE, so its later copies share too. Built aside and swapped
                // in whole: a concurrent reader sees either representation, never half of one.
                var converted = Trie.FromFlat(flat, HashOf, _comparer);
                Volatile.Write(ref other._rep, converted);
                rep = converted;
            }
            _rep = ((Trie)rep).Share();
            return;
        }
        foreach (var kvp in source)
            this[kvp.Key] = kvp.Value;
    }

    private object Rep => Volatile.Read(ref _rep);

    /// <summary>The key comparer. <c>MapKeyHelper</c> trusts a miss only under XdmMapKeyComparer.</summary>
    internal IEqualityComparer<object> Comparer => _comparer;

    public object? this[object key]
    {
        get => TryGetValue(key, out var value)
            ? value
            : throw new KeyNotFoundException($"The given key '{key}' was not present in the map.");
        // New key → append to order. Existing key → value-only update, position kept.
        set => Put(key, value);
    }

    public ICollection<object> Keys
    {
        get
        {
            var rep = Rep;
            if (rep is Flat flat)
                return new List<object>(flat.Order);
            var trie = (Trie)rep;
            var keys = new List<object>(trie.Count);
            foreach (var e in trie.Entries()) keys.Add(e.Key);
            return keys;
        }
    }

    public ICollection<object?> Values
    {
        get
        {
            var rep = Rep;
            var vals = new List<object?>(rep is Flat f ? f.Count : ((Trie)rep).Count);
            if (rep is Flat flat)
                foreach (var k in flat.Order) vals.Add(flat[k]);
            else
                foreach (var e in ((Trie)rep).Entries()) vals.Add(e.Value);
            return vals;
        }
    }

    IEnumerable<object> IReadOnlyDictionary<object, object?>.Keys
    {
        get { foreach (var kvp in this) yield return kvp.Key; }
    }

    IEnumerable<object?> IReadOnlyDictionary<object, object?>.Values
    {
        get { foreach (var kvp in this) yield return kvp.Value; }
    }

    public int Count
    {
        get
        {
            var rep = Rep;
            return rep is Flat flat ? flat.Count : ((Trie)rep).Count;
        }
    }
    public bool IsReadOnly => false;

    /// <summary>For tests: whether the map has converted to its shareable representation.</summary>
    internal bool IsTrie => Rep is Trie;

    /// <summary>For tests: trie order slots in use, removed ones included; a drop means compaction ran.</summary>
    internal int OrderSlotCount => Rep is Trie trie ? trie.Length : 0;

    public void Add(object key, object? value)
    {
        if (ContainsKey(key))
            throw new ArgumentException($"An item with the same key has already been added. Key: {key}", nameof(key));
        Put(key, value);
    }

    public void Add(KeyValuePair<object, object?> item) => Add(item.Key, item.Value);

    public void Clear()
    {
        _rep = new Flat(_comparer);
        _version++;
    }

    public bool Contains(KeyValuePair<object, object?> item)
        => TryGetValue(item.Key, out var v) && EqualityComparer<object?>.Default.Equals(v, item.Value);

    public bool ContainsKey(object key) => TryGetValue(key, out _);

    public void CopyTo(KeyValuePair<object, object?>[] array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);
        foreach (var kvp in Pairs())
            array[arrayIndex++] = kvp;
    }

    public IEnumerator<KeyValuePair<object, object?>> GetEnumerator()
    {
        // Written out per representation rather than over Pairs(): a second iterator layer
        // cost 2-5ns per entry, measured, which every map:for-each and serialization pays.
        var version = _version;
        var rep = Rep;
        if (rep is Flat flat)
        {
            foreach (var k in flat.Order)
            {
                if (version != _version) ThrowModified();
                yield return new KeyValuePair<object, object?>(k, flat[k]);
            }
        }
        else
        {
            foreach (var e in ((Trie)rep).Entries())
            {
                if (version != _version) ThrowModified();
                yield return new KeyValuePair<object, object?>(e.Key, e.Value);
            }
        }
    }

    private static void ThrowModified()
        => throw new InvalidOperationException("The map was modified; enumeration operation may not execute.");

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Remove(object key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var rep = Rep;
        var removed = rep is Flat flat
            ? flat.Remove(key, _comparer)
            : ((Trie)rep).Remove(HashOf(key), key, _comparer);
        if (removed) _version++;
        return removed;
    }

    public bool Remove(KeyValuePair<object, object?> item) => Remove(item.Key);

    public bool TryGetValue(object key, out object? value)
    {
        ArgumentNullException.ThrowIfNull(key);
        var rep = Rep;
        if (rep is Flat flat)
            return flat.TryGetValue(key, out value);
        var e = ((Trie)rep).Find(HashOf(key), key, _comparer);
        value = e?.Value;
        return e is not null;
    }

    private void Put(object key, object? value)
    {
        ArgumentNullException.ThrowIfNull(key);
        var rep = Rep;
        if (rep is Flat flat)
            flat.Put(key, value);
        else
            ((Trie)rep).Put(HashOf(key), key, value, _comparer);
        _version++;
    }

    private IEnumerable<KeyValuePair<object, object?>> Pairs()
    {
        var rep = Rep;
        if (rep is Flat flat)
        {
            foreach (var k in flat.Order)
                yield return new KeyValuePair<object, object?>(k, flat[k]);
        }
        else
        {
            foreach (var e in ((Trie)rep).Entries())
                yield return new KeyValuePair<object, object?>(e.Key, e.Value);
        }
    }

    /// <summary>
    /// The comparer's hash, scrambled (murmur3's finaliser). The trie indexes by the LOW bits
    /// first, where a Dictionary reduces modulo a prime and so never cared: XdmMapKeyComparer
    /// hashes every numeric through its xs:double value, and a double holding a small integer
    /// has all-zero low bits, so without this every small integer key shares one root slot and
    /// the trie grows a level per five bits until the keys finally differ.
    /// </summary>
    private int HashOf(object key)
    {
        var h = (uint)_comparer.GetHashCode(key);
        h ^= h >> 16;
        h *= 0x85ebca6b;
        h ^= h >> 13;
        h *= 0xc2b2ae35;
        h ^= h >> 16;
        return (int)h;
    }

    /// <summary>
    /// The representation every map starts in — unchanged from before the trie existed: the
    /// dictionary itself plus an order list. It IS the dictionary rather than holding one, so
    /// a flat map costs no more objects than it did then (measured: map:merge of 1M
    /// single-entry maps paid ~10% for one extra object per map).
    /// </summary>
    private sealed class Flat : Dictionary<object, object?>
    {
        public readonly List<object> Order;

        public Flat(IEqualityComparer<object> comparer)
            : base(comparer)
        {
            Order = [];
        }

        public Flat(Flat source, IEqualityComparer<object> comparer)
            : base(source, comparer)
        {
            Order = new List<object>(source.Order);
        }

        public void Put(object key, object? value)
        {
            // Dictionary keeps the original key object when an existing key is assigned.
            ref var slot = ref CollectionsMarshal.GetValueRefOrAddDefault(this, key, out var exists);
            if (!exists) Order.Add(key);
            slot = value;
        }

        public bool Remove(object key, IEqualityComparer<object> comparer)
        {
            if (!base.Remove(key))
                return false;
            // The dictionary uses the comparer; mirror it when pruning the order list so a
            // cross-type-equal key (e.g. int vs long) is removed from order too.
            for (var i = 0; i < Order.Count; i++)
            {
                if (comparer.Equals(Order[i], key))
                {
                    Order.RemoveAt(i);
                    break;
                }
            }
            return true;
        }
    }

    // --- Non-generic IDictionary ---
    // Plain Dictionary<K,V> implements System.Collections.IDictionary, so consumers
    // (and tests) commonly check `is IDictionary` to confirm "this is a map". Mirror
    // that surface so OrderedXdmMap is a complete drop-in.

    bool IDictionary.IsFixedSize => false;
    bool IDictionary.IsReadOnly => false;
    ICollection IDictionary.Keys => (List<object>)Keys;
    ICollection IDictionary.Values => (List<object?>)Values;

    bool ICollection.IsSynchronized => false;
    object ICollection.SyncRoot => this;

    object? IDictionary.this[object key]
    {
        get => key is not null && TryGetValue(key, out var v) ? v : null;
        set => this[key] = value;
    }

    void IDictionary.Add(object key, object? value) => Add(key, value);
    bool IDictionary.Contains(object key) => key is not null && ContainsKey(key);
    void IDictionary.Remove(object key) { if (key is not null) Remove(key); }

    IDictionaryEnumerator IDictionary.GetEnumerator() => new DictionaryEnumerator(this);

    void ICollection.CopyTo(Array array, int index)
    {
        ArgumentNullException.ThrowIfNull(array);
        foreach (var kvp in Pairs())
            array.SetValue(new DictionaryEntry(kvp.Key, kvp.Value), index++);
    }

    private sealed class DictionaryEnumerator(OrderedXdmMap owner) : IDictionaryEnumerator
    {
        private readonly OrderedXdmMap _owner = owner;
        private IEnumerator<KeyValuePair<object, object?>> _inner = owner.GetEnumerator();

        public bool MoveNext() => _inner.MoveNext();

        public void Reset()
        {
            _inner.Dispose();
            _inner = _owner.GetEnumerator();
        }

        public object Key => _inner.Current.Key;
        public object? Value => _inner.Current.Value;
        public DictionaryEntry Entry => new(_inner.Current.Key, _inner.Current.Value);
        public object Current => Entry;
    }
}
