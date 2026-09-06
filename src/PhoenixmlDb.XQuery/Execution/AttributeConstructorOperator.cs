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
/// Attribute constructor operator — creates an XdmAttribute node.
/// Used for both direct attribute constructors (name="value") and computed
/// attribute constructors (attribute name { value }).
/// </summary>
public sealed class AttributeConstructorOperator : PhysicalOperator
{
    public required QName Name { get; init; }
    public required PhysicalOperator ValueOperator { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        var store = context.NodeStore as INodeBuilder;

        // Evaluate value
        var sb = new StringBuilder();
        await foreach (var item in ValueOperator.ExecuteAsync(context))
        {
            if (item != null)
            {
                if (sb.Length > 0)
                    sb.Append(' ');
                var atomized = context.AtomizeWithNodes(item);
                sb.Append(Functions.ConcatFunction.XQueryStringValue(atomized));
            }
        }

        // xml:id attributes are always ID attributes per the xml:id specification.
        // Per the xml:id spec, the value is whitespace-normalized (leading/trailing stripped)
        // and must be a valid NCName to be recognized as an ID.
        var isXmlId = Name.LocalName == "id" &&
            (Name.ResolvedNamespace == "http://www.w3.org/XML/1998/namespace" ||
             Name.Prefix == "xml");
        var attrValue = sb.ToString();
        if (isXmlId)
        {
            attrValue = attrValue.Trim();
            if (!IsValidNCName(attrValue))
                isXmlId = false;
        }

        if (store != null)
        {
            var nsId = Name.ResolvedNamespace != null
                ? store.InternNamespace(Name.ResolvedNamespace, Name.Namespace)
                : Name.Namespace;

            var attr = new XdmAttribute
            {
                Id = store.AllocateId(),
                Document = new DocumentId(0),
                Namespace = nsId,
                LocalName = Name.LocalName,
                Prefix = Name.Prefix,
                Value = attrValue,
                IsId = isXmlId
            };
            store.RegisterNode(attr);
            yield return attr;
        }
        else
        {
            // Fallback: return a synthetic attribute node when no INodeBuilder is available
            yield return new XdmAttribute
            {
                Id = new NodeId(0),
                Document = new DocumentId(0),
                Namespace = NamespaceId.None,
                LocalName = Name.LocalName,
                Prefix = Name.Prefix,
                Value = attrValue,
                IsId = isXmlId
            };
        }
    }

    /// <summary>
    /// Checks if a string is a valid NCName per the XML Namespaces spec.
    /// NCName must start with a letter or underscore, followed by letters, digits,
    /// hyphens, underscores, or periods. No colons allowed.
    /// </summary>
    private static bool IsValidNCName(string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        // First character must be a NameStartChar (excluding ':')
        var ch = value[0];
        if (!IsNameStartChar(ch))
            return false;

        // Subsequent characters must be NameChars (excluding ':')
        for (var i = 1; i < value.Length; i++)
        {
            if (!IsNameChar(value[i]))
                return false;
        }

        return true;
    }

    private static bool IsNameStartChar(char ch) =>
        ch == '_' ||
        (ch >= 'A' && ch <= 'Z') ||
        (ch >= 'a' && ch <= 'z') ||
        (ch >= '\u00C0' && ch <= '\u00D6') ||
        (ch >= '\u00D8' && ch <= '\u00F6') ||
        (ch >= '\u00F8' && ch <= '\u02FF') ||
        (ch >= '\u0370' && ch <= '\u037D') ||
        (ch >= '\u037F' && ch <= '\u1FFF') ||
        (ch >= '\u200C' && ch <= '\u200D') ||
        (ch >= '\u2070' && ch <= '\u218F') ||
        (ch >= '\u2C00' && ch <= '\u2FEF') ||
        (ch >= '\u3001' && ch <= '\uD7FF') ||
        (ch >= '\uF900' && ch <= '\uFDCF') ||
        (ch >= '\uFDF0' && ch <= '\uFFFD');

    private static bool IsNameChar(char ch) =>
        IsNameStartChar(ch) ||
        ch == '-' || ch == '.' ||
        (ch >= '0' && ch <= '9') ||
        ch == '\u00B7' ||
        (ch >= '\u0300' && ch <= '\u036F') ||
        (ch >= '\u203F' && ch <= '\u2040');
}
