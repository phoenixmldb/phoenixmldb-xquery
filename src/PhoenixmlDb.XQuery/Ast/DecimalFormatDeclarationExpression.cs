using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// declare [default] decimal-format [name] property=value ...;
/// </summary>
public sealed class DecimalFormatDeclarationExpression : XQueryExpression
{
    /// <summary>
    /// The format name (null for the default decimal format).
    /// </summary>
    public string? FormatName { get; init; }

    /// <summary>
    /// Property name→value pairs (e.g., "decimal-separator" → ".").
    /// </summary>
    public Dictionary<string, string> Properties { get; init; } = new();

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor) => visitor.VisitDecimalFormatDeclaration(this);
}
