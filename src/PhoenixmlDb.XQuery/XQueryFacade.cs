using System.Text;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery;

/// <summary>
/// A simple string-in / string-out API for XQuery evaluation.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="XQueryFacade"/> is the easiest way to evaluate XQuery expressions against XML input.
/// It handles document parsing, engine setup, execution, and result serialization internally,
/// providing a clean "pit of success" experience.
/// </para>
/// <para>
/// Each method creates a fresh <see cref="XdmDocumentStore"/> and <see cref="QueryEngine"/> per call,
/// making the facade safe for concurrent use. For high-throughput scenarios where you want to reuse
/// a store across multiple queries, use <see cref="QueryEngine"/> directly with a shared
/// <see cref="XdmDocumentStore"/>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var xquery = new XQueryFacade();
///
/// // Context item — the standard XQuery way. Input XML is available as "." (the context item).
/// string result = await xquery.EvaluateAsync("//book/title/text()", inputXml);
///
/// // $input variable also works (backward compatible)
/// string result2 = await xquery.EvaluateAsync("$input//book/title/text()", inputXml);
///
/// // All results as strings
/// IReadOnlyList&lt;string&gt; results = await xquery.EvaluateAllAsync("//book/title/text()", inputXml);
///
/// // Scalar
/// string? title = await xquery.EvaluateScalarAsync("//book[1]/title/text()", inputXml);
///
/// // No input XML needed
/// string sum = await xquery.EvaluateAsync("1 + 1");
/// </code>
/// </example>
public sealed class XQueryFacade
{
    private const string InputDocumentUri = "urn:xqueryfacade:input";

    /// <summary>
    /// Evaluates an XQuery expression and returns all results concatenated as a single string.
    /// </summary>
    /// <param name="xquery">The XQuery expression to evaluate.</param>
    /// <param name="inputXml">
    /// Optional XML input. When provided, the parsed document is set as the XQuery context item
    /// (available as <c>.</c>) and also bound as <c>$input</c> for backward compatibility.
    /// The document is also accessible via <c>doc('urn:xqueryfacade:input')</c>.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the evaluation.</param>
    /// <returns>All result items serialized and concatenated. Returns an empty string if the result is the empty sequence.</returns>
    public async Task<string> EvaluateAsync(string xquery, string? inputXml = null, CancellationToken cancellationToken = default)
    {
        var (engine, store, wrappedQuery, contextItem) = SetUp(xquery, inputXml);

        var sb = new StringBuilder();
        await foreach (var item in engine.ExecuteAsync(wrappedQuery, initialContextItem: contextItem, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            sb.Append(XQueryResultSerializer.Serialize(item, store));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Evaluates an XQuery expression and returns each result item as a separate string.
    /// </summary>
    /// <param name="xquery">The XQuery expression to evaluate.</param>
    /// <param name="inputXml">
    /// Optional XML input. When provided, the parsed document is set as the XQuery context item
    /// (available as <c>.</c>) and also bound as <c>$input</c> for backward compatibility.
    /// The document is also accessible via <c>doc('urn:xqueryfacade:input')</c>.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the evaluation.</param>
    /// <returns>A list of serialized result strings, one per result item.</returns>
    public async Task<IReadOnlyList<string>> EvaluateAllAsync(string xquery, string? inputXml = null, CancellationToken cancellationToken = default)
    {
        var (engine, store, wrappedQuery, contextItem) = SetUp(xquery, inputXml);

        var results = new List<string>();
        await foreach (var item in engine.ExecuteAsync(wrappedQuery, initialContextItem: contextItem, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            results.Add(XQueryResultSerializer.Serialize(item, store));
        }
        return results;
    }

    /// <summary>
    /// Evaluates an XQuery expression and returns the first result as a string, or <c>null</c> if empty.
    /// </summary>
    /// <param name="xquery">The XQuery expression to evaluate.</param>
    /// <param name="inputXml">
    /// Optional XML input. When provided, the parsed document is set as the XQuery context item
    /// (available as <c>.</c>) and also bound as <c>$input</c> for backward compatibility.
    /// The document is also accessible via <c>doc('urn:xqueryfacade:input')</c>.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the evaluation.</param>
    /// <returns>The first result item serialized as a string, or <c>null</c> if the result is the empty sequence.</returns>
    public async Task<string?> EvaluateScalarAsync(string xquery, string? inputXml = null, CancellationToken cancellationToken = default)
    {
        var (engine, store, wrappedQuery, contextItem) = SetUp(xquery, inputXml);

        await foreach (var item in engine.ExecuteAsync(wrappedQuery, initialContextItem: contextItem, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            return XQueryResultSerializer.Serialize(item, store);
        }
        return null;
    }

    private static (QueryEngine Engine, XdmDocumentStore Store, string Query, object? ContextItem) SetUp(string xquery, string? inputXml)
    {
        var store = new XdmDocumentStore();
        object? contextItem = null;

        if (inputXml != null)
        {
            var doc = store.LoadFromString(inputXml, InputDocumentUri);
            contextItem = doc;
        }

        var engine = new QueryEngine(
            nodeProvider: store,
            documentResolver: store);

        // When input XML is provided, the document is available three ways:
        // 1. As the context item (.) — the standard XQuery mechanism
        // 2. As $input variable — backward compatibility
        // 3. Via doc('urn:xqueryfacade:input') — explicit URI access
        var effectiveQuery = inputXml != null
            ? $"let $input := doc('{InputDocumentUri}') return ({xquery})"
            : xquery;

        return (engine, store, effectiveQuery, contextItem);
    }
}
