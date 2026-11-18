using System.Text.RegularExpressions;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.Security;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// A collection URI that names a local directory: the files in it, as Saxon reads them. The query
/// part holds <c>keyword=value</c> pairs separated by <c>;</c> (or <c>&amp;</c>):
/// <list type="bullet">
/// <item><c>select=</c> a file-name glob with <c>*</c> and <c>?</c> (default: every file);</item>
/// <item><c>match=</c> a regular expression that the whole file name must match;</item>
/// <item><c>recurse=yes|no</c> (default no): the files of the subdirectories too;</item>
/// <item><c>on-error=fail|warning|ignore</c> (default fail): a file that cannot be parsed raises
/// the error, or is left out;</item>
/// <item><c>content-type=</c> a media type for every file, in place of the one its extension gives.</item>
/// </list>
/// In <c>collection()</c> an XML file gives its document node (as <c>doc()</c>), a JSON file its map
/// or array (as <c>json-doc()</c>), a text file a string (as <c>unparsed-text()</c>) and any other
/// file an <c>xs:base64Binary</c>. <c>uri-collection()</c> gives the file URIs and reads nothing.
/// </summary>
/// <remarks>
/// <para>Under a resource policy the directory is authorised for <see cref="ResourceAccessKind.ReadCollection"/>
/// before anything is listed, so a policy without file access (<see cref="ResourcePolicy.ServerDefault"/>)
/// refuses the request without touching the file system. A host resolver that supplies all content
/// is the only source of resources: no directory is listed.</para>
/// <para>Every file is then judged on its own. Its canonical path (links resolved) must lie inside the
/// canonical directory, so neither <c>..</c> nor a link leads out of it; a linked subdirectory is not
/// descended into. The policy must allow reading it, and it is read through <c>doc()</c>,
/// <c>json-doc()</c> or <c>unparsed-text()</c>, which apply their own checks and load budgets. A file
/// the policy refuses is left out. <c>on-error</c> never hides a refusal or an exhausted budget.</para>
/// <para>Files are given in ordinal order of their path, so the order is the same on every run.</para>
/// <para>Only the engine's own <see cref="XdmDocumentStore"/> lists directories. A host that resolves
/// documents with a resolver of its own keeps deciding what its collection URIs mean.</para>
/// </remarks>
internal static class DirectoryCollection
{
    private enum OnError { Fail, Warning, Ignore }

    private enum FileKind { Xml, Json, Text, Binary }

