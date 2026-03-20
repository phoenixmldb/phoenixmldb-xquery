using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Parser;

/// <summary>
/// Exception thrown when XQuery parsing fails due to syntax errors in the source text.
/// </summary>
/// <remarks>
/// <para>
/// This exception is thrown by <see cref="XQueryParserFacade.Parse"/> when the input contains
/// one or more syntax errors. The <see cref="Errors"/> property provides structured error
/// information including line and column numbers for each error.
/// </para>
/// <para>
/// To avoid exceptions for expected invalid input, use <see cref="XQueryParserFacade.TryParse"/>
/// instead, which returns errors through an <c>out</c> parameter.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// try
/// {
///     var ast = parser.Parse("for $x in (1,2,3) return");  // missing return expression
/// }
/// catch (XQueryParseException ex)
/// {
///     foreach (var error in ex.Errors)
///         Console.Error.WriteLine($"Line {error.Line}, Column {error.Column}: {error.Message}");
/// }
/// </code>
/// </example>
/// <seealso cref="XQueryParserFacade"/>
/// <seealso cref="ParseError"/>
public sealed class XQueryParseException : Exception
{
    /// <summary>
    /// The structured list of parse errors with source location information.
    /// </summary>
    public IReadOnlyList<ParseError> Errors { get; }

    /// <summary>
    /// Creates a new <see cref="XQueryParseException"/> from a list of parse errors.
    /// </summary>
    /// <param name="errors">One or more parse errors.</param>
    public XQueryParseException(IReadOnlyList<ParseError> errors)
        : base(FormatMessage(errors))
    {
        Errors = errors;
    }

    /// <summary>
    /// Creates a new <see cref="XQueryParseException"/> from a single error message.
    /// The error is placed at line 1, column 0.
    /// </summary>
    /// <param name="message">The error message.</param>
    public XQueryParseException(string message) : base(message)
    {
        Errors = [new ParseError(message, 1, 0)];
    }

    private static string FormatMessage(IReadOnlyList<ParseError> errors)
    {
        if (errors.Count == 1)
            return errors[0].Message;
        return $"XQuery parse failed with {errors.Count} errors: {errors[0].Message}";
    }
}

/// <summary>
/// A single parse error with source location information.
/// </summary>
/// <remarks>
/// Instances are produced by the parser and collected in <see cref="XQueryParseException.Errors"/>
/// or returned via <see cref="XQueryParserFacade.TryParse"/>. Line numbers are 1-based;
/// column numbers are 0-based character offsets within the line.
/// </remarks>
/// <param name="Message">A human-readable description of the syntax error.</param>
/// <param name="Line">The 1-based line number where the error occurred.</param>
/// <param name="Column">The 0-based column (character offset) within the line.</param>
public sealed record ParseError(string Message, int Line, int Column);
