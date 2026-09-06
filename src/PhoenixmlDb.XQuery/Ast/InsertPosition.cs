using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Position for insert operations.
/// </summary>
public enum InsertPosition
{
    /// <summary>insert node ... into $target (as child, implementation-defined position)</summary>
    Into,
    /// <summary>insert node ... as first into $target</summary>
    AsFirstInto,
    /// <summary>insert node ... as last into $target</summary>
    AsLastInto,
    /// <summary>insert node ... before $target (as preceding sibling)</summary>
    Before,
    /// <summary>insert node ... after $target (as following sibling)</summary>
    After
}
