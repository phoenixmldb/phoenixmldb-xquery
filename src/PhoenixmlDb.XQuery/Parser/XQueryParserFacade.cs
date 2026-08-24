using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
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
    /// When <c>true</c>, the parser accepts XPath/XSLT-only constructs that XQuery rejects —
    /// currently the <c>namespace::</c> axis (XQuery raises XQST0134, but XPath 3.1 and XSLT 3.0
    /// retain it as an optional axis used by stylesheets like DocBook xslTNG). Default <c>false</c>
    /// preserves strict XQuery semantics.
    /// </summary>
    public bool AllowNamespaceAxis { get; init; }

    /// <summary>
    /// When <c>true</c>, a raw <c>&amp;</c> character is accepted inside string literals and is treated
    /// as a literal ampersand rather than the start of an entity reference. XPath (used by XSLT
    /// stylesheets) has no entity references — the <c>&amp;amp;</c> was already decoded to <c>&amp;</c>
    /// by the XML parser before the expression text reached this parser — so an XPath string literal
    /// can legitimately contain a bare <c>&amp;</c> (e.g. the W3C regex conformance test
    /// <c>regex-070</c> mode j uses <c>'Special characters$&amp;'</c>). Default <c>false</c> preserves
    /// strict XQuery semantics, under which <c>&amp;</c> must introduce a predefined entity reference or
    /// character reference.
    /// </summary>
    public bool AllowRawAmpersand { get; init; }

    /// <summary>
    /// Whether to apply XQuery's source-level end-of-line normalization (§A.2.1) before
    /// parsing. Default <c>true</c>, which is correct for a query FILE.
    /// </summary>
    /// <remarks>
    /// XSLT sets this false. An XPath expression embedded in a stylesheet has already been
    /// through an XML parser, so a <c>&amp;#xD;</c> character reference inside a string literal
    /// is DATA by the time it reaches here — XML explicitly exempts character references from
    /// line-ending normalization (XML 1.0 §2.11), while normalizing literal line breaks in the
    /// file. Applying the query-source rule a second time cannot distinguish the two and
    /// silently rewrites the data:
    ///
    ///     string-to-codepoints('&amp;#x9;&amp;#xa;&amp;#xd;')   gave 9 10 10, must be 9 10 13
    ///
    /// For a real .xq file the normalization stays on and stays correct: there a
    /// <c>&amp;#xD;</c> is six ASCII characters the XQuery lexer decodes itself, untouched by
    /// this pass, and only genuine line breaks are rewritten.
    /// </remarks>
    public bool NormalizeLineEndings { get; init; } = true;

    /// <summary>
    /// Maximum ANTLR parser rule-invocation nesting depth accepted before the parse is aborted with a
    /// catchable <see cref="XQueryParseException"/>. The generated recursive-descent parser (and the
    /// visitor that later walks its tree) recurse once per grammar rule, so an adversarially deep query —
    /// e.g. thousands of nested parentheses or predicates — would otherwise exhaust the native call stack
    /// and abort the process with an uncatchable <see cref="StackOverflowException"/> on untrusted input.
    /// </summary>
    /// <remarks>
    /// Each level of user-visible expression nesting (a parenthesis, a predicate, …) costs roughly 27
    /// grammar-rule frames because of XPath's precedence chain, so this cap corresponds to on the order of
    /// ~90 levels of nesting — far above any realistic query, yet an order of magnitude below the depth at
    /// which the parser/AST-builder actually overflow (empirically ~24,000 rule frames on a 1&#160;MB stack).
    /// The guard fires while the rule is being <em>entered</em>, so it bails before descending further, and
    /// throws a plain <see cref="XQueryParseException"/> (not a <see cref="RecognitionException"/>) so ANTLR's
    /// error-recovery strategy cannot swallow it and resume the runaway recursion.
    /// </remarks>
    internal const int MaxParseDepth = 2500;

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

        // XQuery spec §A.2.1: end-of-line normalization — #xD#xA → #xA, lone #xD → #xA.
        // Skipped for embedded XPath, where a CR can only have come from a character
        // reference the XML parser already decoded — see NormalizeLineEndings.
        if (NormalizeLineEndings && xquery.Contains('\r'))
            xquery = xquery.Replace("\r\n", "\n").Replace('\r', '\n');

        var inputStream = new AntlrInputStream(xquery);
        var lexer = new XQueryLexer(inputStream) { AllowRawAmpersand = AllowRawAmpersand };
        var adapter = new XQueryLexerAdapter(lexer);
        var tokenStream = new CommonTokenStream(adapter);
        var parser = new Grammar.XQueryParser(tokenStream);

        var lexerErrors = new XQueryErrorListener();
        var parserErrors = new XQueryErrorListener();
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(lexerErrors);
        parser.RemoveErrorListeners();
        parser.AddErrorListener(parserErrors);

        // Bound recursion depth so pathologically nested input fails with a catchable parse error
        // instead of overflowing the native stack and aborting the host. See MaxParseDepth.
        parser.AddParseListener(new DepthGuardListener(MaxParseDepth));

        var tree = parser.module();

        if (lexerErrors.HasErrors)
            throw new XQueryParseException(lexerErrors.Errors);
        if (parserErrors.HasErrors)
            throw new XQueryParseException(parserErrors.Errors);

        var builder = new XQueryAstBuilder { AllowNamespaceAxis = AllowNamespaceAxis, AllowRawAmpersand = AllowRawAmpersand };
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

    /// <summary>
    /// A parse listener that tracks ANTLR rule-invocation nesting depth and aborts the parse with a
    /// catchable <see cref="XQueryParseException"/> once it exceeds <see cref="MaxParseDepth"/>. Fires as
    /// each rule is entered (before descending into its children) so the runaway recursion stops early.
    /// A plain exception is thrown deliberately: ANTLR's <c>DefaultErrorStrategy</c> only catches
    /// <see cref="RecognitionException"/>, so a non-recognition exception propagates straight out of
    /// <c>module()</c> rather than triggering error recovery that would resume the descent.
    /// </summary>
    private sealed class DepthGuardListener(int maxDepth) : IParseTreeListener
    {
        private int _depth;

        public void EnterEveryRule(ParserRuleContext ctx)
        {
            if (++_depth > maxDepth)
                throw new XQueryParseException(
                    $"XPST0003: query nesting depth exceeds the maximum supported limit of {maxDepth} " +
                    "grammar levels; the query is too deeply nested to parse safely.");
        }

        public void ExitEveryRule(ParserRuleContext ctx) => _depth--;
        public void VisitTerminal(ITerminalNode node) { }
        public void VisitErrorNode(IErrorNode node) { }
    }
}
