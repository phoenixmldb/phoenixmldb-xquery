using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// A single update primitive in a Pending Update List.
/// </summary>
public abstract class UpdatePrimitive
{
    /// <summary>The target node being modified.</summary>
    public required object Target { get; init; }
}
