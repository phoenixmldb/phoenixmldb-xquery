using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Functions;

namespace PhoenixmlDb.XQuery.Analysis;

/// <summary>
/// Result of static analysis.
/// </summary>
public sealed record AnalysisResult(
    XQueryExpression Expression,
    IReadOnlyList<AnalysisError> Errors)
{
    public bool HasErrors => Errors.Any(e => e.Severity == AnalysisErrorSeverity.Error);
    public bool HasWarnings => Errors.Any(e => e.Severity == AnalysisErrorSeverity.Warning);
}
