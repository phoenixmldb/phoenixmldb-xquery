using Antlr4.Runtime;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Parser.Grammar;

namespace PhoenixmlDb.XQuery.Parser;

/// <summary>
/// Public API for parsing XQuery 3.1 source text into an abstract syntax tree (AST).
/// </summary>
/// <remarks>
/// <para>
/// This class provides two parsing strategies:
/// <list type="bullet">
///   <item><description><see cref="Parse"/> — throws <see cref="XQueryParseException"/> on syntax errors. Use when you expect valid input and want fail-fast behavior.</description></item>
///   <item><description><see cref="TryParse"/> — returns <c>null</c> on failure and populates an error list. Use for IDE-style validation or when you want to report all errors without exceptions.</description></item>
/// </list>
/// </para>
/// <para>
/// The returned <see cref="XQueryExpression"/> AST can be inspected, transformed, or passed directly
/// to <c>QueryEngine.Compile</c> for compilation
/// and execution. This is useful for syntax validation without execution, AST-level transformations,
/// or building tooling such as formatters and linters.
/// </para>
/// </remarks>
/// <example>
/// <para>Parse and validate syntax:</para>
/// <code>
/// var parser = new XQueryParserFacade();
/// if (parser.TryParse("for $x in (1,2,3) return $x * 2", out var errors) is { } ast)
///     Console.WriteLine("Valid XQuery");
/// else
///     foreach (var error in errors)
///         Console.Error.WriteLine($"Line {error.Line}: {error.Message}");
/// </code>
/// </example>
public sealed class XQueryParserFacade
{
    /// <summary>
    /// Parses an XQuery expression string into an AST, throwing on any syntax error.
    /// </summary>
    /// <param name="xquery">The XQuery source text. Must not be <c>null</c>.</param>
    /// <returns>The root <see cref="XQueryExpression"/> node of the parsed AST.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="xquery"/> is <c>null</c>.</exception>
    /// <exception cref="XQueryParseException">Thrown when the input contains one or more syntax errors. The <see cref="XQueryParseException.Errors"/> property contains location details.</exception>
    /// <seealso cref="TryParse"/>
    public XQueryExpression Parse(string xquery)
    {
        ArgumentNullException.ThrowIfNull(xquery);

        // XQuery spec §A.2.1: end-of-line normalization — #xD#xA → #xA, lone #xD → #xA
        if (xquery.Contains('\r'))
            xquery = xquery.Replace("\r\n", "\n").Replace('\r', '\n');

        var inputStream = new AntlrInputStream(xquery);
        var lexer = new XQueryLexer(inputStream);
        var adapter = new XQueryLexerAdapter(lexer);
        var tokenStream = new CommonTokenStream(adapter);
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
        builder.SetTokenStream(tokenStream);
        return builder.Visit(tree);
    }

    /// <summary>
    /// Attempts to parse an XQuery expression without throwing on syntax errors.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="Parse"/>, this method never throws for syntax errors. It returns <c>null</c>
    /// and populates <paramref name="errors"/> with all parse errors including line and column information.
    /// This is the preferred method for interactive validation scenarios.
    /// </remarks>
    /// <param name="xquery">The XQuery source text.</param>
    /// <param name="errors">When the method returns <c>null</c>, contains one or more <see cref="ParseError"/> instances with location details. Empty on success.</param>
    /// <returns>The parsed AST on success, or <c>null</c> if parsing failed.</returns>
    /// <seealso cref="Parse"/>
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
