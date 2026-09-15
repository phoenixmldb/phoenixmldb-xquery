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
///
/// // With base URI for relative document resolution
/// var xml = File.ReadAllText("data/catalog.xml");
/// var baseUri = new Uri(Path.GetFullPath("data/catalog.xml"));
/// string result4 = await xquery.EvaluateAsync(
///     "//item/doc(resolve-uri(@href, base-uri(.)))", xml, baseUri);
/// </code>
/// </example>
public sealed class XQueryFacade
{
    private const string InputDocumentUri = "urn:xqueryfacade:input";

    /// <summary>
    /// Optional resource security policy. When set, controls which URIs the query can access
    /// via <c>doc()</c>, <c>collection()</c>, <c>unparsed-text()</c>, etc.
    /// See <see cref="Security.ResourcePolicy.ServerDefault"/> for a secure server configuration.
    /// </summary>
    public Security.ResourcePolicy? ResourcePolicy { get; set; }

    /// <summary>
    /// Evaluates an XQuery expression and returns all results concatenated as a single string.
    /// </summary>
    /// <param name="xquery">The XQuery expression to evaluate. May include a full prolog.</param>
    /// <param name="inputXml">
    /// Optional XML input. When provided, the parsed document is set as the XQuery context item
    /// (available as <c>.</c>), bound as the external variable <c>$input</c>, and accessible via
    /// <c>doc('urn:xqueryfacade:input')</c>. The query is passed through unmodified.
    /// </param>
    /// <param name="baseUri">
    /// Optional base URI for the input document. Used as the document-uri and base-uri for the
    /// input XML, enabling <c>resolve-uri()</c> and <c>doc()</c> to resolve relative references.
    /// Pass the file URI when loading XML from disk (e.g., <c>new Uri(Path.GetFullPath("data.xml"))</c>).
    /// Also used as the query base URI if <paramref name="queryBaseUri"/> is not set.
    /// </param>
    /// <param name="queryBaseUri">
    /// Optional base URI for the XQuery source. Used for <c>fn:static-base-uri()</c> and for
    /// resolving relative <c>at</c> location hints in <c>import module</c> declarations.
    /// When the query is loaded from a file, pass its URI (e.g., <c>new Uri(Path.GetFullPath("query.xq"))</c>).
    /// </param>
    /// <param name="cancellationToken">Token to cancel the evaluation.</param>
    /// <returns>All result items serialized and concatenated, separated by the query's declared
    /// <c>output:item-separator</c> if it declares one. Returns an empty string if the result is the empty sequence.</returns>
    public async Task<string> EvaluateAsync(string xquery, string? inputXml = null, Uri? baseUri = null, Uri? queryBaseUri = null, CancellationToken cancellationToken = default)
    {
        var (store, context, plan, options) = SetUp(xquery, inputXml, baseUri, queryBaseUri, cancellationToken, ResourcePolicy);

        var sb = new StringBuilder();
        var first = true;
        await foreach (var item in plan.ExecuteAsync(context).ConfigureAwait(false))
        {
            // Each item is serialized on its own, so the serializer's separator never falls between
            // them; write a declared one here. Without a declaration items concatenate as before.
            if (!first && options.ItemSeparator != null)
                sb.Append(options.ItemSeparator);
            sb.Append(XQueryResultSerializer.Serialize(item, store, options));
            first = false;
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
    /// <param name="baseUri">Optional base URI for the input document, enabling relative URI resolution.</param>
    /// <param name="queryBaseUri">Optional base URI for the XQuery source, for module resolution and <c>fn:static-base-uri()</c>.</param>
    /// <param name="cancellationToken">Token to cancel the evaluation.</param>
    /// <returns>A list of serialized result strings, one per result item.</returns>
    public async Task<IReadOnlyList<string>> EvaluateAllAsync(string xquery, string? inputXml = null, Uri? baseUri = null, Uri? queryBaseUri = null, CancellationToken cancellationToken = default)
    {
        var (store, context, plan, options) = SetUp(xquery, inputXml, baseUri, queryBaseUri, cancellationToken, ResourcePolicy);

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
    /// <param name="baseUri">Optional base URI for the input document, enabling relative URI resolution.</param>
    /// <param name="queryBaseUri">Optional base URI for the XQuery source, for module resolution and <c>fn:static-base-uri()</c>.</param>
    /// <param name="cancellationToken">Token to cancel the evaluation.</param>
    /// <returns>The first result item serialized as a string, or <c>null</c> if the result is the empty sequence.</returns>
    public async Task<string?> EvaluateScalarAsync(string xquery, string? inputXml = null, Uri? baseUri = null, Uri? queryBaseUri = null, CancellationToken cancellationToken = default)
    {
        var (store, context, plan, options) = SetUp(xquery, inputXml, baseUri, queryBaseUri, cancellationToken, ResourcePolicy);

        await foreach (var item in plan.ExecuteAsync(context).ConfigureAwait(false))
        {
            return XQueryResultSerializer.Serialize(item, store, options);
        }
        return null;
    }

    private static (XdmDocumentStore Store, QueryExecutionContext Context, ExecutionPlan Plan, SerializationOptions Options) SetUp(
        string xquery, string? inputXml, Uri? baseUri, Uri? queryBaseUri, CancellationToken cancellationToken,
        Security.ResourcePolicy? resourcePolicy = null)
    {
        var store = new XdmDocumentStore();
        XdmDocument? doc = null;

        if (inputXml != null)
        {
            // Use the provided base URI as the document URI so that base-uri(),
            // resolve-uri(), and doc() can resolve relative references correctly.
            var documentUri = baseUri?.AbsoluteUri ?? InputDocumentUri;
            doc = store.LoadFromString(inputXml, documentUri);
        }

        IDocumentResolver documentResolver = store;
        if (resourcePolicy != null)
            documentResolver = new Security.PolicyEnforcingResolver(store, resourcePolicy);

        var engine = new QueryEngine(
            nodeProvider: store,
            documentResolver: documentResolver);

        // Compile the query — use queryBaseUri for module resolution and static-base-uri(),
        // falling back to baseUri for backward compatibility.
        var compileBaseUri = queryBaseUri?.AbsoluteUri ?? baseUri?.AbsoluteUri;
        var compilationResult = engine.Compile(xquery, new CompilationOptions { BaseUri = compileBaseUri });
        if (!compilationResult.Success)
        {
            var errorMessages = string.Join("; ", compilationResult.Errors.Select(e => e.Message));
            // Propagate the analyzer's own code instead of reporting every static failure as
            // XPST0003 (a syntax error). QueryEngine.Compile has always done this; this path and
            // the CLI hardcoded XPST0003, so an XQST0036 or XQST0059 arrived mislabelled as a
            // parse error — the analyzer had classified it correctly and the wrapper discarded it.
            var firstCode = compilationResult.Errors.Count > 0 ? compilationResult.Errors[0].Code : "XPST0003";
            throw new XQueryRuntimeException(firstCode, $"Compilation failed: {errorMessages}");
        }

        // Create context with the document as the initial context item.
        var context = engine.CreateContext(
            initialContextItem: doc,
            cancellationToken: cancellationToken,
            staticBaseUri: compilationResult.BaseUri);

        // Bind $input as an external variable for backward compatibility.
        // Users can access it via: declare variable $input external; $input//path
        if (doc != null)
        {
            context.SetExternalVariable("input", doc);
        }

        // Detect serialization options from the query prolog. A parameter document resolves against
        // the same base URI the query compiles with, and is read under the same resource policy as fn:doc.
        var options = DetectSerializationOptions(xquery, queryBaseUri ?? baseUri, resourcePolicy);

        return (store, context, compilationResult.ExecutionPlan!, options);
    }

    /// <summary>
    /// Reads the serialization options a query's prolog declares — <c>declare option
    /// output:method "adaptive"</c> and friends — without executing it.
    /// </summary>
    /// <remarks>
    /// Public because a caller that runs the plan ITSELF (rather than through
    /// <c>EvaluateAsync</c>) still needs the declared options to serialize the
    /// result the way the query asked. The QT3 runner is exactly that caller: it must supply
    /// an environment — context item, external variables, resources — so it cannot go through
    /// the facade, but <c>&lt;serialization-matches&gt;</c> matches its regex against output
    /// serialized under the declared method. The alternative was re-implementing prolog option
    /// parsing in the test harness, and a second implementation of an engine behaviour is
    /// precisely the shape of bug that has cost the most time here.
    /// </remarks>
    public static SerializationOptions DetectSerializationOptions(string xquery)
        => DetectSerializationOptions(xquery, staticBaseUri: null, resourcePolicy: null);

    /// <summary>
    /// Reads the serialization options a query's prolog declares, resolving a relative
    /// <c>output:parameter-document</c> against <paramref name="staticBaseUri"/>.
    /// </summary>
    public static SerializationOptions DetectSerializationOptions(string xquery, Uri? staticBaseUri)
        => DetectSerializationOptions(xquery, staticBaseUri, resourcePolicy: null);

    /// <summary>
    /// Reads the serialization options a query's prolog declares, using <paramref name="defaultMethod"/>
    /// when neither a declaration nor a parameter document names an output method.
    /// </summary>
    /// <remarks>
    /// The facade serializes an undeclared query with the adaptive method. XQuery 3.1's default output
    /// method is xml, and the QT3 suite assumes it: an attribute node in the result must raise SENR0001
    /// (K2-Serialization-1..4), and a carriage return in a string must be escaped (K2-Serialization-11).
    /// A caller cannot apply that default itself, because SerializationOptions does not record whether
    /// the method was declared.
    /// </remarks>
    public static SerializationOptions DetectSerializationOptions(string xquery, Uri? staticBaseUri, OutputMethod defaultMethod)
        => DetectSerializationOptions(xquery, staticBaseUri, resourcePolicy: null, defaultMethod);

    /// <remarks>
    /// The prolog's options are gathered into a serialization-parameter map — the parameter document's
    /// parameters, overridden by every explicit declaration whatever its position (QT3
    /// Serialization-xml-04) — and read by <see cref="XQueryResultSerializer.ParseSerializationOptions"/>,
    /// the reader fn:serialize uses. This method used to parse 11 options itself: it ignored
    /// output:parameter-document, and compared yes/no values with "yes", so " false " and "0" read as
    /// false (QT3 K2-Serialization-38, -39).
    /// </remarks>
    internal static SerializationOptions DetectSerializationOptions(
        string xquery, Uri? staticBaseUri, Security.ResourcePolicy? resourcePolicy,
        OutputMethod defaultMethod = OutputMethod.Adaptive)
    {
        var declared = new Dictionary<object, object?>();
        string? parameterDocument = null;
        var names = PrologNameContext.Read(xquery);

        // Match: declare option output:OPTIONNAME "value"; or Q{...}OPTIONNAME "value";
        var optionPattern = @"declare\s+option\s+(?:output:(\w[\w-]*)|Q\{[^}]*\}(\w[\w-]*))\s+[""']([^""']*)[""']";
        foreach (Match match in Regex.Matches(xquery, optionPattern, RegexOptions.IgnoreCase))
        {
            var optionName = (match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value).ToLowerInvariant();
            var optionValue = match.Groups[3].Value;
            if (optionName == "parameter-document")
                parameterDocument = optionValue.Trim();
            else if (PrologParameterValue(optionName, optionValue, names) is { } value)
                declared[optionName] = value;
        }

        var parameters = parameterDocument != null
            ? LoadParameterDocument(parameterDocument, staticBaseUri, resourcePolicy)
            : new Dictionary<object, object?>();
        foreach (var (name, value) in declared)
            parameters[name] = value;
        if (defaultMethod != OutputMethod.Adaptive && !parameters.ContainsKey("method"))
            parameters["method"] = defaultMethod.ToString().ToLowerInvariant();

        return XQueryResultSerializer.ParseSerializationOptions(parameters, paramsFromMap: false);
    }

    private static readonly HashSet<string> YesNoParameters = new(StringComparer.Ordinal)
    {
        "indent", "omit-xml-declaration", "byte-order-mark", "undeclare-prefixes",
        "include-content-type", "escape-uri-attributes", "allow-duplicate-names",
    };

    /// <summary>
    /// A prolog option value in the shape the parameter-element reader produces: yes/no parameters
    /// accept xs:boolean's lexical forms as well as yes and no; element-name lists are split and each
    /// name expanded; html-version is a number; method may be written as an EQName. Null drops a value
    /// that has no meaning.
    /// </summary>
    private static object? PrologParameterValue(string name, string value, PrologNameContext names)
    {
        var trimmed = value.Trim();
        if (YesNoParameters.Contains(name))
            return trimmed.ToLowerInvariant() switch
            {
                "yes" or "true" or "1" => "yes",
                "no" or "false" or "0" => "no",
                _ => trimmed,
            };
        return name switch
        {
            "standalone" => trimmed.ToLowerInvariant() switch
            {
                "yes" or "true" or "1" => "yes",
                "no" or "false" or "0" => "no",
                var other => other,
            },
            // QT3 K2-Serialization-29: method " Q{}xml&#x9;" is the no-namespace name xml.
            "method" => trimmed.StartsWith("Q{}", StringComparison.Ordinal) ? trimmed[3..] : trimmed,
            "cdata-section-elements" or "suppress-indentation"
                => trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(names.Expand).ToList(),
            "html-version" => double.TryParse(trimmed, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var hv) ? hv : null,
            _ => value,
        };
    }

    /// <summary>
    /// The prolog's namespace declarations and default element namespace, for expanding the element
    /// names in cdata-section-elements and suppress-indentation. The serializer matches an element by its
    /// expanded name, <c>Q{uri}local</c>, or by a bare local name when it is in no namespace; a raw
    /// <c>p:b</c> matched nothing, so a prefixed name never took effect (QT3 K2-Serialization-29, -30,
    /// Serialization-html-18, -19a, -19b, Serialization-xhtml-18, -19a, -19b, -19c), and an unprefixed
    /// name ignored the default element namespace (K2-Serialization-31).
    /// </summary>
    private sealed class PrologNameContext
    {
        private static readonly Dictionary<string, string> Predeclared = new(StringComparer.Ordinal)
        {
            ["xml"] = "http://www.w3.org/XML/1998/namespace",
            ["xs"] = "http://www.w3.org/2001/XMLSchema",
            ["xsi"] = "http://www.w3.org/2001/XMLSchema-instance",
            ["fn"] = "http://www.w3.org/2005/xpath-functions",
            ["math"] = "http://www.w3.org/2005/xpath-functions/math",
            ["map"] = "http://www.w3.org/2005/xpath-functions/map",
            ["array"] = "http://www.w3.org/2005/xpath-functions/array",
            ["local"] = "http://www.w3.org/2005/xquery-local-functions",
            ["err"] = "http://www.w3.org/2005/xqt-errors",
            ["output"] = "http://www.w3.org/2010/xslt-xquery-serialization",
        };

        private readonly Dictionary<string, string> _prefixes;
        private readonly string _defaultElementNamespace;

        private PrologNameContext(Dictionary<string, string> prefixes, string defaultElementNamespace)
        {
            _prefixes = prefixes;
            _defaultElementNamespace = defaultElementNamespace;
        }

        public static PrologNameContext Read(string xquery)
        {
            var prefixes = new Dictionary<string, string>(Predeclared, StringComparer.Ordinal);
            foreach (Match m in Regex.Matches(xquery, @"declare\s+namespace\s+([\w.-]+)\s*=\s*[""']([^""']*)[""']"))
                prefixes[m.Groups[1].Value] = m.Groups[2].Value;
            var defaultElement = Regex.Match(xquery, @"declare\s+default\s+element\s+namespace\s+[""']([^""']*)[""']");
            return new PrologNameContext(prefixes, defaultElement.Success ? defaultElement.Groups[1].Value : "");
        }

        /// <summary>A name as the serializer matches it: <c>Q{uri}local</c>, or a bare local name in no namespace.</summary>
        public string Expand(string token)
        {
            if (token.StartsWith("Q{", StringComparison.Ordinal))
            {
                var close = token.IndexOf('}', StringComparison.Ordinal);
                if (close < 0) return token;
                var uri = token[2..close];
                return uri.Length == 0 ? token[(close + 1)..] : token;
            }
            var colon = token.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0)
            {
                // An unbound prefix is kept as written, as the parameter-document reader does.
                if (!_prefixes.TryGetValue(token[..colon], out var prefixUri)) return token;
                var local = token[(colon + 1)..];
                return prefixUri.Length == 0 ? local : $"Q{{{prefixUri}}}{local}";
            }
            return _defaultElementNamespace.Length == 0 ? token : $"Q{{{_defaultElementNamespace}}}{token}";
        }
    }

    /// <summary>
    /// Reads an <c>output:parameter-document</c> into a serialization-parameter map. A document that
    /// cannot be located, read, permitted or recognised is XQST0119.
    /// </summary>
    private static Dictionary<object, object?> LoadParameterDocument(
        string location, Uri? staticBaseUri, Security.ResourcePolicy? resourcePolicy)
    {
        const string SerializationNamespace = "http://www.w3.org/2010/xslt-xquery-serialization";
        Uri resolved;
        try
        {
            if (Uri.TryCreate(location, UriKind.Absolute, out var absolute))
                resolved = absolute;
            else if (staticBaseUri != null)
                resolved = new Uri(staticBaseUri, location);
            else
                throw new XQueryRuntimeException("XQST0119",
                    $"output:parameter-document '{location}' is relative and the query has no static base URI to resolve it against");
        }
        catch (UriFormatException ex)
        {
            throw new XQueryRuntimeException("XQST0119", $"output:parameter-document '{location}' is not a valid URI: {ex.Message}");
        }

        // The same check fn:doc gets from PolicyEnforcingResolver: a parameter document is a document the
        // query asks to read.
        if (resourcePolicy != null && !resourcePolicy.IsAllowed(resolved, Security.ResourceAccessKind.ReadDocument))
            throw new XQueryRuntimeException("XQST0119",
                $"output:parameter-document '{resolved}' is not allowed by the resource policy");
        if (!resolved.IsFile)
            throw new XQueryRuntimeException("XQST0119",
                $"output:parameter-document '{resolved}' is not a local file");

        var store = new XdmDocumentStore();
        XdmDocument document;
        try
        {
            document = store.LoadFile(resolved.LocalPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException or ArgumentException)
        {
            throw new XQueryRuntimeException("XQST0119", $"Cannot read output:parameter-document '{resolved}': {ex.Message}");
        }

        if (document.DocumentElement is not { } rootId
            || store.GetNode(rootId) is not PhoenixmlDb.Xdm.Nodes.XdmElement root
            || root.LocalName != "serialization-parameters"
            || store.ResolveNamespaceUri(root.Namespace)?.ToString() != SerializationNamespace)
            throw new XQueryRuntimeException("XQST0119",
                $"output:parameter-document '{resolved}' is not an output:serialization-parameters document");

        return new Dictionary<object, object?>(XQueryResultSerializer.ParseSerializationParamsElement(root, store));
    }
}
