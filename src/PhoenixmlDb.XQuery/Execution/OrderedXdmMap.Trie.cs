using System.Numerics;
using System.Runtime.CompilerServices;

namespace PhoenixmlDb.XQuery.Execution;

// The two persistent structures behind OrderedXdmMap: a hash array mapped trie for
// lookup by key, and a 32-way vector trie holding the same entries in insertion order.
//
// Both use the same ownership rule. Every node records the edit token of the Trie that
// created it. A Trie may mutate a node in place only when the node carries that Trie's
// CURRENT token; any other node may be shared with another Trie and is copied first.
// Sharing (Trie.Share) replaces the source's token, so from then on neither side owns
// anything the other can see. That is what lets an unshared map be built in place, without
// allocating a new path per insertion, while a shared one is never written.
public sealed partial class OrderedXdmMap
{
    /// <summary>The shareable representation: key lookup plus insertion order.</summary>
    private sealed class Trie
    {
        // Lookup by key.
        private HashNode _root = HashNode.Empty;

        // Insertion order: entries by Entry.Index, the trailing (up to 32) of them in _tail.
        // Removed entries leave null slots, so Length counts slots and Count counts entries.
        private VectorNode _vroot = VectorNode.EmptyRoot;
        private int _vshift = 5;
        private object?[] _tail = [];
        private object? _tailOwner;

        private object _edit = new();

        public int Length { get; private set; }
        public int Count { get; private set; }

        public static Trie FromFlat(Flat flat, Func<object, int> hashOf, IEqualityComparer<object> comparer)
        {
            var trie = new Trie();
            foreach (var key in flat.Order)
                trie.Put(hashOf(key), key, flat[key], comparer);
            return trie;
        }

        /// <summary>
        /// A second Trie over the same structure, in O(1). Both sides give up ownership of
        /// every existing node, so each copies what it later changes.
        /// </summary>
        public Trie Share()
        {
            _edit = new object();
            return new Trie
            {
                _root = _root,
                _vroot = _vroot,
                _vshift = _vshift,
                _tail = _tail,
                Length = Length,
                Count = Count,
            };
        }

        public Entry? Find(int hash, object key, IEqualityComparer<object> comparer)
            => _root.Find(hash, key, comparer);

        public void Put(int hash, object key, object? value, IEqualityComparer<object> comparer)
        {
            Entry? existing = null;
            Entry? updated = null;
            _root = _root.Set(_edit, 0, hash, key, value, Length, comparer, ref existing, ref updated);
            if (existing is null)
            {
                Append(updated!);
                Count++;
            }
            else if (!ReferenceEquals(existing, updated))
            {
                SetOrderSlot(updated!.Index, updated);
            }
        }

        public bool Remove(int hash, object key, IEqualityComparer<object> comparer)
        {
            Entry? removed = null;
            var root = _root.Remove(_edit, 0, hash, key, comparer, ref removed);
            if (removed is null)
                return false;
            _root = root ?? HashNode.Empty;
            SetOrderSlot(removed.Index, null);
            Count--;
            // Reclaim the dead order slots once they outnumber the live ones, so iteration and
            // memory stay proportional to the entries a map actually holds.
            if (Length - Count > Math.Max(32, Count))
                Compact(comparer);
            return true;
        }

        /// <summary>Live entries in insertion order.</summary>
        public IEnumerable<Entry> Entries()
        {
            // Snapshot the fields: a shared Trie's structure never changes under us, and an
            // unshared one being modified is caught by the map enumerator's version check.
            var root = _vroot;
            var shift = _vshift;
            var tail = _tail;
            var length = Length;
            var tailOffset = TailOffsetOf(length);
            for (var block = 0; block < tailOffset; block += 32)
            {
                var node = root;
                for (var level = shift; level > 0; level -= 5)
                    node = (VectorNode)node.Items[(block >>> level) & 31]!;
                foreach (var slot in node.Items)
                    if (slot is Entry e) yield return e;
            }
            for (var i = 0; i < length - tailOffset; i++)
                if (tail[i] is Entry e) yield return e;
        }

        /// <summary>
        /// Rebuilds both structures from the live entries, renumbering them densely. O(n), and
        /// only reached once removals have left more dead order slots than live ones, so it is
        /// amortised over at least as many removals as it has entries to move.
        /// </summary>
        private void Compact(IEqualityComparer<object> comparer)
        {
            var live = new List<Entry>(Count);
            live.AddRange(Entries());
            _root = HashNode.Empty;
            _vroot = VectorNode.EmptyRoot;
            _vshift = 5;
            _tail = [];
            _tailOwner = null;
            Length = 0;
            Count = 0;
            foreach (var e in live)
                Put(e.Hash, e.Key, e.Value, comparer);
        }

