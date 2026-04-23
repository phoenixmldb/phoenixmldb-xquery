using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Path expression (e.g., //customer/name, /root/@id, $var/path).
/// </summary>
public sealed class PathExpression : XQueryExpression
{
    /// <summary>
    /// True if path starts with / (absolute path from document root).
    /// </summary>
    public bool IsAbsolute { get; init; }

    /// <summary>
    /// Optional initial expression for paths like $var/path where the path starts
    /// from a non-step expression (variable, function call, etc.).
    /// </summary>
    public XQueryExpression? InitialExpression { get; init; }

    /// <summary>
    /// The steps in the path.
    /// </summary>
    public required IReadOnlyList<StepExpression> Steps { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitPathExpression(this);

    public override string ToString()
    {
        var prefix = IsAbsolute ? "/" : "";
        var init = InitialExpression != null ? $"{InitialExpression}/" : "";
        return init + prefix + string.Join("/", Steps);
    }
}

/// <summary>
/// A single step in a path expression.
/// </summary>
public sealed class StepExpression : XQueryExpression
{
    /// <summary>
    /// The axis (child, descendant, attribute, etc.).
    /// </summary>
    public required Axis Axis { get; init; }

    /// <summary>
    /// The node test (name test or kind test).
    /// </summary>
    public required NodeTest NodeTest { get; init; }

    /// <summary>
    /// Predicates to filter results.
    /// </summary>
    public required IReadOnlyList<XQueryExpression> Predicates { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitStepExpression(this);

    public override string ToString()
    {
        var axis = Axis switch
        {
            Axis.Child => "",
            Axis.Attribute => "@",
            Axis.DescendantOrSelf => "descendant-or-self::",
            Axis.Descendant => "descendant::",
            Axis.Parent => "parent::",
            Axis.Ancestor => "ancestor::",
            Axis.AncestorOrSelf => "ancestor-or-self::",
            Axis.FollowingSibling => "following-sibling::",
            Axis.PrecedingSibling => "preceding-sibling::",
            Axis.Following => "following::",
            Axis.Preceding => "preceding::",
            Axis.Self => "self::",
            Axis.Namespace => "namespace::",
            _ => ""
        };
        var preds = Predicates.Count > 0
            ? string.Concat(Predicates.Select(p => $"[{p}]"))
            : "";
        return $"{axis}{NodeTest}{preds}";
    }
}

/// <summary>
/// XPath axis specifier.
/// </summary>
public enum Axis
{
    Child,
    Descendant,
    Attribute,
    Self,
    DescendantOrSelf,
    FollowingSibling,
    Following,
    Parent,
    Ancestor,
    PrecedingSibling,
    Preceding,
    AncestorOrSelf,
    Namespace
}

/// <summary>
/// Base for node tests in step expressions.
/// </summary>
public abstract class NodeTest
{
    /// <summary>
    /// Tests if a node matches this test.
    /// </summary>
    public abstract bool Matches(XdmNodeKind kind, NamespaceId? ns, string? localName);
}

/// <summary>
/// Tests node by name (e.g., customer, @id, ns:element).
/// </summary>
public sealed class NameTest : NodeTest
{
    /// <summary>
    /// Namespace URI (null for no namespace, "*" for any namespace).
    /// Set at parse time from prefix resolution, used to resolve NamespaceId at execution time.
    /// </summary>
    public string? NamespaceUri { get; set; }

    /// <summary>
    /// Resolved namespace ID (set during static analysis or at transformation time).
    /// </summary>
    public NamespaceId? ResolvedNamespace { get; internal set; }

    /// <summary>
    /// Local name ("*" for any local name).
    /// </summary>
    public required string LocalName { get; init; }

    /// <summary>
    /// Original prefix from query (for error messages).
    /// </summary>
    public string? Prefix { get; init; }

    /// <summary>
    /// True if this is a wildcard for local name (*).
    /// </summary>
    public bool IsLocalNameWildcard => LocalName == "*";

    /// <summary>
    /// True if this is a wildcard for namespace (*:name or *:*).
    /// </summary>
    public bool IsNamespaceWildcard => NamespaceUri == "*";

    public override bool Matches(XdmNodeKind kind, NamespaceId? ns, string? localName)
    {
        // Check local name
        if (!IsLocalNameWildcard && localName != LocalName)
            return false;

        // Check namespace
        if (IsNamespaceWildcard)
        {
            // *:NCName - any namespace matches
            return true;
        }

        if (ResolvedNamespace.HasValue)
        {
            // Resolved namespace - compare by ID
            return ns == ResolvedNamespace;
        }

        // No resolved namespace - check NamespaceUri string
        if (string.IsNullOrEmpty(NamespaceUri))
        {
            // Bare wildcard `*` matches any element regardless of namespace
            if (IsLocalNameWildcard)
                return true;
            // Unprefixed specific name: match only elements in the null namespace.
            // Per XSLT spec, without xpath-default-namespace, unprefixed element names
            // in patterns match elements in no namespace.
            return !ns.HasValue || ns.Value == NamespaceId.None;
        }

        // Pattern has explicit NamespaceUri but ResolvedNamespace wasn't set
        // This shouldn't happen if namespace resolution is working correctly
        // Return false as we can't verify the namespace
        return false;
    }

    /// <summary>
    /// Resolves the namespace URI to a NamespaceId using the provided resolver.
    /// </summary>
    public void ResolveNamespace(Func<string, NamespaceId> namespaceResolver)
    {
        if (!string.IsNullOrEmpty(NamespaceUri) && NamespaceUri != "*")
        {
            ResolvedNamespace = namespaceResolver(NamespaceUri);
        }
    }

    public override string ToString()
    {
        if (Prefix != null)
            return $"{Prefix}:{LocalName}";
        if (NamespaceUri == "*")
            return $"*:{LocalName}";
        return LocalName;
    }
}

/// <summary>
/// Tests node by kind (element(), text(), node(), etc.).
/// </summary>
public sealed class KindTest : NodeTest
{
    public required XdmNodeKind Kind { get; init; }

    /// <summary>
    /// Optional name for element(name) or attribute(name).
    /// </summary>
    public NameTest? Name { get; init; }

    /// <summary>
    /// Optional type for element(*, type) or attribute(*, type).
    /// </summary>
    public XdmTypeName? TypeName { get; init; }

    /// <summary>
    /// For document-node(element(E)): the element test that the document element must match.
    /// </summary>
    public NameTest? DocumentElementTest { get; init; }

    public override bool Matches(XdmNodeKind kind, NamespaceId? ns, string? localName)
    {
        // node() matches everything
        if (Kind == XdmNodeKind.None)
            return true;

        // Check kind
        if (kind != Kind)
            return false;

        // Check name if specified
        if (Name != null && !Name.Matches(kind, ns, localName))
            return false;

        return true;
    }

    public override string ToString()
    {
        var kindStr = Kind switch
        {
            XdmNodeKind.None => "node",
            XdmNodeKind.Document => "document-node",
            XdmNodeKind.Element => "element",
            XdmNodeKind.Attribute => "attribute",
            XdmNodeKind.Text => "text",
            XdmNodeKind.Comment => "comment",
            XdmNodeKind.ProcessingInstruction => "processing-instruction",
            XdmNodeKind.Namespace => "namespace-node",
            _ => "node"
        };

        if (Name != null)
            return $"{kindStr}({Name})";
        if (TypeName != null)
            return $"{kindStr}(*, {TypeName})";
        return $"{kindStr}()";
    }
}

/// <summary>
/// Represents a type name (for schema-aware processing).
/// </summary>
public sealed class XdmTypeName
{
    public string? NamespaceUri { get; init; }
    public required string LocalName { get; init; }
    public string? Prefix { get; init; }

    public override string ToString()
    {
        return Prefix != null ? $"{Prefix}:{LocalName}" : LocalName;
    }
}

/// <summary>
/// Tests node against a schema element declaration: <c>schema-element(Name)</c>.
/// Matches if the element has the declared name (or is in its substitution group)
/// and its type annotation is the declared type or a subtype.
/// Requires an <see cref="ISchemaProvider"/> to be registered.
/// </summary>
public sealed class SchemaElementTest : NodeTest
{
    /// <summary>The element declaration name from the schema.</summary>
    public required string LocalName { get; init; }

    /// <summary>Namespace prefix (for error messages).</summary>
    public string? Prefix { get; init; }

    /// <summary>Namespace URI (resolved from prefix).</summary>
    public string? NamespaceUri { get; init; }

    public override bool Matches(XdmNodeKind kind, NamespaceId? ns, string? localName)
    {
        // Runtime matching is handled by ISchemaProvider.MatchesSchemaElement();
        // this base method provides a structural name check only.
        return kind == XdmNodeKind.Element && localName == LocalName;
    }

    public override string ToString() => Prefix != null
        ? $"schema-element({Prefix}:{LocalName})"
        : $"schema-element({LocalName})";
}

/// <summary>
/// Tests node against a schema attribute declaration: <c>schema-attribute(Name)</c>.
/// Matches if the attribute has the declared name and its type annotation is
/// the declared type or a subtype.
/// Requires an <see cref="ISchemaProvider"/> to be registered.
/// </summary>
public sealed class SchemaAttributeTest : NodeTest
{
    /// <summary>The attribute declaration name from the schema.</summary>
    public required string LocalName { get; init; }

    /// <summary>Namespace prefix (for error messages).</summary>
    public string? Prefix { get; init; }

    /// <summary>Namespace URI (resolved from prefix).</summary>
    public string? NamespaceUri { get; init; }

    public override bool Matches(XdmNodeKind kind, NamespaceId? ns, string? localName)
    {
        // Runtime matching is handled by ISchemaProvider.MatchesSchemaAttribute();
        // this base method provides a structural name check only.
        return kind == XdmNodeKind.Attribute && localName == LocalName;
    }

    public override string ToString() => Prefix != null
        ? $"schema-attribute({Prefix}:{LocalName})"
        : $"schema-attribute({LocalName})";
}

/// <summary>
/// Validate expression: <c>validate [strict|lax|type T] { expr }</c>.
/// Validates the enclosed node tree against in-scope schema definitions.
/// Requires an <see cref="ISchemaProvider"/> to be registered.
/// </summary>
public sealed class ValidateExpression : XQueryExpression
{
    /// <summary>The validation mode (strict, lax, or type).</summary>
    public required ValidationMode Mode { get; init; }

    /// <summary>
    /// For <c>validate type T</c>, the target type name.
    /// Null for strict and lax modes.
    /// </summary>
    public XdmTypeName? TypeName { get; init; }

    /// <summary>The expression to validate.</summary>
    public required XQueryExpression Expression { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitValidateExpression(this);

    public override string ToString() => Mode switch
    {
        ValidationMode.Lax => $"validate lax {{ {Expression} }}",
        ValidationMode.Type => $"validate type {TypeName} {{ {Expression} }}",
        _ => $"validate {{ {Expression} }}"
    };
}

/// <summary>
/// Filter expression (primary expression with predicates).
/// </summary>
public sealed class FilterExpression : XQueryExpression
{
    /// <summary>
    /// The primary expression to filter.
    /// </summary>
    public required XQueryExpression Primary { get; init; }

    /// <summary>
    /// The predicates.
    /// </summary>
    public required IReadOnlyList<XQueryExpression> Predicates { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor)
        => visitor.VisitFilterExpression(this);

    public override string ToString()
    {
        var preds = string.Concat(Predicates.Select(p => $"[{p}]"));
        return $"{Primary}{preds}";
    }
}
