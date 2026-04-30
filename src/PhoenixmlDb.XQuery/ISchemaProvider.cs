using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery;

/// <summary>
/// Provides schema awareness to the XQuery and XSLT engines. Public extension point —
/// callers can substitute their own implementation (RelaxNG, Schematron-derived, in-memory,
/// dynamic) by passing it to the <see cref="Execution.QueryEngine"/> constructor.
/// </summary>
/// <remarks>
/// <para>
/// The default implementation, <see cref="XsdSchemaProvider"/>, ships in this package and
/// is registered automatically by <see cref="Execution.QueryEngine"/>. Together they provide
/// the XQuery 3.1 "Schema-Aware Conformance" features:
/// </para>
/// <list type="bullet">
///   <item><description><c>validate</c> expressions (<c>validate strict { ... }</c>)</description></item>
///   <item><description><c>schema-element()</c> and <c>schema-attribute()</c> type tests</description></item>
///   <item><description><c>import schema</c> prolog declarations</description></item>
///   <item><description>Typed node annotations (elements/attributes annotated with their XSD type)</description></item>
///   <item><description>Full XSD type hierarchy for <c>instance of</c> and <c>treat as</c></description></item>
/// </list>
/// <para>
/// Implement this interface to back schema processing with a non-XSD schema language, an
/// in-memory schema, or a custom resolution strategy. The engine calls in at compile time
/// (for static type checks) and at execution time (for validation and type annotation).
/// </para>
/// <para>
/// Passing <c>null</c> for <c>schemaProvider</c> opts out of schema features entirely —
/// every <c>schema-element/attribute</c> reference becomes XPST0008 and every <c>validate</c>
/// raises XQDY0027. This is rare; intended for size-constrained embedded scenarios.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Default — XsdSchemaProvider auto-registered, no schemas loaded yet.
/// var engine = new QueryEngine(nodeProvider: store, documentResolver: store);
///
/// // Pre-load schemas via the default provider:
/// var xsd = new XsdSchemaProvider("catalog.xsd");
/// var engine2 = new QueryEngine(nodeProvider: store, documentResolver: store, schemaProvider: xsd);
///
/// // Custom implementation:
/// var engine3 = new QueryEngine(nodeProvider: store, documentResolver: store, schemaProvider: new MyRelaxNgProvider());
/// </code>
/// </example>
/// <seealso cref="XsdSchemaProvider"/>
/// <seealso cref="Execution.QueryEngine"/>
/// <seealso cref="IDocumentResolver"/>
public interface ISchemaProvider
{
    // ──────────────────────────────────────────────
    //  Schema loading
    // ──────────────────────────────────────────────

    /// <summary>
    /// Imports a schema namespace, corresponding to the XQuery <c>import schema</c> declaration.
    /// </summary>
    /// <param name="targetNamespace">
    /// The target namespace of the schema to import. An empty string represents the no-namespace schema.
    /// </param>
    /// <param name="locationHints">
    /// Optional location hints (URIs) for the schema document(s), from the <c>at</c> clause.
    /// </param>
    /// <exception cref="SchemaException">
    /// Thrown if the schema cannot be loaded or contains errors (maps to XQST0059).
    /// </exception>
    void ImportSchema(string targetNamespace, IReadOnlyList<string>? locationHints = null);

    // ──────────────────────────────────────────────
    //  Type hierarchy
    // ──────────────────────────────────────────────

    /// <summary>
    /// Determines whether <paramref name="actualType"/> is a subtype of <paramref name="requiredType"/>
    /// in the XSD type derivation hierarchy.
    /// </summary>
    /// <remarks>
    /// This is the core type subsumption check used by <c>instance of</c>, <c>treat as</c>,
    /// and function argument coercion. The implementation must cover the full XSD 1.1 type
    /// hierarchy including built-in types, restriction, extension, union, and list derivation.
    /// </remarks>
    /// <param name="actualType">The type to check.</param>
    /// <param name="requiredType">The type to check against.</param>
    /// <returns><c>true</c> if <paramref name="actualType"/> is equal to or derived from <paramref name="requiredType"/>.</returns>
    bool IsSubtypeOf(XdmTypeName actualType, XdmTypeName requiredType);

    // ──────────────────────────────────────────────
    //  Element and attribute declarations
    // ──────────────────────────────────────────────

    /// <summary>
    /// Checks whether a global element declaration exists in the in-scope schema definitions,
    /// corresponding to the <c>schema-element(Name)</c> type test.
    /// </summary>
    /// <param name="name">The element QName to look up.</param>
    /// <returns><c>true</c> if the element is declared in a loaded schema.</returns>
    bool HasElementDeclaration(XdmQName name);

