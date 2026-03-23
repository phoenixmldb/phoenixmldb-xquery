using Antlr4.Runtime;
using PhoenixmlDb.XQuery.Parser.Grammar;

namespace PhoenixmlDb.XQuery.Parser;

/// <summary>
/// An <see cref="ITokenSource"/> adapter that wraps <see cref="XQueryLexer"/> to manage
/// lexer mode transitions for XQuery direct element constructors.
/// </summary>
/// <remarks>
/// <para>
/// ANTLR4 lexer rules each support a single mode action (<c>pushMode</c>, <c>popMode</c>, or <c>mode</c>).
/// XQuery direct element constructors require multi-step mode transitions that cannot be expressed
/// in a single lexer action. This adapter intercepts tokens from the lexer and performs the
/// additional mode stack manipulations needed for correct parsing:
/// </para>
/// <list type="bullet">
///   <item><description><c>LESS_THAN</c> followed by a name-start character pushes <c>START_TAG</c> mode.</description></item>
///   <item><description><c>RBRACE</c> while inside element content pops back from <c>DEFAULT_MODE</c> to <c>ELEM_CONTENT</c>.</description></item>
///   <item><description><c>END_TAG_CLOSE</c> pops both <c>END_TAG</c> (done by the lexer) and <c>ELEM_CONTENT</c> (done here).</description></item>
/// </list>
/// </remarks>
internal sealed class XQueryLexerAdapter : ITokenSource
{
    private readonly XQueryLexer _lexer;

    /// <summary>
    /// Tracks element nesting depth so we know when <c>RBRACE</c> should pop back to ELEM_CONTENT
    /// vs. being a normal brace in default mode.
    /// </summary>
    private int _elemDepth;

    public XQueryLexerAdapter(XQueryLexer lexer)
    {
        _lexer = lexer;
    }

    public int Line => _lexer.Line;
    public int Column => _lexer.Column;
    public ICharStream InputStream => (ICharStream)_lexer.InputStream;
    public string SourceName => _lexer.SourceName;

    public ITokenFactory TokenFactory
    {
        get => _lexer.TokenFactory;
        set => _lexer.TokenFactory = value;
    }

    public IToken NextToken()
    {
        var token = _lexer.NextToken();

        switch (token.Type)
        {
            case XQueryLexer.LESS_THAN:
                // If the next character is a name-start char, this begins a direct element constructor.
                // Push START_TAG mode so the lexer handles tag content.
                if (IsNameStartChar(_lexer.InputStream.LA(1)))
                {
                    _lexer.PushMode(XQueryLexer.START_TAG);
                    _elemDepth++;
                }
                break;

            case XQueryLexer.START_TAG_CLOSE:
                // The lexer rule already did mode(ELEM_CONTENT), replacing START_TAG.
                // No additional action needed here.
                break;

            case XQueryLexer.START_TAG_EMPTY_CLOSE:
                // Self-closing tag: the lexer did popMode (leaves START_TAG).
                // Decrease depth since no ELEM_CONTENT was entered.
                _elemDepth--;
                break;

            case XQueryLexer.ELEM_CONTENT_OPEN_TAG:
                // Nested element in content: lexer pushed START_TAG.
                _elemDepth++;
                break;

            case XQueryLexer.ELEM_CONTENT_LBRACE:
                // Enclosed expression: lexer pushed DEFAULT_MODE on top of ELEM_CONTENT.
                // No additional action needed.
                break;

            case XQueryLexer.RBRACE:
                // If we're inside element content (depth > 0), this RBRACE closes an enclosed
                // expression. Pop DEFAULT_MODE to return to ELEM_CONTENT.
                if (_elemDepth > 0)
                {
                    _lexer.PopMode();
                }
                break;

            case XQueryLexer.END_TAG_CLOSE:
                // The lexer rule did popMode (pops END_TAG, returning to ELEM_CONTENT).
                // We need one more popMode to leave ELEM_CONTENT and return to the parent mode.
                _lexer.PopMode();
                if (_elemDepth > 0) _elemDepth--;
                break;
        }

        return token;
    }

    /// <summary>
    /// Returns <c>true</c> if the character code point is an XML NameStartChar.
    /// </summary>
    private static bool IsNameStartChar(int c)
    {
        return c is (>= 'a' and <= 'z')
                  or (>= 'A' and <= 'Z')
                  or '_'
                  or (>= 0x00C0 and <= 0x00D6)
                  or (>= 0x00D8 and <= 0x00F6)
                  or (>= 0x00F8 and <= 0x02FF)
                  or (>= 0x0370 and <= 0x037D)
                  or (>= 0x037F and <= 0x1FFF)
                  or (>= 0x200C and <= 0x200D)
                  or (>= 0x2070 and <= 0x218F)
                  or (>= 0x2C00 and <= 0x2FEF)
                  or (>= 0x3001 and <= 0xD7FF)
                  or (>= 0xF900 and <= 0xFDCF)
                  or (>= 0xFDF0 and <= 0xFFFD);
    }
}
