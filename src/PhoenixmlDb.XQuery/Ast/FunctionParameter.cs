using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Function parameter definition.
/// </summary>
public sealed class FunctionParameter
{
    public required QName Name { get; init; }
    public XdmSequenceType? Type { get; init; }
}
