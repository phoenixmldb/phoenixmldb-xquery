using System.Text;
using System.Text.RegularExpressions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

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
/// The user's XQuery is passed through unmodified — prolog declarations (namespaces, options,
/// variable declarations) work exactly as in any conformant XQuery processor. When input XML
/// is provided, it is available as:
/// <list type="bullet">
///   <item><description>The context item (<c>.</c>) — the standard XQuery mechanism</description></item>
///   <item><description><c>$input</c> — declare as <c>declare variable $input external;</c> in the prolog</description></item>
///   <item><description><c>doc('urn:xqueryfacade:input')</c> — explicit URI access</description></item>
/// </list>
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
/// // Works with full XQuery prolog
/// string result2 = await xquery.EvaluateAsync("""
///     declare namespace bk = "http://example.com/books";
///     declare variable $input external;
///     $input//bk:book/bk:title/text()
///     """, inputXml);
///
/// // Also available via doc()
/// string result3 = await xquery.EvaluateAsync(
///     "doc('urn:xqueryfacade:input')//book/title/text()", inputXml);
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
    /// <param name="xquery">The XQuery expression to evaluate. May include a full prolog.</param>
    /// <param name="inputXml">
    /// Optional XML input. When provided, the parsed document is set as the XQuery context item
    /// (available as <c>.</c>), bound as the external variable <c>$input</c>, and accessible via
    /// <c>doc('urn:xqueryfacade:input')</c>. The query is passed through unmodified.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the evaluation.</param>
    /// <returns>All result items serialized and concatenated. Returns an empty string if the result is the empty sequence.</returns>
    public async Task<string> EvaluateAsync(string xquery, string? inputXml = null, CancellationToken cancellationToken = default)
    {
        var (store, context, plan, options) = SetUp(xquery, inputXml, cancellationToken);

        var sb = new StringBuilder();
        await foreach (var item in plan.ExecuteAsync(context).ConfigureAwait(false))
        {
            sb.Append(XQueryResultSerializer.Serialize(item, store, options));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Evaluates an XQuery expression and returns each result item as a separate string.
    /// </summary>
    /// <param name="xquery">The XQuery expression to evaluate. May include a full prolog.</param>
    /// <param name="inputXml">
    /// Optional XML input. When provided, the parsed document is set as the XQuery context item
    /// (available as <c>.</c>), bound as the external variable <c>$input</c>, and accessible via
    /// <c>doc('urn:xqueryfacade:input')</c>. The query is passed through unmodified.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the evaluation.</param>
    /// <returns>A list of serialized result strings, one per result item.</returns>
    public async Task<IReadOnlyList<string>> EvaluateAllAsync(string xquery, string? inputXml = null, CancellationToken cancellationToken = default)
    {
        var (store, context, plan, options) = SetUp(xquery, inputXml, cancellationToken);

        var results = new List<string>();
        await foreach (var item in plan.ExecuteAsync(context).ConfigureAwait(false))
        {
            results.Add(XQueryResultSerializer.Serialize(item, store, options));
        }
        return results;
    }

    /// <summary>
    /// Evaluates an XQuery expression and returns the first result as a string, or <c>null</c> if empty.
    /// </summary>
    /// <param name="xquery">The XQuery expression to evaluate. May include a full prolog.</param>
    /// <param name="inputXml">
    /// Optional XML input. When provided, the parsed document is set as the XQuery context item
    /// (available as <c>.</c>), bound as the external variable <c>$input</c>, and accessible via
    /// <c>doc('urn:xqueryfacade:input')</c>. The query is passed through unmodified.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the evaluation.</param>
    /// <returns>The first result item serialized as a string, or <c>null</c> if the result is the empty sequence.</returns>
    public async Task<string?> EvaluateScalarAsync(string xquery, string? inputXml = null, CancellationToken cancellationToken = default)
    {
        var (store, context, plan, options) = SetUp(xquery, inputXml, cancellationToken);

        await foreach (var item in plan.ExecuteAsync(context).ConfigureAwait(false))
        {
            return XQueryResultSerializer.Serialize(item, store, options);
        }
        return null;
    }

    private static (XdmDocumentStore Store, QueryExecutionContext Context, ExecutionPlan Plan, SerializationOptions Options) SetUp(
        string xquery, string? inputXml, CancellationToken cancellationToken)
    {
        var store = new XdmDocumentStore();
        XdmDocument? doc = null;

        if (inputXml != null)
        {
            doc = store.LoadFromString(inputXml, InputDocumentUri);
        }

        var engine = new QueryEngine(
            nodeProvider: store,
            documentResolver: store);

        // Compile the query as-is — no wrapping, no rewriting.
        // Prolog declarations (namespaces, options, variables) work correctly.
        var compilationResult = engine.Compile(xquery);
        if (!compilationResult.Success)
        {
            var errorMessages = string.Join("; ", compilationResult.Errors.Select(e => e.Message));
            throw new XQueryRuntimeException("XPST0003", $"Compilation failed: {errorMessages}");
        }

        // Create context with the document as the initial context item.
        var context = engine.CreateContext(initialContextItem: doc, cancellationToken: cancellationToken);

        // Bind $input as an external variable for backward compatibility.
        // Users can access it via: declare variable $input external; $input//path
        if (doc != null)
        {
            context.SetExternalVariable("input", doc);
        }

        // Detect serialization options from the query prolog.
        var options = DetectSerializationOptions(xquery);

        return (store, context, compilationResult.ExecutionPlan!, options);
    }

    private static SerializationOptions DetectSerializationOptions(string xquery)
    {
        var method = OutputMethod.Adaptive;
        var indent = false;
        var omitXmlDeclaration = false;
        string? encoding = null;
        string? standalone = null;

        // Match: declare option output:OPTIONNAME "value"; or Q{...}OPTIONNAME "value";
        var optionPattern = @"declare\s+option\s+(?:output:(\w[\w-]*)|Q\{[^}]*\}(\w[\w-]*))\s+[""']([^""']*)[""']";
        foreach (Match match in Regex.Matches(xquery, optionPattern, RegexOptions.IgnoreCase))
        {
            var optionName = (match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value).ToLowerInvariant();
            var optionValue = match.Groups[3].Value;

            switch (optionName)
            {
                case "method":
                    method = optionValue.ToLowerInvariant() switch
                    {
                        "json" => OutputMethod.Json,
                        "xml" => OutputMethod.Xml,
                        "text" => OutputMethod.Text,
                        "adaptive" => OutputMethod.Adaptive,
                        _ => OutputMethod.Adaptive
                    };
                    break;
                case "indent":
                    indent = optionValue.Equals("yes", StringComparison.OrdinalIgnoreCase);
                    break;
                case "omit-xml-declaration":
                    omitXmlDeclaration = optionValue.Equals("yes", StringComparison.OrdinalIgnoreCase);
                    break;
                case "encoding":
                    encoding = optionValue;
                    break;
                case "standalone":
                    standalone = optionValue.ToLowerInvariant() switch
                    {
                        "yes" or "no" or "omit" => optionValue.ToLowerInvariant(),
                        _ => null
                    };
                    break;
            }
        }

        return new SerializationOptions
        {
            Method = method,
            Indent = indent,
            OmitXmlDeclaration = omitXmlDeclaration,
            Encoding = encoding,
            Standalone = standalone
        };
    }
}
