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
/// Computed attribute constructor operator — attribute { nameExpr } { valueExpr }.
/// The attribute name is computed at runtime from an expression.
/// </summary>
public sealed class ComputedAttributeConstructorOperator : PhysicalOperator
{
    public required PhysicalOperator NameOperator { get; init; }
    public required PhysicalOperator ValueOperator { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        // Evaluate name
        string localName = "";
        string? prefix = null;
        int nameCount = 0;

        string? expandedNs = null;
        object? firstName = null;
        await foreach (var nameResult in NameOperator.ExecuteAsync(context))
        {
            nameCount++;
            if (nameCount > 1)
                throw new XQueryRuntimeException("XPTY0004",
                    "Attribute name must be a single atomic value, got a sequence");
            firstName = nameResult;
        }
        if (nameCount == 0)
            throw new XQueryRuntimeException("XPTY0004",
                "Attribute name cannot be an empty sequence");

        if (firstName is QName qn)
        {
            localName = qn.LocalName;
            prefix = qn.Prefix;
            expandedNs = qn.ResolvedNamespace;
        }
        else
        {
            var nameVal = (context.AtomizeWithNodes(firstName)?.ToString() ?? "").Trim();
            // Handle EQName: Q{uri}local
            if (nameVal.StartsWith("Q{", StringComparison.Ordinal))
            {
                // Find the closing '}' that separates namespace URI from local name.
                // The URI may contain '}' via resolved character references, so search
                // from the end for the last '}' followed by a valid NCName start char.
                int closeBrace = -1;
                for (int i = nameVal.Length - 1; i >= 2; i--)
                {
                    if (nameVal[i] == '}' && i + 1 < nameVal.Length
                        && ComputedElementConstructorOperator.IsValidNCNameStart(nameVal[i + 1]))
                    {
                        closeBrace = i;
                        break;
                    }
                }
                if (closeBrace < 0)
                    closeBrace = nameVal.IndexOf('}', 2);
                if (closeBrace > 1)
                {
                    var rawUri = nameVal[2..closeBrace];
                    if (rawUri.Contains('{') || rawUri.Contains('}'))
                        throw new XQueryRuntimeException("XQDY0074",
                            $"'{nameVal}' is not a valid expanded QName for an attribute: namespace URI contains '{{' or '}}'");
                    expandedNs = ComputedElementConstructorOperator.CollapseWhitespace(rawUri);
                    localName = nameVal[(closeBrace + 1)..];
                }
                else localName = nameVal;
            }
            else if (nameVal.Contains(':'))
            {
                var parts = nameVal.Split(':', 2);
                prefix = parts[0];
                localName = parts[1];
                if (prefix == "xml")
                    expandedNs = "http://www.w3.org/XML/1998/namespace";
                else if (prefix == "xmlns")
                    throw new XQueryRuntimeException("XQDY0044",
                        "Computed attribute names with prefix 'xmlns' are not allowed");
                else if (context.PrefixNamespaceBindings?.TryGetValue(prefix, out var nsUri) == true)
                    expandedNs = nsUri;
                else if (ComputedElementConstructorOperator.DefaultPrefixBindings.TryGetValue(prefix, out var defaultNsUri))
                    expandedNs = defaultNsUri;
                else
                    throw new XQueryRuntimeException("XQDY0074",
                        $"Namespace prefix '{prefix}' has not been declared");
            }
            else
            {
                localName = nameVal;
            }
        }

        // XQDY0074: localName must be a valid NCName
        if (!IsValidNCName(localName))
            throw new XQueryRuntimeException("XQDY0074",
                $"'{localName}' is not a valid NCName for an attribute");
        // Empty/absent prefix is valid ("no prefix"); only validate a non-empty prefix.
        if (!string.IsNullOrEmpty(prefix) && !IsValidNCName(prefix))
            throw new XQueryRuntimeException("XQDY0074",
                $"'{prefix}' is not a valid NCName for a prefix");

        // XQDY0044: Computed attribute cannot have name 'xmlns' (with no namespace)
        if (localName == "xmlns" && string.IsNullOrEmpty(prefix))
            throw new XQueryRuntimeException("XQDY0044",
                "The computed attribute name 'xmlns' is not allowed");
        // XQDY0044: Computed attribute cannot have prefix 'xmlns'
        if (prefix == "xmlns")
            throw new XQueryRuntimeException("XQDY0044",
                "Computed attribute names with prefix 'xmlns' are not allowed");
        // XQDY0044: Cannot be in the xmlns namespace (from fn:QName runtime namespace)
        if (expandedNs == "http://www.w3.org/2000/xmlns/")
            throw new XQueryRuntimeException("XQDY0044",
                "Computed attribute name cannot be in the 'http://www.w3.org/2000/xmlns/' namespace");
        // Prefix 'xml' must map to the XML namespace, and vice versa
        if (prefix == "xml" && expandedNs != null && expandedNs != "http://www.w3.org/XML/1998/namespace")
            throw new XQueryRuntimeException("XQDY0044",
                "Prefix 'xml' must be bound to 'http://www.w3.org/XML/1998/namespace'");
        if (expandedNs == "http://www.w3.org/XML/1998/namespace")
        {
            // Auto-assign xml prefix when no prefix given, error if wrong prefix
            if (string.IsNullOrEmpty(prefix)) prefix = "xml";
            else if (prefix != "xml")
                throw new XQueryRuntimeException("XQDY0044",
                    "Only prefix 'xml' can be bound to 'http://www.w3.org/XML/1998/namespace'");
        }

        // Auto-generate a prefix when namespace URI is present but no prefix was given.
        // Attributes with a namespace URI MUST have a prefix (unlike elements which can use default NS).
        if (!string.IsNullOrEmpty(expandedNs) && string.IsNullOrEmpty(prefix)
            && expandedNs != "http://www.w3.org/XML/1998/namespace")
        {
            // Try to find an existing prefix for this namespace in scope
            if (context.PrefixNamespaceBindings != null)
            {
                foreach (var (p, ns) in context.PrefixNamespaceBindings)
                {
                    if (ns == expandedNs && !string.IsNullOrEmpty(p))
                    {
                        prefix = p;
                        break;
                    }
                }
            }
            // If no existing prefix found, generate one
            if (string.IsNullOrEmpty(prefix))
            {
                // Generate ns0, ns1, ns2... until we find an unused prefix
                for (int i = 0; ; i++)
                {
                    var candidate = $"ns{i}";
                    if (context.PrefixNamespaceBindings == null
                        || !context.PrefixNamespaceBindings.ContainsKey(candidate))
                    {
                        prefix = candidate;
                        break;
                    }
                }
            }
        }

        var name = new QName(NamespaceId.None, localName, prefix) { ExpandedNamespace = expandedNs };
        var delegateOp = new AttributeConstructorOperator
        {
            Name = name,
            ValueOperator = ValueOperator
        };

        await foreach (var result in delegateOp.ExecuteAsync(context))
        {
            yield return result;
        }
    }

    private static bool IsValidNCName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        var first = name[0];
        if (first != '_' && !char.IsLetter(first)) return false;
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
}
