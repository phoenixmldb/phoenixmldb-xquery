using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Scope of a variable.
/// </summary>
public enum VariableScope
{
    /// <summary>
    /// Declared in prolog.
    /// </summary>
    Global,

    /// <summary>
    /// Bound in for clause.
    /// </summary>
    For,

    /// <summary>
    /// Bound in let clause.
    /// </summary>
    Let,

    /// <summary>
    /// Positional variable in for clause.
    /// </summary>
    Positional,

    /// <summary>
    /// Function parameter.
    /// </summary>
    Parameter,

    /// <summary>
    /// Bound in quantified expression.
    /// </summary>
    Quantified,

    /// <summary>
    /// Bound in typeswitch/switch.
    /// </summary>
    TypeswitchVariable
}
