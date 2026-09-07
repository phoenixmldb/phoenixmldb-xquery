using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Validate expression: <c>validate [strict|lax|type T] { expr }</c>.
/// Validates the enclosed node tree against in-scope schema definitions.
/// Requires an <see cref="ISchemaProvider"/> to be registered.
/// </summary>
public sealed class ValidateExpression : XQueryExpression
{
    /// <summary>The validation mode (strict, lax, or type).</summary>
    public required ValidationMode Mode { get; init; }

    /// <summary>
    /// For <c>validate type T</c>, the target type name.
    /// Null for strict and lax modes.
    /// </summary>
    public XdmTypeName? TypeName { get; init; }

    /// <summary>The expression to validate.</summary>
    public required XQueryExpression Expression { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitValidateExpression(this);

    public override string ToString() => Mode switch
    {
        ValidationMode.Lax => $"validate lax {{ {Expression} }}",
        ValidationMode.Type => $"validate type {TypeName} {{ {Expression} }}",
        _ => $"validate {{ {Expression} }}"
    };
}
