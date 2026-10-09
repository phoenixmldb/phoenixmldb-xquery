using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

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

    /// <summary>
    /// True when the variable is declared with <c>%private</c> annotation.
    /// Private variables are not visible to importing modules.
    /// </summary>
    public bool IsPrivate { get; init; }

    /// <summary>
    /// Static base URI of the module in which this variable was declared. When the
    /// initializer executes, this overrides the importing query's static base URI for
    /// constructed nodes, <c>fn:static-base-uri()</c>, and relative URI resolution.
    /// Null for variables declared in the main module (inherits caller's base URI).
    /// </summary>
    public string? ModuleBaseUri { get; set; }

    /// <summary>
    /// Where the module this declaration is in was loaded from, as the engine knows it: the
    /// URI its import named, or the base URI a host gave with supplied content. The module
    /// cannot change it (<c>declare base-uri</c> does not). Null when the engine knows no
    /// location to give.
    /// </summary>
    public string? ModuleLocation { get; set; }

    /// <summary>True for a declaration of an imported library module.</summary>
    public bool InLibraryModule { get; set; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor) => visitor.VisitVariableDeclaration(this);
}
