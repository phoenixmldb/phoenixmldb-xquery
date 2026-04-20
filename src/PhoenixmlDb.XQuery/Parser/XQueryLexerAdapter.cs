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

    /// <summary>
    /// Tracks string constructor interpolation depth so we know when <c>RBRACE</c> should pop back
    /// to STRING_CONSTRUCTOR mode vs. being a normal brace in default mode.
    /// </summary>
    private int _stringConstructorDepth;

    /// <summary>
    /// Tracks attribute value enclosed expression depth so we know when <c>RBRACE</c> should pop
    /// back to ATTR_VALUE_DQ or ATTR_VALUE_SQ mode.
    /// </summary>
    private int _attrValueDepth;

    /// <summary>
    /// Tracks nested brace depth within enclosed expressions so that inner <c>{}</c> (maps,
    /// computed constructors) don't prematurely pop out of element content.
    /// </summary>
    private int _enclosedBraceDepth;

    /// <summary>
    /// When a LESS_THAN_SLASH token is split into LESS_THAN + SLASH, this holds the
    /// deferred SLASH token to be returned on the next call.
    /// </summary>
    private IToken? _deferredToken;

    /// <summary>
    /// The type of the last non-whitespace token — used to disambiguate &lt; between
    /// comparison operator and direct element constructor start.
    /// </summary>
    private int _lastTokenType = -1;

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
        if (_deferredToken != null)
        {
            var deferred = _deferredToken;
            _deferredToken = null;
            return deferred;
        }

        var token = _lexer.NextToken();

        switch (token.Type)
        {
            case XQueryLexer.LESS_THAN_SLASH:
                // Outside element content, '</' is not a closing tag — split into '<' + '/'.
                // Inside element content (elemDepth > 0), this is a genuine end tag opener.
                if (_elemDepth == 0)
                {
                    // Create a LESS_THAN token for '<' at the original position
                    var factory = _lexer.TokenFactory;
                    var lt = factory.Create(
                        XQueryLexer.LESS_THAN, "<");
                    if (lt is CommonToken ltc)
                    {
                        ltc.Line = token.Line;
                        ltc.Column = token.Column;
                        ltc.StartIndex = token.StartIndex;
                        ltc.StopIndex = token.StartIndex;
                        ltc.TokenIndex = -1;
                    }
                    // Defer a SLASH token for '/' at position + 1
                    var slash = factory.Create(
                        XQueryLexer.SLASH, "/");
                    if (slash is CommonToken slashc)
                    {
                        slashc.Line = token.Line;
                        slashc.Column = token.Column + 1;
                        slashc.StartIndex = token.StartIndex + 1;
                        slashc.StopIndex = token.StartIndex + 1;
                        slashc.TokenIndex = -1;
                    }
                    _deferredToken = slash;
                    return lt;
                }
                break;

            case XQueryLexer.LESS_THAN:
                // If the next character is a name-start char and we're in a position where a
                // direct element constructor is valid, push START_TAG mode.
                // After tokens like ), ], NCName, literals, *, etc. the '<' is a comparison operator.
                if (IsNameStartChar(_lexer.InputStream.LA(1)) && !IsOperatorPosition(_lastTokenType))
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

            case XQueryLexer.STRING_CONSTRUCTOR_INTERPOLATION_OPEN:
                // Interpolation open `{ pushes DEFAULT_MODE (done by lexer rule).
                // Track depth so we know when RBRACE should return to STRING_CONSTRUCTOR.
                _stringConstructorDepth++;
                break;

            case XQueryLexer.ATTR_DQ_LBRACE:
            case XQueryLexer.ATTR_SQ_LBRACE:
                // Enclosed expression in attribute value: lexer pushed DEFAULT_MODE.
                // Track depth so RBRACE pops back to the attribute value mode.
                _attrValueDepth++;
                break;

            case XQueryLexer.LBRACE:
                // Track nested braces within enclosed expressions
                if (_elemDepth > 0 || _stringConstructorDepth > 0 || _attrValueDepth > 0)
                    _enclosedBraceDepth++;
                break;

            case XQueryLexer.RBRACE:
                // If we have nested braces within an enclosed expression, just decrement
                if (_enclosedBraceDepth > 0)
                {
                    _enclosedBraceDepth--;
                    break;
                }
                // If we're inside a string constructor interpolation, this RBRACE closes the
                // expression. Pop DEFAULT_MODE to return to STRING_CONSTRUCTOR.
                if (_stringConstructorDepth > 0)
                {
                    _lexer.PopMode();
                    _stringConstructorDepth--;
                }
                // If we're inside an attribute value enclosed expression, pop back to attr mode.
                else if (_attrValueDepth > 0)
                {
                    _lexer.PopMode();
                    _attrValueDepth--;
                }
                // If we're inside element content (depth > 0), this RBRACE closes an enclosed
                // expression. Pop DEFAULT_MODE to return to ELEM_CONTENT.
                else if (_elemDepth > 0)
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

        _lastTokenType = token.Type;
        return token;
    }

    /// <summary>
    /// Returns <c>true</c> if the last token type indicates we are in an "operator position"
    /// where <c>&lt;</c> must be a comparison operator, not a direct element constructor.
    /// After these tokens, <c>&lt;</c> cannot start an element constructor per XQuery grammar.
    /// </summary>
    private static bool IsOperatorPosition(int tokenType)
    {
        return tokenType is XQueryLexer.RPAREN
            or XQueryLexer.RBRACKET
            or XQueryLexer.IntegerLiteral
            or XQueryLexer.DecimalLiteral
            or XQueryLexer.DoubleLiteral
            or XQueryLexer.StringLiteral
            or XQueryLexer.NCName
            or XQueryLexer.URIQualifiedName
            or XQueryLexer.DOT
            or XQueryLexer.DOTDOT
            or XQueryLexer.QUESTION;
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