    /// <summary>
    /// Checks whether a global attribute declaration exists in the in-scope schema definitions,
    /// corresponding to the <c>schema-attribute(Name)</c> type test.
    /// </summary>
    /// <param name="name">The attribute QName to look up.</param>
    /// <returns><c>true</c> if the attribute is declared in a loaded schema.</returns>
    bool HasAttributeDeclaration(XdmQName name);

    /// <summary>
    /// Returns the declared type name for a global element declaration.
    /// </summary>
    /// <param name="name">The element QName.</param>
    /// <returns>The XSD type name, or <c>null</c> if the element is not declared.</returns>
    XdmTypeName? GetElementType(XdmQName name);

    /// <summary>
    /// Returns the declared type name for a global attribute declaration.
    /// </summary>
    /// <param name="name">The attribute QName.</param>
    /// <returns>The XSD type name, or <c>null</c> if the attribute is not declared.</returns>
    XdmTypeName? GetAttributeType(XdmQName name);

    /// <summary>
    /// Checks whether an element matches a <c>schema-element(Name)</c> test.
    /// An element matches if it has the same name as the declaration (or a name in its
    /// substitution group) and its type annotation is the declared type or a subtype.
    /// </summary>
    /// <param name="element">The element node to test.</param>
    /// <param name="declarationName">The element declaration name from the schema-element() test.</param>
    /// <returns><c>true</c> if the element matches the schema-element() test.</returns>
    bool MatchesSchemaElement(XdmElement element, XdmQName declarationName);

    /// <summary>
    /// Checks whether an attribute matches a <c>schema-attribute(Name)</c> test.
    /// An attribute matches if it has the declared name and its type annotation is
    /// the declared type or a subtype.
    /// </summary>
    /// <param name="attribute">The attribute node to test.</param>
    /// <param name="declarationName">The attribute declaration name from the schema-attribute() test.</param>
    /// <returns><c>true</c> if the attribute matches the schema-attribute() test.</returns>
    bool MatchesSchemaAttribute(XdmAttribute attribute, XdmQName declarationName);

    // ──────────────────────────────────────────────
    //  Validation
    // ──────────────────────────────────────────────

    /// <summary>
    /// Validates a node tree against the loaded schemas, corresponding to the
    /// <c>validate</c> expression.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Returns a deep copy of the input node tree with <see cref="XdmElement.TypeAnnotation"/>
    /// and <see cref="XdmAttribute.TypeAnnotation"/> populated from the schema. The original
    /// node tree is not modified.
    /// </para>
    /// <para>
    /// The validation mode determines behavior:
    /// <list type="bullet">
    ///   <item><description><see cref="ValidationMode.Strict"/> — the element must have a global declaration.</description></item>
    ///   <item><description><see cref="ValidationMode.Lax"/> — validate if a declaration exists, pass through if not.</description></item>
    ///   <item><description><see cref="ValidationMode.Type"/> — validate against a specific named type.</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <param name="node">The document or element node to validate.</param>
    /// <param name="mode">The validation mode (strict, lax, or type).</param>
    /// <param name="typeNamespaceUri">
    /// For <see cref="ValidationMode.Type"/>, the namespace URI of the target type.
    /// Null for strict and lax modes.
    /// </param>
    /// <param name="typeLocalName">
    /// For <see cref="ValidationMode.Type"/>, the local name of the target type.
    /// Null for strict and lax modes.
    /// </param>
    /// <returns>A validated copy of the node tree with type annotations applied.</returns>
    /// <exception cref="SchemaValidationException">
    /// Thrown if validation fails (maps to XQDY0027 for strict/lax, XQDY0027 for type).
    /// </exception>
    XdmNode Validate(XdmNode node, ValidationMode mode, string? typeNamespaceUri = null, string? typeLocalName = null);
}

/// <summary>
/// Validation mode for the <c>validate</c> expression.
/// </summary>
public enum ValidationMode
{
    /// <summary>
    /// <c>validate strict { ... }</c> — the element must match a global element declaration.
    /// This is the default mode when no keyword is specified.
    /// </summary>
    Strict,

    /// <summary>
    /// <c>validate lax { ... }</c> — validate if a declaration is found, pass through if not.
    /// </summary>
    Lax,

    /// <summary>
    /// <c>validate type T { ... }</c> — validate against the named type <c>T</c>.
    /// </summary>
    Type
}

/// <summary>
/// Thrown when a schema cannot be loaded or compiled.
/// Maps to XQuery error code XQST0059.
/// </summary>
public class SchemaException : Exception
{
    public string ErrorCode { get; }

    public SchemaException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public SchemaException(string errorCode, string message, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}

/// <summary>
/// Thrown when schema validation fails during a <c>validate</c> expression.
/// Maps to XQuery error code XQDY0027.
/// </summary>
public class SchemaValidationException : Exception
{
    public string ErrorCode { get; }

    public SchemaValidationException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public SchemaValidationException(string errorCode, string message, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}
