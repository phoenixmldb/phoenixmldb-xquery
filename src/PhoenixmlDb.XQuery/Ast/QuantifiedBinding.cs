using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Binding in quantified expression.
/// </summary>
public sealed class QuantifiedBinding
{
    public required QName Variable { get; init; }
    public XdmSequenceType? TypeDeclaration { get; init; }
    public required XQueryExpression Expression { get; init; }

    public override string ToString()
    {
        var type = TypeDeclaration != null ? $" as {TypeDeclaration}" : "";
        return $"${Variable.LocalName}{type} in {Expression}";
    }
}
