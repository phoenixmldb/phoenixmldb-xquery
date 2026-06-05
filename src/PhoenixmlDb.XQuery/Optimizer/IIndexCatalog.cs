using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Optimizer;

/// <summary>
/// Queries the optimizer can make against the indexing layer to discover what
/// indexes cover a given path/predicate shape. Returned values are opaque
/// handles consumed by the runtime index lookup operator.
/// </summary>
public interface IIndexCatalog
{
    /// <summary>
    /// Returns coverage info if a value index exists for an attribute equality
    /// predicate against the given element + attribute. Returns null when no
    /// index covers the shape.
    /// </summary>
    IndexCoverage? LookupValueIndex(ContainerId container, string elementName, string attributeName);
}

/// <summary>
/// Describes an index that covers a query shape. Carries the identifier the
/// runtime index lookup uses to dispatch into the indexing layer.
/// </summary>
public sealed record IndexCoverage(string IndexName, double EstimatedSelectivity);
