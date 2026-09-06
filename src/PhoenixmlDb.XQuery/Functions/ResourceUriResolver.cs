using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// Shared resource-URI resolution for the document-loading functions
/// (<c>fn:doc</c>, <c>fn:doc-available</c>, <c>fn:json-doc</c>).
/// </summary>
internal static class ResourceUriResolver
{
    /// <summary>
    /// If the context carries a resource-URI → local-file mapping (registered by the host,
    /// e.g. an environment that binds a logical http:// catalog URI to a file on disk) and
    /// <paramref name="uri"/> matches a registered entry, returns the backing local file path
    /// (as a <c>file://</c> URI). Otherwise returns <paramref name="uri"/> unchanged.
    /// </summary>
    public static string Map(QueryExecutionContext context, string uri)
    {
        var mappings = context.ResourceMappings;
        if (mappings != null && mappings.TryGetValue(uri, out var mappedPath) && File.Exists(mappedPath))
            return new Uri(Path.GetFullPath(mappedPath)).AbsoluteUri;
        return uri;
    }
}
