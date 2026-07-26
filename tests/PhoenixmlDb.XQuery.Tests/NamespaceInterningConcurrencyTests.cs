using System.Collections.Concurrent;
using FluentAssertions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// Adversarial-audit finding P-ns: <see cref="XdmDocumentStore.ResolveNamespace"/> interns namespace
/// URIs into two <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey,TValue}"/> maps and
/// allocates ids from a shared counter. The pre-fix code called <c>Interlocked.Increment</c> but discarded
/// the returned value and re-read the shared field non-atomically, so two threads interning distinct URIs
/// could be handed the SAME <c>NamespaceId</c> — corrupting the forward/reverse maps and serializing the
/// wrong URI. This test hammers the method concurrently and asserts every distinct URI receives a distinct
/// id and both maps round-trip.
/// </summary>
public class NamespaceInterningConcurrencyTests
{
    [Fact]
    public void ResolveNamespace_ConcurrentDistinctUris_AllGetUniqueRoundTrippableIds()
    {
        var store = new XdmDocumentStore();
        const int uriCount = 2_000;
        var uris = Enumerable.Range(0, uriCount)
            .Select(i => $"urn:phoenixml:ns:{i}")
            .ToArray();

        var results = new ConcurrentDictionary<string, NamespaceId>();

        Parallel.ForEach(
            uris,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount * 4 },
            uri => results[uri] = store.ResolveNamespace(uri));

        // (a) Every distinct URI must have been given a distinct id.
        var idsById = results.Values.ToList();
        idsById.Should().OnlyHaveUniqueItems(
            "two distinct namespace URIs must never be interned to the same NamespaceId");

        // (b) Both maps must round-trip: id -> the exact URI it was allocated for.
        foreach (var (uri, id) in results)
        {
            var resolved = store.ResolveNamespaceUri(id);
            resolved.Should().NotBeNull();
            resolved!.ToString().Should().Be(uri,
                "the reverse map must agree with the forward map for id {0}", id);
        }

        // (c) Re-resolving each URI must be stable (idempotent).
        foreach (var (uri, id) in results)
            store.ResolveNamespace(uri).Should().Be(id);
    }
}