    private static readonly HashSet<string> XmlExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".xml", ".xsl", ".xslt", ".xsd", ".xhtml", ".svg", ".rdf", ".sch", ".xspec" };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".txt", ".text", ".csv", ".tsv", ".md" };

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
        { "select", "match", "recurse", "on-error", "content-type" };

    /// <summary>
    /// The items of the directory collection <paramref name="uri"/> names, or null when it names no
    /// local directory (the caller then resolves it as before).
    /// </summary>
    internal static async ValueTask<List<object?>?> TryResolveAsync(string uri, Ast.ExecutionContext context, bool urisOnly)
    {
        // Only the engine's own store lists a directory, directly or behind the policy. A host
        // with a resolver of its own (the XSLT engine, a database) decides what its collection
        // URIs mean, as it always has: the XSLT engine reads only the XML files of a directory.
        if (context is not QueryExecutionContext queryContext || EngineStore(queryContext) is null)
            return null;

        var queryIdx = uri.IndexOf('?', StringComparison.Ordinal);
        var location = queryIdx >= 0 ? uri[..queryIdx] : uri;
        var parameters = queryIdx >= 0 ? ParseQuery(uri[(queryIdx + 1)..]) : null;
        // A query part of other keywords is not ours: such a URI may name something a host serves.
        if (parameters is null && queryIdx >= 0)
            return null;

        // A resolver that is the only source of resources decides what exists: nothing is listed.
        if (context.ResourcePolicy?.ResourceResolver is { SuppliesAllContent: true })
            return null;

        if (location.Length == 0)
            location = ".";
        var absolute = LocationResolver.Absolute(location, queryContext.StaticBaseUri);
        if (absolute is null || LocationResolver.IsRelativeName(absolute))
        {
            // A relative directory with no base URI names nothing (xquery#374). Only a URI that
            // asks for a directory listing is reported here; any other is left to the resolver.
            if (parameters is not null)
                throw new XQueryRuntimeException("FODC0002", LocationResolver.NoBaseUri(location));
            return null;
        }
        if (!Uri.TryCreate(absolute, UriKind.Absolute, out var directoryUri) || !directoryUri.IsFile)
            return null;

        // Authorised BEFORE the file system is looked at: a policy without file access refuses the
        // request here, and nothing is listed or even tested for existence.
        string directory;
        if (context.ResourcePolicy is { } policy)
        {
            Uri authorized;
            try
            {
                authorized = policy.Authorize(directoryUri.AbsoluteUri, ResourceAccessKind.ReadCollection);
            }
            catch (ResourceAccessDeniedException e)
            {
                throw new XQueryRuntimeException("FODC0002", e.Message, e);
            }
            directory = authorized.LocalPath;
        }
        else
        {
            directory = ResourcePolicy.CanonicalPath(directoryUri.LocalPath);
        }

        if (!Directory.Exists(directory))
        {
            if (parameters is not null)
                throw new XQueryRuntimeException("FODC0002", $"Collection '{uri}' not found: '{directory}' is not a directory");
            return null;
        }

        var options = Options.From(parameters, queryContext);
        var files = new List<string>();
        Collect(directory, options, files);
        files.Sort(StringComparer.Ordinal);

        var root = Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar;
        var items = new List<object?>(files.Count);
        foreach (var file in files)
        {
            // The file as it really is: a link to somewhere outside the directory is left out.
            var canonical = ResourcePolicy.CanonicalPath(file);
            if (!canonical.StartsWith(root, StringComparison.Ordinal))
                continue;
            var fileUri = new Uri(canonical).AbsoluteUri;
            var kind = options.Kind(canonical);
            if (context.ResourcePolicy is { } filePolicy
                && filePolicy.TryAuthorize(fileUri, kind == FileKind.Xml ? ResourceAccessKind.ReadDocument : ResourceAccessKind.ReadText) is null)
                continue;

            if (urisOnly)
            {
                items.Add(new XsAnyUri(fileUri));
                continue;
            }

            try
            {
                items.Add(await ReadAsync(fileUri, kind, queryContext).ConfigureAwait(false));
            }
            catch (Exception e) when (options.OnError != OnError.Fail && IsParseFailure(e))
            {
                if (options.OnError == OnError.Warning)
                    await Console.Error.WriteLineAsync($"Warning: collection '{uri}': '{fileUri}' left out: {e.Message}").ConfigureAwait(false);
            }
        }
        return items;
    }

    /// <summary>
    /// Whether the host registered a collection under <paramref name="uri"/>: that collection is
    /// served, not the directory of the same name.
    /// </summary>
    internal static bool IsRegistered(string uri, QueryExecutionContext context)
        => EngineStore(context) is { } store && store.TryResolveCollectionItems(uri, out _);

    /// <summary>The engine's document store, when it resolves documents, directly or behind the policy.</summary>
    private static XdmDocumentStore? EngineStore(QueryExecutionContext context)
        => context.DocumentResolver as XdmDocumentStore
            ?? (context.DocumentResolver as PolicyEnforcingResolver)?.Inner as XdmDocumentStore;

    /// <summary>The keyword=value pairs, or null when a keyword is not one of ours.</summary>
    private static Dictionary<string, string>? ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in query.Split([';', '&'], StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0 || !Keywords.Contains(part[..eq]))
                return null;
            result[part[..eq]] = Uri.UnescapeDataString(part[(eq + 1)..]);
        }
        return result;
    }

    private static void Collect(string directory, Options options, List<string> files)
    {
        foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
        {
            if (entry is DirectoryInfo sub)
            {
                // A linked directory is not descended into: it may lead out of the collection, or
                // back into it for ever.
                if (options.Recurse && sub.LinkTarget is null)
                    Collect(sub.FullName, options, files);
                continue;
            }
            if (options.Matches(entry.Name))
                files.Add(entry.FullName);
        }
    }

    private static async ValueTask<object?> ReadAsync(string fileUri, FileKind kind, QueryExecutionContext context)
    {
        object?[] args = [fileUri];
        switch (kind)
        {
            case FileKind.Xml:
                return await new DocFunction().InvokeAsync(args, context).ConfigureAwait(false);
            case FileKind.Json:
                return await new JsonDocFunction().InvokeAsync(args, context).ConfigureAwait(false);
            case FileKind.Text:
                return await new UnparsedTextFunction().InvokeAsync(args, context).ConfigureAwait(false);
            default:
                // Counted and checked as a text read, as unparsed-text() is, then read at the
                // canonical path it was authorised as.
                (context.DocumentResolver as PolicyEnforcingResolver)?.ResolveText(fileUri, null);
                var uri = new Uri(fileUri);
                var bytes = await AuthorizedFile.ReadAllBytesAsync(context.ResourcePolicy, uri, ResourceAccessKind.ReadText, fileUri)
                    .ConfigureAwait(false);
                return XdmValue.Base64Binary(bytes);
        }
    }

    /// <summary>
    /// An error reading or parsing one file, which on-error may hide; never a refusal by the
    /// policy or an exhausted load budget, which stop the collection whatever on-error says.
    /// </summary>
    private static bool IsParseFailure(Exception e)
    {
        for (var inner = e; inner is not null; inner = inner.InnerException)
            if (inner is ResourceAccessDeniedException)
                return false;
        return e is XQueryRuntimeException or System.Xml.XmlException or System.Text.Json.JsonException
            or IOException or InvalidOperationException;
    }

    private sealed class Options
    {
        private Regex? _select;
        private Regex? _match;
        private FileKind? _contentKind;

        public bool Recurse { get; private set; }

        public OnError OnError { get; private set; }

        public static Options From(Dictionary<string, string>? parameters, QueryExecutionContext context)
        {
            var options = new Options();
            if (parameters is null)
                return options;
            var timeout = context.Limits.RegexMatchTimeout ?? Regex.InfiniteMatchTimeout;
            if (parameters.TryGetValue("select", out var select))
                options._select = new Regex(
                    "^" + Regex.Escape(select).Replace("\\*", ".*", StringComparison.Ordinal).Replace("\\?", ".", StringComparison.Ordinal) + "$",
                    // Globs have no backreferences: the non-backtracking engine is linear in the name.
                    RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
            if (parameters.TryGetValue("match", out var match))
            {
                try
                {
                    options._match = new Regex("^(?:" + match + ")$", RegexOptions.CultureInvariant, timeout);
                }
                catch (ArgumentException e)
                {
                    throw new XQueryRuntimeException("FODC0004", $"Invalid match= pattern '{match}' in a collection URI: {e.Message}");
                }
            }
            if (parameters.TryGetValue("recurse", out var recurse))
                options.Recurse = recurse switch
                {
                    "yes" or "true" or "1" => true,
                    "no" or "false" or "0" => false,
                    _ => throw new XQueryRuntimeException("FODC0004", $"Invalid recurse= value '{recurse}' in a collection URI: yes or no"),
                };
            if (parameters.TryGetValue("on-error", out var onError))
                options.OnError = onError switch
                {
                    "fail" => OnError.Fail,
                    "warning" => OnError.Warning,
                    "ignore" => OnError.Ignore,
                    _ => throw new XQueryRuntimeException("FODC0004", $"Invalid on-error= value '{onError}' in a collection URI: fail, warning or ignore"),
                };
            if (parameters.TryGetValue("content-type", out var contentType))
                options._contentKind = KindOfMediaType(contentType);
            return options;
        }

        public bool Matches(string fileName)
            => (_select is null || _select.IsMatch(fileName)) && (_match is null || _match.IsMatch(fileName));

        public FileKind Kind(string path)
        {
            if (_contentKind is { } kind)
                return kind;
            var extension = Path.GetExtension(path);
            if (XmlExtensions.Contains(extension))
                return FileKind.Xml;
            if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
                return FileKind.Json;
            return TextExtensions.Contains(extension) ? FileKind.Text : FileKind.Binary;
        }

        private static FileKind KindOfMediaType(string mediaType)
        {
            var type = mediaType.Split(';')[0].Trim().ToLowerInvariant();
            if (type.EndsWith("/json", StringComparison.Ordinal) || type.EndsWith("+json", StringComparison.Ordinal))
                return FileKind.Json;
            if (type.EndsWith("/xml", StringComparison.Ordinal) || type.EndsWith("+xml", StringComparison.Ordinal))
                return FileKind.Xml;
            return type.StartsWith("text/", StringComparison.Ordinal) ? FileKind.Text : FileKind.Binary;
        }
    }
}
