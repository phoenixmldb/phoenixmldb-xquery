using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Function parameter definition for function registry.
/// </summary>
public sealed class FunctionParameterDef
{
    public required QName Name { get; init; }
    public required XdmSequenceType Type { get; init; }
}