        // --- Order vector (a persistent vector with a tail, after Clojure's) ---

        private static int TailOffsetOf(int length) => length < 32 ? 0 : ((length - 1) >>> 5) << 5;

        private void Append(Entry entry)
        {
            var tailCount = Length - TailOffsetOf(Length);
            if (tailCount < 32)
            {
                if (!ReferenceEquals(_tailOwner, _edit) || tailCount == _tail.Length)
                {
                    // Grow a small map's tail gradually; a map past one leaf goes straight to 32.
                    var capacity = tailCount < _tail.Length ? _tail.Length
                        : Length >= 32 ? 32
                        : Math.Min(32, Math.Max(4, tailCount * 2));
                    var grown = new object?[capacity];
                    Array.Copy(_tail, grown, tailCount);
                    _tail = grown;
                    _tailOwner = _edit;
                }
                _tail[tailCount] = entry;
            }
            else
            {
                // Tail is full: it becomes a leaf of the trie. A tail this Trie does not own may
                // be another Trie's tail too, so the leaf made from it must not be ours to edit.
                var leaf = new VectorNode(ReferenceEquals(_tailOwner, _edit) ? _edit : null, _tail);
                if ((Length >>> 5) > (1 << _vshift))
                {
                    var items = new object?[32];
                    items[0] = _vroot;
                    items[1] = NewPath(_vshift, leaf);
                    _vroot = new VectorNode(_edit, items);
                    _vshift += 5;
                }
                else
                {
                    _vroot = PushTail(_vshift, _vroot, leaf);
                }
                _tail = new object?[32];
                _tail[0] = entry;
                _tailOwner = _edit;
            }
            Length++;
        }

        private VectorNode EditableVector(VectorNode node)
            => ReferenceEquals(node.Owner, _edit) ? node : new VectorNode(_edit, (object?[])node.Items.Clone());

        private VectorNode PushTail(int level, VectorNode parent, VectorNode leaf)
        {
            var sub = ((Length - 1) >>> level) & 31;
            var result = EditableVector(parent);
            object toInsert;
            if (level == 5)
                toInsert = leaf;
            else
                toInsert = parent.Items[sub] is VectorNode child
                    ? PushTail(level - 5, child, leaf)
                    : NewPath(level - 5, leaf);
            result.Items[sub] = toInsert;
            return result;
        }

        private VectorNode NewPath(int level, VectorNode node)
        {
            if (level == 0) return node;
            var items = new object?[32];
            items[0] = NewPath(level - 5, node);
            return new VectorNode(_edit, items);
        }

        private void SetOrderSlot(int index, Entry? entry)
        {
            var tailOffset = TailOffsetOf(Length);
            if (index >= tailOffset)
            {
                if (!ReferenceEquals(_tailOwner, _edit))
                {
                    _tail = (object?[])_tail.Clone();
                    _tailOwner = _edit;
                }
                _tail[index - tailOffset] = entry;
                return;
            }
            _vroot = AssocSlot(_vshift, _vroot, index, entry);
        }

        private VectorNode AssocSlot(int level, VectorNode node, int index, Entry? entry)
        {
            var result = EditableVector(node);
            if (level == 0)
            {
                result.Items[index & 31] = entry;
            }
            else
            {
                var sub = (index >>> level) & 31;
                result.Items[sub] = AssocSlot(level - 5, (VectorNode)node.Items[sub]!, index, entry);
            }
            return result;
        }
    }

    /// <summary>
    /// One key/value pair. Immutable: an update creates a new entry, because the old one
    /// may still be reachable from another map. <see cref="Index"/> is the entry's slot in
    /// the order vector, and is what makes an updated key keep its position.
    /// </summary>
    private sealed class Entry(object key, object? value, int hash, int index)
    {
        public readonly object Key = key;
        public readonly object? Value = value;
        public readonly int Hash = hash;
        public readonly int Index = index;
    }

    /// <summary>
    /// Hash trie node. The first <c>PopCount(Bitmap)</c> elements of <see cref="Slots"/> hold
    /// one item per set bit, each an <see cref="Entry"/>, a child node, or a
    /// <see cref="Collision"/> bucket. An owned node may carry spare capacity beyond that, so
    /// that building a map in place does not reallocate a node's array on every insertion;
    /// a copied node is trimmed to its contents.
    /// </summary>
    private sealed class HashNode(object? owner, uint bitmap, object[] slots)
    {
        public readonly object? Owner = owner;
        public uint Bitmap = bitmap;
        public object[] Slots = slots;

