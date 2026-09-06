using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// A single for binding.
/// </summary>
public sealed class ForBinding
{
    /// <summary>
    /// Variable name.
    /// </summary>
    public required QName Variable { get; init; }

    /// <summary>
    /// Optional type declaration.
    /// </summary>
    public XdmSequenceType? TypeDeclaration { get; init; }

    /// <summary>
    /// Whether to allow empty sequences (for $x allowing empty in ...).
    /// </summary>
    public bool AllowingEmpty { get; init; }

    /// <summary>
    /// Optional positional variable (for $x at $i in ...).
    /// </summary>
    public QName? PositionalVariable { get; init; }

    /// <summary>
    /// The expression to iterate over.
    /// </summary>
    public required XQueryExpression Expression { get; init; }

    public override string ToString()
    {
        var type = TypeDeclaration != null ? $" as {TypeDeclaration}" : "";
        var allowing = AllowingEmpty ? " allowing empty" : "";
        var pos = PositionalVariable != null ? $" at ${PositionalVariable.Value.LocalName}" : "";
        return $"${Variable.LocalName}{type}{allowing}{pos} in {Expression}";
    }
}
