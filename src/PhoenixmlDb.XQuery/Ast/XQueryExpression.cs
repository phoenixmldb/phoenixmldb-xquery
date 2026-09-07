namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Base type for all XQuery expressions.
/// </summary>
public abstract class XQueryExpression
{
    /// <summary>
    /// Source location for error reporting. Settable so the XSLT compiler can augment
    /// post-parse with the originating XSLT element's source URI / line / column —
    /// without this, errors from XPath embedded in XSLT show only the position
    /// relative to the inline XPath string ("[line 2, col 24]"), which is useless
    /// across thousands of similar expressions in real stylesheets like Docbook TNG.
    /// </summary>
    public SourceLocation? Location { get; set; }

    /// <summary>
    /// Static type after type checking (null before analysis).
    /// </summary>
    public XdmSequenceType? StaticType { get; internal set; }

    /// <summary>
    /// Accept a visitor.
    /// </summary>
    public abstract T Accept<T>(IXQueryExpressionVisitor<T> visitor);
}
