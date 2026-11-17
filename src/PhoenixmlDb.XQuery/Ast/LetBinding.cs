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
    /// let score $s := expr (Full Text 3.0 [51]): the variable is the relevance of the value,
    /// an xs:double in [0, 1], not the value.
    /// </summary>
    public bool IsScore { get; init; }

    /// <summary>
    /// The expression to bind.
    /// </summary>
    public required XQueryExpression Expression { get; init; }

    public override string ToString()
    {
        var type = TypeDeclaration != null ? $" as {TypeDeclaration}" : "";
        return $"{(IsScore ? "score " : "")}${Variable.LocalName}{type} := {Expression}";
    }
}
