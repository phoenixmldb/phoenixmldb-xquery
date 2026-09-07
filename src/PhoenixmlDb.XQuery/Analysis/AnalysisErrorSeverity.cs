using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Functions;

namespace PhoenixmlDb.XQuery.Analysis;

/// <summary>
/// Severity of an analysis error.
/// </summary>
public enum AnalysisErrorSeverity
{
    Warning,
    Error
}
