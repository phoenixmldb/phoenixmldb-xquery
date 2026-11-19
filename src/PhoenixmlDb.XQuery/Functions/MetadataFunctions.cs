using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Analysis;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// phx:metadata($node as node()?, $key as (xs:string | xs:QName)) as item()*
/// Returns the values of one metadata key of the document that contains the node: each value at its stored type, in
/// stored order (one for a scalar key, all of them for a set). The empty sequence when the document has no value for
/// the key, when $node is empty, or when no metadata provider is configured.
/// </summary>
/// <remarks>
/// <para>The key is resolved here, once, before the host's <see cref="IMetadataProvider"/> sees it
/// (namespace-consolidation design §3.4):</para>
/// <list type="bullet">
/// <item><c>status</c> (unprefixed) and <c>Q{uri}status</c> are passed through; the host applies its default
/// metadata namespace to an unprefixed key.</item>
/// <item>The Clark form <c>{uri}status</c> (the CLI's) becomes <c>Q{uri}status</c>; <c>{}status</c> becomes
/// <c>Q{}status</c>.</item>
/// <item>An <c>xs:QName</c> key becomes <c>Q{uri}local</c> from its expanded name (<c>Q{}local</c> when it has no
/// namespace).</item>
/// <item><c>dbxml:size</c> becomes <c>Q{https://schemas.phoenixml.dev/2026/meta}size</c>, always, whatever the query
/// binds <c>dbxml</c> to.</item>
/// <item><c>p:status</c> resolves <c>p</c> in the query's statically known namespaces: its prolog, then the host's
/// bindings, then the predeclared prefixes. An unbound prefix raises <c>FONS0004</c>.</item>
/// <item>A string that is not a lexical QName is passed through unchanged.</item>
/// </list>
/// <para>So a provider never receives a prefix. The arguments follow the function conversion rules: a node given as
/// <c>$key</c> is atomized; a <c>$node</c> that is not a node, or a <c>$key</c> that is neither a string nor a QName,
/// is XPTY0004 (phoenixml#132; they gave the empty sequence and the node object's text).</para>
/// </remarks>
public sealed class MetadataGetFunction : XQueryFunction
{
    private static readonly string MetaUri = NamespaceRegistry.GetUri(NamespaceId.PhoenixmlMeta)!;

