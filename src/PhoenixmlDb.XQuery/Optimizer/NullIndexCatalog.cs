using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Optimizer;

/// <summary>
/// Null-object implementation that reports no index coverage. The default when
/// no production catalog is wired into <see cref="OptimizationContext"/>.
/// </summary>
public sealed class NullIndexCatalog : IIndexCatalog
{
    public IndexCoverage? LookupValueIndex(ContainerId container, string elementName, string attributeName) => null;
}
