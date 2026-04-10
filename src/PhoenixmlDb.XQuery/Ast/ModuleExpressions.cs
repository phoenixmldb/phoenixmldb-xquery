using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Wraps a module with prolog declarations and a query body.
/// </summary>
public sealed class ModuleExpression : XQueryExpression
{
    /// <summary>
    /// Prolog declarations (variable bindings, function declarations, namespace declarations).
    /// </summary>
    public required IReadOnlyList<XQueryExpression> Declarations { get; init; }

    /// <summary>
    /// The query body expression.
    /// </summary>
    public required XQueryExpression Body { get; init; }

    /// <summary>
    /// Static base URI declared in this module's prolog via <c>declare base-uri "..."</c>.
    /// Null if not declared. Captured by closures created within this module for
    /// <c>fn:static-base-uri()</c> and relative URI resolution.
    /// </summary>
    public string? BaseUri { get; init; }

    /// <summary>
    /// Copy-namespaces mode declared in the prolog via <c>declare copy-namespaces ...</c>.
    /// Null if not declared (defaults to preserve, inherit).
    /// </summary>
    public Analysis.CopyNamespacesMode? CopyNamespacesMode { get; init; }

    /// <summary>
    /// Default collation URI declared in the prolog via <c>declare default collation "..."</c>.
    /// Null if not declared (defaults to Unicode codepoint collation).
    /// </summary>
    public string? DefaultCollation { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor) => visitor.VisitModuleExpression(this);
}

/// <summary>
/// declare variable $name := expr;
/// </summary>
public sealed class VariableDeclarationExpression : XQueryExpression
{
    public required QName Name { get; set; }
    public XdmSequenceType? TypeDeclaration { get; init; }
    /// <summary>
    /// The initializer expression. Null when the variable is declared <c>external</c> with no default value.
    /// </summary>
    public XQueryExpression? Value { get; init; }
    public bool IsExternal { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor) => visitor.VisitVariableDeclaration(this);
}

/// <summary>
/// declare function name($params) [as type] { body };
/// </summary>
public sealed class FunctionDeclarationExpression : XQueryExpression
{
    public required QName Name { get; set; }
    public required IReadOnlyList<FunctionParameter> Parameters { get; init; }
    public XdmSequenceType? ReturnType { get; init; }
    public required XQueryExpression Body { get; init; }

    /// <summary>
    /// Static base URI of the module in which this function was declared. When the
    /// function executes, this overrides the caller's static base URI for
    /// <c>fn:static-base-uri()</c> and relative URI resolution. Null for functions
    /// declared in the main module (inherits caller's base URI).
    /// </summary>
    public string? ModuleBaseUri { get; set; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor) => visitor.VisitFunctionDeclaration(this);
}

/// <summary>
/// declare namespace prefix = "uri";
/// </summary>
public sealed class NamespaceDeclarationExpression : XQueryExpression
{
    public required string Prefix { get; init; }
    public required string Uri { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor) => visitor.VisitNamespaceDeclaration(this);
}

/// <summary>
/// import module namespace prefix = "uri" at "location1", "location2";
/// </summary>
public sealed class ModuleImportExpression : XQueryExpression
{
    /// <summary>
    /// The namespace prefix bound by this import, or <c>null</c> if no prefix was specified.
    /// </summary>
    public string? Prefix { get; init; }

    /// <summary>
    /// The target namespace URI of the module to import.
    /// </summary>
    public required string NamespaceUri { get; init; }

    /// <summary>
    /// Optional location hints (URIs) for locating the module source.
    /// </summary>
    public IReadOnlyList<string> LocationHints { get; init; } = [];

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor) => visitor.VisitModuleImport(this);
}

/// <summary>
/// declare context item := expr;
/// </summary>
public sealed class ContextItemDeclarationExpression : XQueryExpression
{
    public XQueryExpression? DefaultValue { get; init; }
    /// <summary>The declared item type constraint (from "as &lt;type&gt;"), or null if none.</summary>
    public XdmSequenceType? TypeConstraint { get; init; }
    /// <summary>True when "external" keyword is present.</summary>
    public bool IsExternal { get; init; }
    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor) => visitor.VisitContextItemDeclaration(this);
}

/// <summary>
/// declare [default] decimal-format [name] property=value ...;
/// </summary>
public sealed class DecimalFormatDeclarationExpression : XQueryExpression
{
    /// <summary>
    /// The format name (null for the default decimal format).
    /// </summary>
    public string? FormatName { get; init; }

    /// <summary>
    /// Property name→value pairs (e.g., "decimal-separator" → ".").
    /// </summary>
    public Dictionary<string, string> Properties { get; init; } = new();

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor) => visitor.VisitDecimalFormatDeclaration(this);
}
