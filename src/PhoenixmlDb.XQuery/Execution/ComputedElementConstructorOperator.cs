using System.Numerics;
using System.Text;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.Xdm.Serialization;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Functions;
using PhoenixmlDb.XQuery.Optimizer;

namespace PhoenixmlDb.XQuery.Execution;

/// <summary>
/// Computed element constructor operator — element { nameExpr } { contentExpr }.
/// The element name is computed at runtime from an expression.
/// </summary>
public sealed class ComputedElementConstructorOperator : PhysicalOperator
{
    /// <summary>
    /// Default XQuery statically-known namespace prefixes (XQuery 3.1 §2.1.1).
    /// Used as fallback when PrefixNamespaceBindings is null (no prolog).
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> DefaultPrefixBindings =
        new Dictionary<string, string>
        {
            ["xml"] = "http://www.w3.org/XML/1998/namespace",
            ["xs"] = "http://www.w3.org/2001/XMLSchema",
            ["xsi"] = "http://www.w3.org/2001/XMLSchema-instance",
            ["fn"] = "http://www.w3.org/2005/xpath-functions",
            ["math"] = "http://www.w3.org/2005/xpath-functions/math",
            ["array"] = "http://www.w3.org/2005/xpath-functions/array",
            ["map"] = "http://www.w3.org/2005/xpath-functions/map",
            ["local"] = "http://www.w3.org/2005/xquery-local-functions"
        };

