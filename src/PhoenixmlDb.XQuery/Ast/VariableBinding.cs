using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Variable binding information.
/// </summary>
public sealed class VariableBinding
{
    public required QName Name { get; init; }
    public required XdmSequenceType Type { get; init; }
    public required VariableScope Scope { get; init; }

    /// <summary>
    /// True when this variable was declared with <c>%private</c> in an imported module.
    /// Private variables are accessible within the module but not from importing modules.
    /// </summary>
    public bool IsModulePrivate { get; init; }
}