        public static readonly HashNode Empty = new(null, 0, []);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint Bit(int hash, int shift) => 1u << ((hash >>> shift) & 31);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int SlotIndex(uint bit) => BitOperations.PopCount(Bitmap & (bit - 1));

        public Entry? Find(int hash, object key, IEqualityComparer<object> comparer)
        {
            var node = this;
            var shift = 0;
            while (true)
            {
                var bit = Bit(hash, shift);
                if ((node.Bitmap & bit) == 0) return null;
                var slot = node.Slots[node.SlotIndex(bit)];
                if (slot is Entry e)
                    return e.Hash == hash && comparer.Equals(e.Key, key) ? e : null;
                if (slot is HashNode child)
                {
                    node = child;
                    shift += 5;
                    continue;
                }
                return ((Collision)slot).Find(key, comparer);
            }
        }

        private int Count => BitOperations.PopCount(Bitmap);

        private HashNode Editable(object edit)
            => ReferenceEquals(Owner, edit) ? this : new HashNode(edit, Bitmap, Slots.AsSpan(0, Count).ToArray());

        /// <summary>
        /// Returns this subtree with <paramref name="key"/> bound to <paramref name="value"/>.
        /// If the key was already present (under the comparer), <paramref name="existing"/>
        /// receives the old entry, and the new entry keeps its key object and index; otherwise a
        /// new entry is created at <paramref name="newIndex"/>. <paramref name="updated"/>
        /// receives whichever entry now holds the binding.
        /// </summary>
        public HashNode Set(object edit, int shift, int hash, object key, object? value, int newIndex,
            IEqualityComparer<object> comparer, ref Entry? existing, ref Entry? updated)
        {
            var bit = Bit(hash, shift);
            var idx = SlotIndex(bit);
            if ((Bitmap & bit) == 0)
            {
                var entry = updated = new Entry(key, value, hash, newIndex);
                var count = Count;
                if (ReferenceEquals(Owner, edit))
                {
                    if (count == Slots.Length)
                    {
                        var larger = new object[Math.Min(32, Math.Max(4, count * 2))];
                        Array.Copy(Slots, larger, count);
                        Slots = larger;
                    }
                    Array.Copy(Slots, idx, Slots, idx + 1, count - idx);
                    Slots[idx] = entry;
                    Bitmap |= bit;
                    return this;
                }
                var grown = new object[count + 1];
                Array.Copy(Slots, 0, grown, 0, idx);
                grown[idx] = entry;
                Array.Copy(Slots, idx, grown, idx + 1, count - idx);
                return new HashNode(edit, Bitmap | bit, grown);
            }

            var slot = Slots[idx];
            object replacement;
            if (slot is Entry e)
            {
                if (e.Hash == hash && comparer.Equals(e.Key, key))
                {
                    existing = e;
                    if (ReferenceEquals(e.Value, value))
                    {
                        updated = e;
                        return this;
                    }
                    // An existing key keeps its ORIGINAL key object and its position.
                    replacement = updated = new Entry(e.Key, value, hash, e.Index);
                }
                else
                {
                    var entry = updated = new Entry(key, value, hash, newIndex);
                    replacement = Split(edit, shift + 5, e, e.Hash, entry);
                }
            }
            else if (slot is HashNode child)
            {
                var r = child.Set(edit, shift + 5, hash, key, value, newIndex, comparer, ref existing, ref updated);
                if (ReferenceEquals(r, child)) return this;
                replacement = r;
            }
            else
            {
                var bucket = (Collision)slot;
                if (bucket.Hash == hash)
                {
                    var r = bucket.Set(key, value, newIndex, comparer, ref existing, ref updated);
                    if (ReferenceEquals(r, bucket)) return this;
                    replacement = r;
                }
                else
                {
                    var entry = updated = new Entry(key, value, hash, newIndex);
                    replacement = Split(edit, shift + 5, bucket, bucket.Hash, entry);
                }
            }

            var ed = Editable(edit);
            ed.Slots[idx] = replacement;
            return ed;
        }

        /// <summary>
        /// The smallest subtree holding <paramref name="existing"/> (an entry or collision
        /// bucket whose hash is <paramref name="existingHash"/>) and <paramref name="added"/>.
        /// </summary>
        private static object Split(object edit, int shift, object existing, int existingHash, Entry added)
        {
            if (existingHash == added.Hash)
            {
                // Identical 32-bit hashes cannot be separated by descending further.
                // (Set routes a same-hash key into an existing bucket, so `existing` is an entry.)
                return new Collision(existingHash, [(Entry)existing, added]);
            }
            var bitA = Bit(existingHash, shift);
            var bitB = Bit(added.Hash, shift);
            if (bitA == bitB)
                return new HashNode(edit, bitA, [Split(edit, shift + 5, existing, existingHash, added)]);
            return bitA < bitB
                ? new HashNode(edit, bitA | bitB, [existing, added])
                : new HashNode(edit, bitA | bitB, [added, existing]);
        }

