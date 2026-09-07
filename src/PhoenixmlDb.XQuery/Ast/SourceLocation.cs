namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Source location for error reporting and debugging.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Module"/> identifies the originating stylesheet/query module — the value of
/// <c>xml:base</c> or the system-id of the source file. It's the diagnostic anchor users
/// need when an error fires inside an imported/included module: without it, callers only
/// see a line/column number but can't tell which file the error came from.
/// </para>
/// <para>
/// <see cref="Module"/> is populated by callers that have a base URI in hand (e.g. the
/// XSLT parser via <c>XElement.BaseUri</c>); older callers that don't set it leave it
/// <c>null</c> and the formatter falls back to the bare line/column form.
/// </para>
/// <para>
/// <b>Coordinate conventions (Phase D8 of the source-location audit):</b>
/// <list type="bullet">
///   <item><description><see cref="Line"/> is <b>1-based</b> in all contexts (matching
///         <c>System.Xml.IXmlLineInfo</c> and ANTLR-runtime conventions).</description></item>
///   <item><description><see cref="Column"/> is <b>1-based</b> for XSLT-shifted
///         file-absolute positions (set by the XSLT <c>StylesheetParser</c> shift helpers
///         from <c>IXmlLineInfo.LinePosition</c>) and <b>0-based</b> for raw ANTLR-only
///         contexts (XQuery-direct parses where no shift was applied). Consumers that need
///         a single convention should check the presence of <see cref="Module"/> to
///         distinguish: file-absolute (Module set) → 1-based; XPath-relative (Module
///         null) → 0-based. LSP adapters should normalize to 0-based UTF-16.</description></item>
///   <item><description><see cref="StartIndex"/> / <see cref="EndIndex"/> are 0-based
///         character offsets into the parsed input string (XPath text). They remain
///         input-string-relative even after location shifts. <see cref="EndIndex"/> is
///         <i>inclusive</i> (matches ANTLR's <c>StopIndex</c>): the character at
///         <c>EndIndex</c> is the last character of the token range.</description></item>
/// </list>
/// </para>
/// </remarks>
public sealed record SourceLocation(int Line, int Column, int StartIndex, int EndIndex)
{
    /// <summary>
    /// The originating module URI (file path / system id), or <c>null</c> if unknown.
    /// </summary>
    public string? Module { get; init; }

    /// <summary>
    /// Token length in characters, computed as <c>EndIndex - StartIndex + 1</c>
    /// (since <see cref="EndIndex"/> is inclusive). Returns <c>0</c> when the range
    /// is degenerate (<c>EndIndex &lt; StartIndex</c>) — used by LSP adapters to size
    /// the diagnostic squiggle. The length is in characters of the parsed input
    /// string (XPath text), not file-absolute bytes.
    /// </summary>
    public int Length => EndIndex >= StartIndex ? EndIndex - StartIndex + 1 : 0;
}
