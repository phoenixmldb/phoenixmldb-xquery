using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Functions;

namespace PhoenixmlDb.XQuery.Analysis;

/// <summary>
/// Copy-namespaces mode.
/// </summary>
public enum CopyNamespacesMode
{
    PreserveInherit,
    PreserveNoInherit,
    NoPreserveInherit,
    NoPreserveNoInherit
}
