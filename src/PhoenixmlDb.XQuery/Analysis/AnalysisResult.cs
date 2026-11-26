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

    /// <summary>The body of the main module is an updating expression (XQUF 3.0 §2.2).</summary>
    internal bool BodyIsUpdating { get; init; }
}