        /// <summary>
        /// Returns this subtree without <paramref name="key"/>, or null if it became empty.
        /// Returns this same instance when the key is absent.
        /// </summary>
        public HashNode? Remove(object edit, int shift, int hash, object key,
            IEqualityComparer<object> comparer, ref Entry? removed)
        {
            var bit = Bit(hash, shift);
            if ((Bitmap & bit) == 0) return this;
            var idx = SlotIndex(bit);
            var slot = Slots[idx];
            object? replacement;
            if (slot is Entry e)
            {
                if (e.Hash != hash || !comparer.Equals(e.Key, key)) return this;
                removed = e;
                replacement = null;
            }
            else if (slot is HashNode child)
            {
                var r = child.Remove(edit, shift + 5, hash, key, comparer, ref removed);
                if (ReferenceEquals(r, child)) return this;
                // Hoist a lone entry so the trie is no deeper than its contents need.
                replacement = r is not null && r.Count == 1 && r.Slots[0] is Entry lone ? lone : r;
            }
            else
            {
                var bucket = (Collision)slot;
                if (bucket.Hash != hash) return this;
                var r = bucket.Remove(key, comparer, ref removed);
                if (ReferenceEquals(r, bucket)) return this;
                replacement = r;
            }

            if (replacement is not null)
            {
                var ed = Editable(edit);
                ed.Slots[idx] = replacement;
                return ed;
            }
            var count = Count;
            if (count == 1) return null;
            if (ReferenceEquals(Owner, edit))
            {
                Array.Copy(Slots, idx + 1, Slots, idx, count - idx - 1);
                Slots[count - 1] = null!;
                Bitmap &= ~bit;
                return this;
            }
            var shrunk = new object[count - 1];
            Array.Copy(Slots, 0, shrunk, 0, idx);
            Array.Copy(Slots, idx + 1, shrunk, idx, count - idx - 1);
            return new HashNode(edit, Bitmap & ~bit, shrunk);
        }
    }

    /// <summary>Entries whose full 32-bit hashes are equal. Never mutated in place.</summary>
    private sealed class Collision(int hash, Entry[] entries)
    {
        public readonly int Hash = hash;
        private readonly Entry[] _entries = entries;

        public Entry? Find(object key, IEqualityComparer<object> comparer)
        {
            foreach (var e in _entries)
                if (comparer.Equals(e.Key, key)) return e;
            return null;
        }

        public Collision Set(object key, object? value, int newIndex, IEqualityComparer<object> comparer,
            ref Entry? existing, ref Entry? updated)
        {
            for (var i = 0; i < _entries.Length; i++)
            {
                var e = _entries[i];
                if (!comparer.Equals(e.Key, key)) continue;
                existing = e;
                if (ReferenceEquals(e.Value, value))
                {
                    updated = e;
                    return this;
                }
                var copy = (Entry[])_entries.Clone();
                copy[i] = updated = new Entry(e.Key, value, Hash, e.Index);
                return new Collision(Hash, copy);
            }
            var grown = new Entry[_entries.Length + 1];
            Array.Copy(_entries, grown, _entries.Length);
            grown[^1] = updated = new Entry(key, value, Hash, newIndex);
            return new Collision(Hash, grown);
        }

        /// <summary>The bucket without the key; a single survivor comes back as a bare entry.</summary>
        public object Remove(object key, IEqualityComparer<object> comparer, ref Entry? removed)
        {
            for (var i = 0; i < _entries.Length; i++)
            {
                if (!comparer.Equals(_entries[i].Key, key)) continue;
                removed = _entries[i];
                if (_entries.Length == 2) return _entries[1 - i];
                var shrunk = new Entry[_entries.Length - 1];
                Array.Copy(_entries, 0, shrunk, 0, i);
                Array.Copy(_entries, i + 1, shrunk, i, _entries.Length - i - 1);
                return new Collision(Hash, shrunk);
            }
            return this;
        }
    }

    /// <summary>
    /// Node of the order vector: 32 children in an interior node, 32 entries in a leaf.
    /// A removed entry leaves a null slot; <c>Trie.Compact</c> reclaims them in bulk.
    /// </summary>
    private sealed class VectorNode(object? owner, object?[] items)
    {
        public readonly object? Owner = owner;
        public readonly object?[] Items = items;

        public static readonly VectorNode EmptyRoot = new(null, new object?[32]);
    }
}
