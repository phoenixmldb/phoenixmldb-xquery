using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// Exception thrown by the XQuery <c>fn:error()</c> function or when a built-in function encounters
/// an error condition defined by the XQuery and XPath Functions and Operators specification.
/// </summary>
/// <remarks>
/// <para>
/// This is the base exception type for errors raised by XQuery built-in functions (e.g., type
/// conversion failures, invalid arguments). It is distinct from <see cref="Execution.XQueryRuntimeException"/>,
/// which covers dynamic errors raised by the execution engine itself (e.g., unbound variables, context errors).
/// </para>
/// <para>
/// The <see cref="ErrorCode"/> follows the standard XQuery error code format (e.g., <c>"FOTY0013"</c>
/// for atomization of function items, <c>"FOER0000"</c> for <c>fn:error()</c> with no arguments).
/// </para>
/// </remarks>
/// <seealso cref="Execution.XQueryRuntimeException"/>
/// <seealso cref="Parser.XQueryParseException"/>
public class XQueryException : Exception
{
    /// <summary>
    /// The XQuery error code (e.g., <c>"FOTY0013"</c>, <c>"FOER0000"</c>) identifying the error
    /// as defined by the XQuery and XPath Functions and Operators specification.
    /// </summary>
    public string ErrorCode { get; }

    /// <summary>
    /// Namespace URI of the error code QName. Null for built-in errors (which live in the
    /// default err: namespace, <c>http://www.w3.org/2005/xqt-errors</c>).
    /// </summary>
    public string? ErrorNamespaceUri { get; init; }

    /// <summary>
    /// Prefix of the error QName (e.g., <c>"example"</c> for <c>example:EXER3141</c>).
    /// Null for built-in errors.
    /// </summary>
    public string? ErrorPrefix { get; init; }

    /// <summary>
    /// The error value passed as the third argument to <c>fn:error($code, $description, $error-object)</c>.
    /// Null when no error object was provided.
    /// </summary>
    public object? ErrorValue { get; init; }

    /// <summary>
    /// The originating module URI (file path or system id), if known. Populated for
    /// errors that fire from an executor with a <see cref="SourceLocation"/> in hand.
    /// </summary>
    public string? Module { get; init; }

    /// <summary>
    /// 1-based line number in the originating module, or <c>null</c> if not known.
    /// </summary>
    public int? Line { get; init; }

    /// <summary>
    /// Column number in the originating module, or <c>null</c> if not known.
    /// 1-based for XSLT-shifted file-absolute positions; 0-based for raw ANTLR-only
    /// (XQuery-direct) contexts. See <see cref="Ast.SourceLocation"/> remarks for the
    /// full convention.
    /// </summary>
    public int? Column { get; init; }

    /// <summary>
    /// Phase D7 source-location-audit: secondary locations related to this error,
    /// e.g. the position of a specific input <see cref="Xdm.Nodes.XdmNode"/> that
    /// triggered a type assertion. Empty when the error is purely localized at the
    /// stylesheet position (most common case). LSP adapters surface these as
    /// "related diagnostics" alongside the primary <see cref="Module"/>/<see cref="Line"/>/
    /// <see cref="Column"/> location, so users can jump from the assertion site to the
    /// data position that violated it.
    /// </summary>
    public IReadOnlyList<Ast.SourceLocation> RelatedLocations { get; init; } = Array.Empty<Ast.SourceLocation>();

    /// <summary>
    /// Creates a new <see cref="XQueryException"/> with the specified error code and message.
    /// </summary>
    /// <param name="errorCode">A standard XQuery error code (e.g., <c>"FOTY0013"</c>).</param>
    /// <param name="message">A human-readable description of the error.</param>
    public XQueryException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Creates a new <see cref="XQueryException"/> with the specified error code, message, and inner exception.
    /// </summary>
    /// <param name="errorCode">A standard XQuery error code.</param>
    /// <param name="message">A human-readable description of the error.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public XQueryException(string errorCode, string message, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Creates a new <see cref="XQueryException"/> carrying source-location info from a
    /// <see cref="SourceLocation"/>. The formatted <see cref="Exception.Message"/> is
    /// prefixed with <c>[module:line:col] </c> (or <c>[line:col] </c> when no module is
    /// known) so plain string-based logging surfaces the location without needing to
    /// inspect the exception's structured properties.
    /// </summary>
    /// <param name="errorCode">A standard XQuery error code.</param>
    /// <param name="message">A human-readable description of the error.</param>
    /// <param name="location">
    /// Source location of the offending expression, or <c>null</c> when no location info
    /// is available (in which case behavior matches the 2-argument constructor).
    /// </param>
    public XQueryException(string errorCode, string message, SourceLocation? location)
        : base(FormatWithLocation(message, location))
    {
        ErrorCode = errorCode;
        if (location is not null)
        {
            Module = location.Module;
            Line = location.Line;
            Column = location.Column;
        }
    }

    /// <summary>
    /// Same as <see cref="XQueryException(string, string, SourceLocation?)"/> but wraps
    /// an inner exception. Used by <see cref="Execution.QueryExecutionContext.Error(string, string, Exception)"/>.
    /// </summary>
    public XQueryException(string errorCode, string message, Exception innerException, SourceLocation? location)
        : base(FormatWithLocation(message, location), innerException)
    {
        ErrorCode = errorCode;
        if (location is not null)
        {
            Module = location.Module;
            Line = location.Line;
            Column = location.Column;
        }
    }

    private static string FormatWithLocation(string message, SourceLocation? location)
    {
        if (location is null) return message;
        return location.Module is { Length: > 0 } module
            ? $"[{module}:{location.Line}:{location.Column}] {message}"
            : $"[line {location.Line}, col {location.Column}] {message}";
    }
}
