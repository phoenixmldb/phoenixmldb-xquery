using System.Globalization;
using System.Text;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Analysis;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// phx:metadata($node as node(), $key as xs:string) as item()?
/// Retrieves a single metadata value for the document containing the given node.
/// </summary>
/// <remarks>
/// <para>The key is resolved here, once, before the host's <see cref="IMetadataProvider"/> sees it
/// (namespace-consolidation design §3.4):</para>
/// <list type="bullet">
/// <item><c>status</c> (unprefixed) and <c>Q{uri}status</c> are passed through; the host applies its default
/// metadata namespace to an unprefixed key.</item>
/// <item><c>dbxml:size</c> becomes <c>Q{https://schemas.phoenixml.dev/2026/meta}size</c>, always, whatever the query
/// binds <c>dbxml</c> to.</item>
/// <item><c>p:status</c> resolves <c>p</c> in the query's statically known namespaces: its prolog, then the host's
/// bindings, then the predeclared prefixes. An unbound prefix raises <c>FONS0004</c>.</item>
/// <item>A string that is not a lexical QName is passed through unchanged.</item>
/// </list>
/// <para>So a provider never receives a prefix. The literal <c>dbxml:</c> routing this replaced also existed in the
/// engine's provider, and made a prolog-declared prefix unusable in a key.</para>
/// <para><c>size</c> and <c>node-count</c> in the metadata namespace are returned as <c>xs:integer</c>; every other
/// value is returned as its string.</para>
/// </remarks>
public sealed class MetadataGetFunction : XQueryFunction
{
    private static readonly string MetaUri = NamespaceRegistry.GetUri(NamespaceId.PhoenixmlMeta)!;
    private static readonly string SizeKey = $"Q{{{MetaUri}}}size";
    private static readonly string NodeCountKey = $"Q{{{MetaUri}}}node-count";

    public override QName Name => new(FunctionNamespaces.Phx, "metadata", "phx");

    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalItem;

    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "node"), Type = XdmSequenceType.Node },
        new() { Name = new QName(NamespaceId.None, "key"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        if (arguments[0] is not XdmNode node)
            return ValueTask.FromResult<object?>(null);

        // Resolve first, so an unbound prefix raises FONS0004 whether or not a provider is present.
        var key = ResolveKey(arguments[1]?.ToString() ?? string.Empty, context);

        if (context is not QueryExecutionContext { MetadataProvider: { } provider })
            return ValueTask.FromResult<object?>(null);

        var rawValue = provider.GetMetadata(node.Document, key);
        if (rawValue is null)
            return ValueTask.FromResult<object?>(null);

        var text = Encoding.UTF8.GetString(rawValue);
        if ((key == SizeKey || key == NodeCountKey)
            && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            return ValueTask.FromResult<object?>(number);
        return ValueTask.FromResult<object?>(text);
    }

    /// <summary>Resolves a metadata key to an unprefixed local name or <c>Q{uri}local</c>. See the class remarks.</summary>
    internal static string ResolveKey(string key, Ast.ExecutionContext context)
    {
        if (key.StartsWith("Q{", StringComparison.Ordinal))
            return key;
        var colon = key.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0)
            return key;
        var prefix = key[..colon];
        var local = key[(colon + 1)..];
        if (!IsNCName(prefix) || !IsNCName(local))
            return key;
        if (prefix == "dbxml")
            return $"Q{{{MetaUri}}}{local}";

        string? uri = null;
        if (context is QueryExecutionContext { PrologNamespaceBindings: { } bindings })
            bindings.TryGetValue(prefix, out uri);
        uri ??= WellKnownNamespaces.PredeclaredUri(prefix);
        if (string.IsNullOrEmpty(uri))
            throw new XQueryRuntimeException("FONS0004",
                $"No namespace binding for prefix '{prefix}' in metadata key '{key}'");
        return $"Q{{{uri}}}{local}";
    }

    private static bool IsNCName(string value)
    {
        try
        {
            System.Xml.XmlConvert.VerifyNCName(value);
            return true;
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }
    }
}

/// <summary>
/// phx:metadata($node as node()) as map(xs:string, item()?)
/// Retrieves all metadata for the document containing the given node as a map, keyed as the provider returns them.
/// </summary>
public sealed class MetadataAllFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Phx, "metadata", "phx");

    public override XdmSequenceType ReturnType => new()
    {
        ItemType = ItemType.Map,
        Occurrence = Occurrence.ExactlyOne
    };

    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "node"), Type = XdmSequenceType.Node }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var node = arguments[0] as XdmNode;
        if (node is null)
            return ValueTask.FromResult<object?>(new OrderedXdmMap(XdmMapKeyComparer.Instance));

        var documentId = node.Document;

        if (context is QueryExecutionContext queryContext && queryContext.MetadataProvider is not null)
        {
            var entries = queryContext.MetadataProvider.GetAllMetadata(documentId);
            var map = new OrderedXdmMap(XdmMapKeyComparer.Instance);

            foreach (var (key, value) in entries)
            {
                map[key] = Encoding.UTF8.GetString(value);
            }

            return ValueTask.FromResult<object?>(map);
        }

        return ValueTask.FromResult<object?>(new OrderedXdmMap(XdmMapKeyComparer.Instance));
    }
}
