using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery;

/// <summary>
/// Adapter that wraps delegate-based metadata resolution into the <see cref="IMetadataProvider"/> interface.
/// </summary>
/// <remarks>
/// Provides a lightweight way to supply metadata resolution without implementing a full class.
/// Both a single-key resolver and an all-keys resolver must be supplied.
/// </remarks>
public sealed class DelegateMetadataProvider : IMetadataProvider
{
    private readonly Func<DocumentId, string, byte[]?> _resolver;
    private readonly Func<DocumentId, IEnumerable<(string Key, byte[] Value)>> _allResolver;

    public DelegateMetadataProvider(
        Func<DocumentId, string, byte[]?> resolver,
        Func<DocumentId, IEnumerable<(string Key, byte[] Value)>> allResolver)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _allResolver = allResolver ?? throw new ArgumentNullException(nameof(allResolver));
    }

    public byte[]? GetMetadata(DocumentId documentId, string key) => _resolver(documentId, key);

    public IEnumerable<(string Key, byte[] Value)> GetAllMetadata(DocumentId documentId) => _allResolver(documentId);
}
