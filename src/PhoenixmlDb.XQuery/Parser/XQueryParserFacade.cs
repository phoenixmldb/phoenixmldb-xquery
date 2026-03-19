using Antlr4.Runtime;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Parser.Grammar;

namespace PhoenixmlDb.XQuery.Parser;

/// <summary>
/// Public API for parsing XQuery strings into AST expressions.
/// </summary>
public sealed class XQueryParserFacade
{
    /// <summary>
    /// Parses an XQuery expression string into an AST.
    /// </summary>
    /// <param name="xquery">The XQuery source text.</param>
    /// <returns>The parsed expression AST.</returns>
    /// <exception cref="XQueryParseException">If the input has syntax errors.</exception>
    public XQueryExpression Parse(string xquery)
    {
        ArgumentNullException.ThrowIfNull(xquery);

        var inputStream = new AntlrInputStream(xquery);
        var lexer = new XQueryLexer(inputStream);
        var tokenStream = new CommonTokenStream(lexer);
        var parser = new Grammar.XQueryParser(tokenStream);

        var lexerErrors = new XQueryErrorListener();
        var parserErrors = new XQueryErrorListener();
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(lexerErrors);
        parser.RemoveErrorListeners();
        parser.AddErrorListener(parserErrors);

        var tree = parser.module();

        if (lexerErrors.HasErrors)
            throw new XQueryParseException(lexerErrors.Errors);
        if (parserErrors.HasErrors)
            throw new XQueryParseException(parserErrors.Errors);

        var builder = new XQueryAstBuilder();
        return builder.Visit(tree);
    }

    /// <summary>
    /// Tries to parse an XQuery expression, returning null on failure.
    /// </summary>
    public XQueryExpression? TryParse(string xquery, out IReadOnlyList<ParseError> errors)
    {
        try
        {
            errors = [];
            return Parse(xquery);
        }
        catch (XQueryParseException ex)
        {
            errors = ex.Errors;
            return null;
        }
    }
}
