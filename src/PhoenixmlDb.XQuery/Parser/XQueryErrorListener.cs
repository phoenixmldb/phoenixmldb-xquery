using Antlr4.Runtime;

namespace PhoenixmlDb.XQuery.Parser;

/// <summary>
/// ANTLR error listener that collects parse errors.
/// Implements both lexer and parser error listener interfaces.
/// </summary>
internal sealed class XQueryErrorListener : BaseErrorListener, IAntlrErrorListener<int>
{
    private readonly List<ParseError> _errors = [];

    public IReadOnlyList<ParseError> Errors => _errors;
    public bool HasErrors => _errors.Count > 0;

    /// <summary>
    /// Parser syntax error handler.
    /// </summary>
    public override void SyntaxError(
        TextWriter output,
        IRecognizer recognizer,
        IToken offendingSymbol,
        int line,
        int charPositionInLine,
        string msg,
        RecognitionException e)
    {
        _errors.Add(new ParseError(msg, line, charPositionInLine));
    }

    /// <summary>
    /// Lexer syntax error handler.
    /// </summary>
    public void SyntaxError(
        TextWriter output,
        IRecognizer recognizer,
        int offendingSymbol,
        int line,
        int charPositionInLine,
        string msg,
        RecognitionException e)
    {
        _errors.Add(new ParseError(msg, line, charPositionInLine));
    }
}
