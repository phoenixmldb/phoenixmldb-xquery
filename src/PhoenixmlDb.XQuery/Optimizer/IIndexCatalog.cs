using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Optimizer;

/// <summary>
/// Queries the optimizer can make against the indexing layer to discover what
/// indexes cover a given path/predicate shape. Returned values are opaque
/// handles consumed by the runtime index lookup operator.
/// </summary>
public interface IIndexCatalog
{
    /// <summary>
    /// Returns coverage if a value index exists for an attribute predicate against the given
    /// absolute child-axis element path (local names, root-first) + attribute. Returns null when
    /// no index covers the path. The single-step case passes a one-element list.
    /// <para>
    /// The matched <paramref name="predicate"/> (<see cref="IndexEquality"/> or
    /// <see cref="IndexRange"/>) lets the catalog compute a value-specific selectivity (e.g. from
    /// a histogram). A predicate carrying a <see cref="VariableComparand"/> has no plan-time value,
    /// so the catalog falls back to a path-level/placeholder selectivity (or null).
    /// </para>
    /// </summary>
    IndexCoverage? LookupValueIndex(ContainerId container, IReadOnlyList<string> elementPath, string attributeName, IndexPredicate predicate);
}

/// <summary>
/// Describes an index that covers a query shape. Carries the identifier the
/// runtime index lookup uses to dispatch into the indexing layer.
/// </summary>
public sealed record IndexCoverage(string IndexName, double EstimatedSelectivity);