    public required PhysicalOperator NameOperator { get; init; }
    public required PhysicalOperator ContentOperator { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        // Evaluate name — preserve QName namespace from EQName expressions
        QName name;
        int nameCount = 0;
        object? firstName = null;
        await foreach (var nameResult in NameOperator.ExecuteAsync(context))
        {
            nameCount++;
            if (nameCount > 1)
                throw new XQueryRuntimeException("XPTY0004",
                    "Element name must be a single atomic value, got a sequence");
            firstName = nameResult;
        }
        if (nameCount == 0)
            throw new XQueryRuntimeException("XPTY0004",
                "Element name cannot be an empty sequence");

        if (firstName is QName qn)
        {
            // Per XQuery 3.1 §3.9.3.1: if the QName has no namespace and no prefix,
            // apply the default element namespace (from prolog or enclosing direct constructor).
            if (qn.ResolvedNamespace == null && string.IsNullOrEmpty(qn.Prefix))
            {
                string? defaultNs = null;
                if (context.PrefixNamespaceBindings != null)
                {
                    if (context.PrefixNamespaceBindings.TryGetValue("", out var enclosingDefaultNs)
                        && !string.IsNullOrEmpty(enclosingDefaultNs))
                        defaultNs = enclosingDefaultNs;
                    else if (context.PrefixNamespaceBindings.TryGetValue("##default-element", out var prologDefaultNs)
                        && !string.IsNullOrEmpty(prologDefaultNs))
                        defaultNs = prologDefaultNs;
                }
                if (defaultNs != null)
                    qn = new QName(qn.Namespace, qn.LocalName, qn.Prefix) { ExpandedNamespace = defaultNs };
            }
            name = qn;
        }
        else
        {
            var nameVal = (context.AtomizeWithNodes(firstName)?.ToString() ?? "").Trim();
            string localName;
            string? prefix = null;
            string? expandedNs = null;

            if (nameVal.StartsWith("Q{", StringComparison.Ordinal))
            {
                // Find the closing '}' that separates the namespace URI from the local name.
                // The namespace URI may itself contain '}' (from resolved character references),
                // so we search from the end: the last '}' followed by a valid NCName is the delimiter.
                int closeBrace = -1;
                for (int i = nameVal.Length - 1; i >= 2; i--)
                {
                    if (nameVal[i] == '}' && i + 1 < nameVal.Length && IsValidNCNameStart(nameVal[i + 1]))
                    {
                        closeBrace = i;
                        break;
                    }
                }
                // Fallback: if no '}' followed by NCName start found, try first '}' after Q{
                if (closeBrace < 0)
                    closeBrace = nameVal.IndexOf('}', 2);
                if (closeBrace > 1)
                {
                    var rawUri = nameVal[2..closeBrace];
                    // XQDY0074: runtime Q{uri}local strings cannot contain '{' or '}' in the URI.
                    // In source-level EQNames, these appear only via character references; in computed
                    // string values the grammar forbids them.
                    if (rawUri.Contains('{') || rawUri.Contains('}'))
                        throw new XQueryRuntimeException("XQDY0074",
                            $"'{nameVal}' is not a valid expanded QName: namespace URI contains '{{' or '}}'");
                    // Per XQuery 3.1 §3.9.3.1: whitespace in BracedURILiteral is
                    // normalized — leading/trailing trimmed, internal runs collapsed.
                    expandedNs = CollapseWhitespace(rawUri);
                    localName = nameVal[(closeBrace + 1)..];
                }
                else
                    localName = nameVal;
            }
            else if (nameVal.Contains(':'))
            {
                var parts = nameVal.Split(':', 2);
                prefix = parts[0];
                localName = parts[1];
                if (prefix == "xml")
                    expandedNs = "http://www.w3.org/XML/1998/namespace";
                else if (context.PrefixNamespaceBindings?.TryGetValue(prefix, out var nsUri) == true)
                    expandedNs = nsUri;
                else if (DefaultPrefixBindings.TryGetValue(prefix, out var defaultNsUri))
                    expandedNs = defaultNsUri;
                else
                    throw new XQueryRuntimeException("XQDY0074",
                        $"Namespace prefix '{prefix}' has not been declared");
            }
            else
            {
                localName = nameVal;
                // Per XQuery 3.1 §3.9.3.1: unprefixed computed element names use the
                // default element namespace. Check enclosing direct element constructor's
                // xmlns first, then fall back to prolog's declare default element namespace.
                if (context.PrefixNamespaceBindings != null)
                {
                    if (context.PrefixNamespaceBindings.TryGetValue("", out var enclosingDefaultNs)
                        && !string.IsNullOrEmpty(enclosingDefaultNs))
                    {
                        expandedNs = enclosingDefaultNs;
                    }
                    else if (context.PrefixNamespaceBindings.TryGetValue("##default-element", out var prologDefaultNs)
                        && !string.IsNullOrEmpty(prologDefaultNs))
                    {
                        expandedNs = prologDefaultNs;
                    }
                }
            }
            name = new QName(NamespaceId.None, localName, prefix) { ExpandedNamespace = expandedNs };
        }

        // XQDY0074: localName and prefix must be valid NCNames
        if (!IsValidNCName(name.LocalName))
            throw new XQueryRuntimeException("XQDY0074",
                $"'{name.LocalName}' is not a valid NCName for an element");
        // An empty/absent prefix is valid (it denotes "no prefix"); only a non-empty
        // prefix must be a valid NCName. (A no-prefix QName, e.g. xs:QName('att1'),
        // carries Prefix == "" — rejecting that wrongly broke computed construction.)
        if (!string.IsNullOrEmpty(name.Prefix) && !IsValidNCName(name.Prefix))
            throw new XQueryRuntimeException("XQDY0074",
                $"'{name.Prefix}' is not a valid NCName for a prefix");

        // XQDY0096: element name namespace checks (XQuery 3.1 §3.9.3.1)
        var resolvedNs = name.ResolvedNamespace;
        // Cannot be in the xmlns namespace
        if (resolvedNs == "http://www.w3.org/2000/xmlns/")
            throw new XQueryRuntimeException("XQDY0096",
                "Computed element name cannot be in the 'http://www.w3.org/2000/xmlns/' namespace");
        // Prefix 'xml' must map to the XML namespace, and vice versa
        if (name.Prefix == "xml" && resolvedNs != null && resolvedNs != "http://www.w3.org/XML/1998/namespace")
            throw new XQueryRuntimeException("XQDY0096",
                "Prefix 'xml' must be bound to 'http://www.w3.org/XML/1998/namespace'");
        if (resolvedNs == "http://www.w3.org/XML/1998/namespace")
        {
            // Auto-assign xml prefix when no prefix given, error if wrong prefix
            if (string.IsNullOrEmpty(name.Prefix))
                name = new QName(name.Namespace, name.LocalName, "xml") { ExpandedNamespace = name.ExpandedNamespace, RuntimeNamespace = name.RuntimeNamespace };
            else if (name.Prefix != "xml")
                throw new XQueryRuntimeException("XQDY0096",
                    "Only prefix 'xml' can be bound to 'http://www.w3.org/XML/1998/namespace'");
        }
        // Prefix 'xmlns' is always reserved
        if (name.Prefix == "xmlns")
            throw new XQueryRuntimeException("XQDY0096",
                "Prefix 'xmlns' cannot be used in a computed element constructor");

        var delegateOp = new ElementConstructorOperator
        {
            Name = name,
            AttributeOperators = Array.Empty<PhysicalOperator>(),
            ContentOperators = new[] { ContentOperator }
        };

        await foreach (var result in delegateOp.ExecuteAsync(context))
        {
            yield return result;
        }
    }

    internal static bool IsValidNCNameStart(char c) => c == '_' || char.IsLetter(c);

    private static bool IsValidNCName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (!IsValidNCNameStart(name[0])) return false;
        for (int i = 1; i < name.Length; i++)
        {
            var c = name[i];
            if (!char.IsLetterOrDigit(c) && c != '.' && c != '-' && c != '_'
                && char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark
                && char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.SpacingCombiningMark
                && char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.EnclosingMark)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Collapses whitespace in a namespace URI per the BracedURILiteral normalization rules:
    /// strip leading/trailing whitespace and collapse internal runs to a single space.
    /// </summary>
    internal static string CollapseWhitespace(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new System.Text.StringBuilder(s.Length);
        bool inWs = false;
        bool started = false;
        foreach (var ch in s)
        {
            if (ch is ' ' or '\t' or '\r' or '\n')
            {
                if (started) inWs = true;
                continue;
            }
            if (inWs) { sb.Append(' '); inWs = false; }
            sb.Append(ch);
            started = true;
        }
        return sb.ToString();
    }
}