    public override QName Name => new(FunctionNamespaces.Phx, "metadata", "phx");

    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;

    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "node"), Type = XdmSequenceType.OptionalNode },
        new() { Name = new QName(NamespaceId.None, "key"), Type = new XdmSequenceType { ItemType = ItemType.AnyAtomicType, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var node = RequireOptionalNode(arguments[0], context);
        // Resolve first, so a bad key or an unbound prefix raises its error whether or not a provider is present.
        var key = KeyOf(arguments[1], context);

        if (node is null || context is not QueryExecutionContext { MetadataProvider: { } provider })
            return ValueTask.FromResult<object?>(null);

        return ValueTask.FromResult(ToSequence(provider.GetMetadata(node.Document, key)));
    }

    /// <summary>$node is <c>node()?</c>: a node, or empty; anything else is XPTY0004.</summary>
    internal static XdmNode? RequireOptionalNode(object? argument, Ast.ExecutionContext context) => argument switch
    {
        null => null,
        object?[] { Length: 0 } => null,
        XdmNode node => node,
        object?[] { Length: 1 } one when one[0] is XdmNode node => node,
        _ => throw context.Error("XPTY0004",
            $"phx:metadata: $node must be a node, got {XdmShape.TypeNameOf(argument is object?[] { Length: 1 } a ? a[0] : argument)}"),
    };

    /// <summary>The key as the provider receives it, from an <c>xs:string</c> or <c>xs:QName</c> argument.</summary>
    private static string KeyOf(object? argument, Ast.ExecutionContext context)
    {
        var value = argument is object?[] { Length: 1 } one ? one[0] : argument;
        if (value is XdmNode node)
            value = QueryExecutionContext.AtomizeTyped(node, (context as QueryExecutionContext)?.NodeProvider);
        switch (value)
        {
            case string s:
                return ResolveKey(s, context);
            case Xdm.XsUntypedAtomic u:
                return ResolveKey(u.Value, context);
            case Xdm.XsAnyUri uri:
                return ResolveKey(uri.Value, context);
            case QName qn:
            {
                var uriText = qn.ResolvedNamespace
                    ?? NamespaceUriFunction.ResolveNsId(qn.Namespace, context as QueryExecutionContext);
                return $"Q{{{uriText}}}{qn.LocalName}";
            }
            default:
                throw context.Error("XPTY0004",
                    $"phx:metadata: $key must be an xs:string or an xs:QName, got {XdmShape.TypeNameOf(value)}");
        }
    }

    /// <summary>Resolves a metadata key to an unprefixed local name or <c>Q{uri}local</c>. See the class remarks.</summary>
    internal static string ResolveKey(string key, Ast.ExecutionContext context)
    {
        if (key.StartsWith("Q{", StringComparison.Ordinal))
            return key;
        // Clark notation {uri}local: the expanded name, as Q{uri}local.
        if (key.StartsWith('{'))
        {
            var close = key.IndexOf('}', StringComparison.Ordinal);
            return close > 0 && IsNCName(key[(close + 1)..]) ? "Q" + key : key;
        }
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

    /// <summary>A provider's values as a sequence: empty, one item, or several in order.</summary>
    internal static object? ToSequence(IReadOnlyList<Xdm.XdmValue>? values)
    {
        if (values is null || values.Count == 0)
            return null;
        if (values.Count == 1)
            return ToItem(values[0]);
        var items = new object?[values.Count];
        for (var i = 0; i < items.Length; i++)
            items[i] = ToItem(values[i]);
        return items;
    }

    /// <summary>One stored value as the engine's item of the same type.</summary>
    internal static object? ToItem(Xdm.XdmValue value)
    {
        if (value.IsEmpty)
            return null;
        return value.Type switch
        {
            Xdm.XdmType.UntypedAtomic => new Xdm.XsUntypedAtomic(value.AsString()),
            Xdm.XdmType.XsString => value.AsString(),
            Xdm.XdmType.XsInteger => value.RawValue is System.Numerics.BigInteger big ? (object)big : value.AsLong(),
            Xdm.XdmType.XsDecimal => value.AsDecimal(),
            Xdm.XdmType.XsDouble => value.AsDouble(),
            Xdm.XdmType.XsFloat => value.AsFloat(),
            Xdm.XdmType.Boolean => value.AsBoolean(),
            // The engine represents the binary types as the XdmValue itself.
            Xdm.XdmType.Base64Binary or Xdm.XdmType.HexBinary => value,
            Xdm.XdmType.AnyUri => new Xdm.XsAnyUri(value.AsString()),
            Xdm.XdmType.QName => value.AsQName() is var q ? new QName(q.Namespace, q.LocalName, q.Prefix) : null,
            Xdm.XdmType.Map or Xdm.XdmType.Array or Xdm.XdmType.Function or Xdm.XdmType.Notation =>
                throw new XQueryRuntimeException("XPTY0004", $"phx:metadata: a stored value of type {value.Type} is not supported"),
            // Derived string and numeric types, dates, times, durations and the g-types: cast from the lexical form,
            // which gives the value of that exact type.
            var t => TypeCastHelper.BuiltInTypedValue(XsdLocalName(t), value.AsString()) ?? value.AsString(),
        };
    }

    private static string XsdLocalName(Xdm.XdmType type) => type switch
    {
        Xdm.XdmType.NmToken => "NMTOKEN",
        Xdm.XdmType.Id => "ID",
        Xdm.XdmType.IdRef => "IDREF",
        Xdm.XdmType.Entity => "ENTITY",
        Xdm.XdmType.NCName => "NCName",
        Xdm.XdmType.XsLong => "long",
        Xdm.XdmType.XsInt => "int",
        Xdm.XdmType.XsShort => "short",
        Xdm.XdmType.Byte => "byte",
        Xdm.XdmType.DateTimeStamp => "dateTimeStamp",
        Xdm.XdmType.GYearMonth => "gYearMonth",
        Xdm.XdmType.GYear => "gYear",
        Xdm.XdmType.GMonthDay => "gMonthDay",
        Xdm.XdmType.GDay => "gDay",
        Xdm.XdmType.GMonth => "gMonth",
        var t => char.ToLowerInvariant(t.ToString()[0]) + t.ToString()[1..],
    };

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
/// phx:metadata($node as node()?) as map(xs:string, item()*)
/// Returns all metadata of the document that contains the node as a map from each key, as the provider names it, to
/// its values (each at its stored type, in stored order).
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
        new() { Name = new QName(NamespaceId.None, "node"), Type = XdmSequenceType.OptionalNode }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var node = MetadataGetFunction.RequireOptionalNode(arguments[0], context);
        var map = new OrderedXdmMap(XdmMapKeyComparer.Instance);
        if (node is not null && context is QueryExecutionContext { MetadataProvider: { } provider })
        {
            foreach (var (key, values) in provider.GetAllMetadata(node.Document))
                map[key] = MetadataGetFunction.ToSequence(values);
        }
        return ValueTask.FromResult<object?>(map);
    }
}
