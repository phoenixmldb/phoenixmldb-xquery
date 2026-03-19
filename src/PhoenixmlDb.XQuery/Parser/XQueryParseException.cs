using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Parser;

/// <summary>
/// Exception thrown when XQuery parsing fails.
/// </summary>
public sealed class XQueryParseException : Exception
{
    public IReadOnlyList<ParseError> Errors { get; }

    public XQueryParseException(IReadOnlyList<ParseError> errors)
        : base(FormatMessage(errors))
    {
        Errors = errors;
    }

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
/// A single parse error with location information.
/// </summary>
public sealed record ParseError(string Message, int Line, int Column);
