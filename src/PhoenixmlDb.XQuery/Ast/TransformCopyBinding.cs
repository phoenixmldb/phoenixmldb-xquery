using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// A single copy binding in a transform expression: $var := expr
/// </summary>
public sealed class TransformCopyBinding
{
    public required QName Variable { get; init; }
    public required XQueryExpression Expression { get; init; }
}

// ═══════════════════════════════════════════════════════════════════════════
// Pending Update List — runtime structures for collecting and applying updates
// ═══════════════════════════════════════════════════════════════════════════
