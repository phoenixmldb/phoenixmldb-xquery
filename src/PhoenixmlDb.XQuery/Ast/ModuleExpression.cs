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
    /// For library modules, the target namespace declared via <c>module namespace prefix = "uri"</c>.
    /// Null for main modules.
    /// </summary>
    public string? TargetNamespace { get; init; }

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

    /// <summary>
    /// Construction mode declared in the prolog via <c>declare construction preserve|strip</c>.
    /// Null if not declared (defaults to Preserve per XQuery 3.1 §2.2.1).
    /// </summary>
    public Analysis.ConstructionMode? ConstructionMode { get; init; }

    /// <summary>
    /// Boundary-space mode declared in the prolog via <c>declare boundary-space preserve/strip</c>.
    /// Null if not declared (defaults to strip per XQuery 3.1 §2.2.1).
    /// </summary>
    public bool? BoundarySpacePreserve { get; init; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor) => visitor.VisitModuleExpression(this);
}
