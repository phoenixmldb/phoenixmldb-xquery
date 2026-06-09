using System.Collections;

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
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1710:Identifiers should have correct suffix",
    Justification = "Domain type: an XDM map per XPath 4.0, not a general-purpose Dictionary/Collection. The 'Map' suffix matches XDM/XPath terminology.")]
public sealed class OrderedXdmMap : IDictionary<object, object?>, IReadOnlyDictionary<object, object?>, IDictionary
{
    private readonly Dictionary<object, object?> _map;
    private readonly List<object> _order;
    private readonly IEqualityComparer<object> _comparer;

    public OrderedXdmMap()
        : this(XdmMapKeyComparer.Instance) { }

    public OrderedXdmMap(IEqualityComparer<object> comparer)
    {
        _comparer = comparer ?? EqualityComparer<object>.Default;
        _map = new Dictionary<object, object?>(_comparer);
        _order = [];
    }

    /// <summary>
    /// Copy constructor. Enumerates <paramref name="source"/> in its own iteration
    /// order and re-inserts, so an ordered source stays ordered and a legacy
    /// <see cref="Dictionary{TKey,TValue}"/> source keeps its (insertion) order.
    /// </summary>
    public OrderedXdmMap(IEnumerable<KeyValuePair<object, object?>> source, IEqualityComparer<object> comparer)
        : this(comparer)
    {
        ArgumentNullException.ThrowIfNull(source);
        foreach (var kvp in source)
            this[kvp.Key] = kvp.Value;
    }

    public object? this[object key]
    {
        get => _map[key];
        set
        {
            // New key → append to order. Existing key → value-only update, position kept.
            if (!_map.ContainsKey(key))
                _order.Add(key);
            _map[key] = value;
        }
    }

    public ICollection<object> Keys => _order.ToList();
    public ICollection<object?> Values
    {
        get
        {
            var vals = new List<object?>(_order.Count);
            foreach (var k in _order) vals.Add(_map[k]);
            return vals;
        }
    }

    IEnumerable<object> IReadOnlyDictionary<object, object?>.Keys => _order;
    IEnumerable<object?> IReadOnlyDictionary<object, object?>.Values
    {
        get { foreach (var k in _order) yield return _map[k]; }
    }

    public int Count => _map.Count;
    public bool IsReadOnly => false;

    public void Add(object key, object? value)
    {
        _map.Add(key, value); // throws on duplicate (comparer-based), matching Dictionary
        _order.Add(key);
    }

    public void Add(KeyValuePair<object, object?> item) => Add(item.Key, item.Value);

    public void Clear()
    {
        _map.Clear();
        _order.Clear();
    }

    public bool Contains(KeyValuePair<object, object?> item)
        => _map.TryGetValue(item.Key, out var v) && EqualityComparer<object?>.Default.Equals(v, item.Value);

    public bool ContainsKey(object key) => _map.ContainsKey(key);

    public void CopyTo(KeyValuePair<object, object?>[] array, int arrayIndex)
    {
        foreach (var k in _order)
            array[arrayIndex++] = new KeyValuePair<object, object?>(k, _map[k]);
    }

    public IEnumerator<KeyValuePair<object, object?>> GetEnumerator()
    {
        foreach (var k in _order)
            yield return new KeyValuePair<object, object?>(k, _map[k]);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Remove(object key)
    {
        if (!_map.Remove(key))
            return false;
        // _map uses the comparer; mirror it when pruning the order list so a
        // cross-type-equal key (e.g. int vs long) is removed from order too.
        for (var i = 0; i < _order.Count; i++)
        {
            if (_comparer.Equals(_order[i], key))
            {
                _order.RemoveAt(i);
                break;
            }
        }
        return true;
    }

    public bool Remove(KeyValuePair<object, object?> item) => Remove(item.Key);

    public bool TryGetValue(object key, out object? value) => _map.TryGetValue(key, out value);

    // --- Non-generic IDictionary ---
    // Plain Dictionary<K,V> implements System.Collections.IDictionary, so consumers
    // (and tests) commonly check `is IDictionary` to confirm "this is a map". Mirror
    // that surface so OrderedXdmMap is a complete drop-in.

    bool IDictionary.IsFixedSize => false;
    bool IDictionary.IsReadOnly => false;
    ICollection IDictionary.Keys => _order.ToList();
    ICollection IDictionary.Values
    {
        get
        {
            var vals = new List<object?>(_order.Count);
            foreach (var k in _order) vals.Add(_map[k]);
            return vals;
        }
    }

    bool ICollection.IsSynchronized => false;
    object ICollection.SyncRoot => this;

    object? IDictionary.this[object key]
    {
        get => key is not null && _map.TryGetValue(key, out var v) ? v : null;
        set => this[key] = value;
    }

    void IDictionary.Add(object key, object? value) => Add(key, value);
    bool IDictionary.Contains(object key) => key is not null && _map.ContainsKey(key);
    void IDictionary.Remove(object key) { if (key is not null) Remove(key); }

    IDictionaryEnumerator IDictionary.GetEnumerator() => new Enumerator(this);

    void ICollection.CopyTo(Array array, int index)
    {
        ArgumentNullException.ThrowIfNull(array);
        foreach (var k in _order)
            array.SetValue(new DictionaryEntry(k, _map[k]), index++);
    }

    private sealed class Enumerator(OrderedXdmMap owner) : IDictionaryEnumerator
    {
        private readonly OrderedXdmMap _owner = owner;
        private int _index = -1;

        public bool MoveNext() => ++_index < _owner._order.Count;
        public void Reset() => _index = -1;

        private object CurrentKey => _owner._order[_index];
        public object Key => CurrentKey;
        public object? Value => _owner._map[CurrentKey];
        public DictionaryEntry Entry => new(CurrentKey, _owner._map[CurrentKey]);
        public object Current => Entry;
    }
}
