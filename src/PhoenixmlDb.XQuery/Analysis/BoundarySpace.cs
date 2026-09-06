using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Functions;

namespace PhoenixmlDb.XQuery.Analysis;

/// <summary>
/// Boundary space handling.
/// </summary>
public enum BoundarySpace
{
    Preserve,
    Strip
}
