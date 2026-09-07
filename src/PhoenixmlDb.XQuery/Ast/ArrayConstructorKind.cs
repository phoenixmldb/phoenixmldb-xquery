using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Kind of array constructor.
/// </summary>
public enum ArrayConstructorKind
{
    /// <summary>
    /// Square bracket: [ a, b, c ]
    /// </summary>
    Square,

    /// <summary>
    /// Curly brace: array { expr }
    /// </summary>
    Curly
}
