using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Interpolated expression part of a string constructor.
/// </summary>
public sealed class StringConstructorInterpolationPart : StringConstructorPart
{
    public required XQueryExpression Expression { get; init; }
    public override string ToString() => $"`{{{Expression}}}`";
}
