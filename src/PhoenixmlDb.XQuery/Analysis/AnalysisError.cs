using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Functions;

namespace PhoenixmlDb.XQuery.Analysis;

/// <summary>
/// An error or warning from static analysis.
/// </summary>
public sealed record AnalysisError(
    string Code,
    string Message,
    SourceLocation? Location,
    AnalysisErrorSeverity Severity = AnalysisErrorSeverity.Error);
