using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Optimizer;

/// <summary>
/// Provides index configuration information to the query optimizer.
/// Implementations bridge the XQuery engine to an actual indexing subsystem.
/// </summary>
public interface IIndexConfiguration
{
    /// <summary>
    /// Returns true if the specified path should be indexed.
    /// </summary>
    bool ShouldIndexPath(QueryPathStep[] path);

    /// <summary>
    /// Gets the value index type for a path, if configured.
    /// Returns a string type name (e.g. "xs:string", "xs:integer"), or null if no value index exists.
    /// </summary>
    string? GetValueIndexType(QueryPathStep[] path);
}

/// <summary>
/// A single step in a path used for index selection.
/// </summary>
public readonly record struct QueryPathStep
{
    public QueryAxis Axis { get; init; }
    public NamespaceId Namespace { get; init; }
    public string LocalName { get; init; }
    public bool IsWildcard => LocalName == "*";

    public QueryPathStep(QueryAxis axis, NamespaceId ns, string localName)
    {
        Axis = axis;
        Namespace = ns;
        LocalName = localName;
    }
}

/// <summary>
/// XPath axis types for index path analysis.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1028:Enum Storage should be Int32")]
public enum QueryAxis : byte
{
    Child = 0,
    Descendant = 1,
    DescendantOrSelf = 2,
    Attribute = 3,
    Self = 4,
    Parent = 5,
    Ancestor = 6,
    AncestorOrSelf = 7,
    FollowingSibling = 8,
    PrecedingSibling = 9,
    Following = 10,
    Preceding = 11
}
