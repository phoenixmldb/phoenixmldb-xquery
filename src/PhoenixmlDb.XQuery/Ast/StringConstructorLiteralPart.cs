using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Literal text part of a string constructor.
/// </summary>
public sealed class StringConstructorLiteralPart : StringConstructorPart
{
    public required string Value { get; init; }
    public override string ToString() => Value;
}
