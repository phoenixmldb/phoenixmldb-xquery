using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// A single let binding.
/// </summary>
public sealed class LetBinding
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
    /// The expression to bind.
    /// </summary>
    public required XQueryExpression Expression { get; init; }

    public override string ToString()
    {
        var type = TypeDeclaration != null ? $" as {TypeDeclaration}" : "";
        return $"${Variable.LocalName}{type} := {Expression}";
    }
}
