using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Functions;

namespace PhoenixmlDb.XQuery.Analysis;

/// <summary>
/// Construction mode for element constructors.
/// </summary>
public enum ConstructionMode
{
    Preserve,
    Strip
}
