using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// Extension helpers for raising <see cref="XQueryException"/> with the current execution
/// context's source location auto-attached. Lets call sites throughout the function library
/// — which see the AST-level <see cref="Ast.ExecutionContext"/> interface, not the concrete
/// <see cref="Execution.QueryExecutionContext"/> — pick up runtime location information
/// without each having to type-test and downcast.
/// </summary>
/// <remarks>
/// When the context isn't a <see cref="Execution.QueryExecutionContext"/> (e.g. test
/// harnesses, or contexts that haven't yet pushed any location), the helper falls back to
/// a locationless exception — same observable behavior as the legacy direct constructor.
/// </remarks>
public static class ExecutionContextErrorExtensions
{
    /// <summary>
    /// Constructs an <see cref="XQueryException"/> tagged with the current location.
    /// Safe to call on a <c>null</c> receiver — falls back to a locationless exception,
    /// so deep static helpers can be threaded with <c>Ast.ExecutionContext? context = null</c>
    /// and still call <c>context.Error(...)</c> uniformly.
    /// </summary>
    public static XQueryException Error(this Ast.ExecutionContext? context, string errorCode, string message)
    {
        if (context is Execution.QueryExecutionContext qec)
            return qec.Error(errorCode, message);
        return new XQueryException(errorCode, message);
    }

    /// <summary>Same as <see cref="Error(Ast.ExecutionContext?, string, string)"/> with an inner exception.</summary>
    public static XQueryException Error(this Ast.ExecutionContext? context, string errorCode, string message, Exception innerException)
    {
        if (context is Execution.QueryExecutionContext qec)
            return qec.Error(errorCode, message, innerException);
        return new XQueryException(errorCode, message, innerException);
    }
}
