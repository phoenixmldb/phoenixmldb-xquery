using System.Buffers.Binary;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.FullText;

/// <summary>
/// Full-text inverted index stored in LMDB.
///
/// Key format:   [term_utf8][0x00][container_id:4][document_id:8]
/// Value format: [frequency:2][position_count:2][positions:4*count]
///
/// The null byte separator allows prefix scanning by term across all documents.
/// Container ID scoping enables per-container full-text queries.
/// </summary>
public sealed class FullTextIndex
{
    private readonly Core.Storage.IDatabase _db;

    public FullTextIndex(Core.Storage.IDatabase db)
    {
        _db = db;
    }

    /// <summary>
    /// Indexes a document's text content. Call within a write transaction.
    /// Analyzes the text using Lucene.NET, then writes term→posting entries.
    /// </summary>
    public void IndexDocument(
        Core.Storage.IStorageTransaction txn,
        ContainerId container,
        DocumentId document,
        string textContent,
        FullTextAnalysisOptions? options = null)
    {
        var terms = FullTextEngine.Analyze(textContent, options);
        if (terms.Count == 0) return;

        // Group terms by text to compute frequencies and collect positions
        var termGroups = terms
            .GroupBy(t => t.Text)
            .Select(g => new
            {
                Term = g.Key,
                Frequency = (ushort)Math.Min(g.Count(), ushort.MaxValue),
                Positions = g.Select(t => (uint)t.Position).ToArray()
            });

        foreach (var group in termGroups)
        {
            var key = BuildKey(group.Term, container, document);
            var value = BuildValue(group.Frequency, group.Positions);
            txn.Put(_db, key, value);
        }
    }

    /// <summary>
    /// Removes all full-text index entries for a document.
    /// Call before re-indexing or when deleting a document.
    /// </summary>
    public void RemoveDocument(
        Core.Storage.IStorageTransaction txn,
        ContainerId container,
        DocumentId document)
    {
        // We need to scan all entries for this document and delete them.
        // Since keys are term-first, we can't efficiently find all terms for a doc.
        // In practice, we'd maintain a reverse index or re-index from scratch.
        // For now, use cursor scan with document ID matching.
        using var cursor = txn.CreateCursor(_db);
        if (!cursor.MoveFirst()) return;

        var keysToDelete = new List<byte[]>();
        do
        {
            var key = cursor.Current.Key.Span;
            if (key.Length >= 13) // minimum: 1 term byte + 0x00 + 4 container + 8 document
            {
                // Extract container and document from key suffix
                var separatorIdx = key.IndexOf((byte)0x00);
                if (separatorIdx >= 0 && key.Length >= separatorIdx + 13)
                {
                    var keyContainer = new ContainerId(BinaryPrimitives.ReadUInt32BigEndian(key[(separatorIdx + 1)..]));
                    var keyDocument = new DocumentId(BinaryPrimitives.ReadUInt64BigEndian(key[(separatorIdx + 5)..]));

                    if (keyContainer == container && keyDocument == document)
                        keysToDelete.Add(key.ToArray());
                }
            }
        } while (cursor.MoveNext());

        foreach (var key in keysToDelete)
            txn.Delete(_db, key);
    }

    /// <summary>
    /// Searches for documents containing the given term in a container.
    /// Returns posting entries with document IDs, frequencies, and positions.
    /// </summary>
    public List<PostingEntry> SearchTerm(
        Core.Storage.IStorageTransaction txn,
        ContainerId container,
        string term,
        FullTextAnalysisOptions? options = null)
    {
        // Analyze the search term to get the stemmed form
        var analyzedTerms = FullTextEngine.Analyze(term, options);
        if (analyzedTerms.Count == 0) return [];

        var results = new List<PostingEntry>();
        var searchTerm = analyzedTerms[0].Text; // Use first analyzed token

        // Build prefix key: [term_utf8][0x00][container_id:4]
        var termBytes = System.Text.Encoding.UTF8.GetBytes(searchTerm);
        var prefix = new byte[termBytes.Length + 1 + 4];
        termBytes.CopyTo(prefix.AsSpan());
        prefix[termBytes.Length] = 0x00;
        BinaryPrimitives.WriteUInt32BigEndian(prefix.AsSpan(termBytes.Length + 1), container.Value);

        // Prefix scan
        using var cursor = txn.CreateCursor(_db);
        if (!cursor.SetRange(prefix)) return results;

        do
        {
            var key = cursor.Current.Key.Span;
            // Check prefix match
            if (key.Length < prefix.Length || !key[..prefix.Length].SequenceEqual(prefix))
                break;

            // Extract document ID
            var docId = new DocumentId(BinaryPrimitives.ReadUInt64BigEndian(key[(prefix.Length)..]));

            // Parse value
            var value = cursor.Current.Value.Span;
            var frequency = BinaryPrimitives.ReadUInt16BigEndian(value);
            var posCount = BinaryPrimitives.ReadUInt16BigEndian(value[2..]);
            var positions = new uint[posCount];
            for (var i = 0; i < posCount; i++)
                positions[i] = BinaryPrimitives.ReadUInt32BigEndian(value[(4 + i * 4)..]);

            results.Add(new PostingEntry
            {
                DocumentId = docId,
                Frequency = frequency,
                Positions = positions
            });
        } while (cursor.MoveNext());

        return results;
    }

    /// <summary>
    /// Returns the number of documents containing a term (for IDF calculation).
    /// </summary>
    public int GetDocumentFrequency(
        Core.Storage.IStorageTransaction txn,
        ContainerId container,
        string term,
        FullTextAnalysisOptions? options = null)
    {
        return SearchTerm(txn, container, term, options).Count;
    }

    // ─── Key/Value Encoding ──────────────────────────────────────────────────

    private static byte[] BuildKey(string term, ContainerId container, DocumentId document)
    {
        var termBytes = System.Text.Encoding.UTF8.GetBytes(term);
        var key = new byte[termBytes.Length + 1 + 4 + 8]; // term + null + container + document
        termBytes.CopyTo(key.AsSpan());
        key[termBytes.Length] = 0x00; // separator
        BinaryPrimitives.WriteUInt32BigEndian(key.AsSpan(termBytes.Length + 1), container.Value);
        BinaryPrimitives.WriteUInt64BigEndian(key.AsSpan(termBytes.Length + 5), document.Value);
        return key;
    }

    private static byte[] BuildValue(ushort frequency, uint[] positions)
    {
        var value = new byte[4 + positions.Length * 4]; // freq:2 + count:2 + positions
        BinaryPrimitives.WriteUInt16BigEndian(value.AsSpan(0), frequency);
        BinaryPrimitives.WriteUInt16BigEndian(value.AsSpan(2), (ushort)positions.Length);
        for (var i = 0; i < positions.Length; i++)
            BinaryPrimitives.WriteUInt32BigEndian(value.AsSpan(4 + i * 4), positions[i]);
        return value;
    }
}

/// <summary>
/// A single posting list entry — one term in one document.
/// </summary>
public sealed class PostingEntry
{
    /// <summary>The document containing this term.</summary>
    public required DocumentId DocumentId { get; init; }
    /// <summary>How many times the term appears in the document.</summary>
    public required ushort Frequency { get; init; }
    /// <summary>Token positions where the term appears (for phrase queries).</summary>
    public required uint[] Positions { get; init; }
}
