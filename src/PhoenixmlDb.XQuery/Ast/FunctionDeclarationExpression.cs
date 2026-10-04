using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Ast;

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
    /// Declared <c>external</c>: the implementation comes from the host, which binds it by
    /// registering a function of the same name and arity in the <c>FunctionLibrary</c> it compiles
    /// with. Unbound, it is XPST0017 (#18); it used to get an empty body and silently return ().
    /// </summary>
    public bool IsExternal { get; init; }

    /// <summary>
    /// Static base URI of the module in which this function was declared. When the
    /// function executes, this overrides the caller's static base URI for
    /// <c>fn:static-base-uri()</c> and relative URI resolution. Null for functions
    /// declared in the main module (inherits caller's base URI).
    /// </summary>
    public string? ModuleBaseUri { get; set; }

    /// <summary>
    /// Target namespace URI of the library module that declares this function.
    /// Used to resolve unqualified decimal-format names at runtime — per XQuery 4.0 §4.18,
    /// decimal-format declarations are module-local, so <c>format-number(n, pic, "df001")</c>
    /// inside an imported module must resolve <c>df001</c> against the module's own namespace
    /// rather than the caller's namespace. Null for main-module functions.
    /// </summary>
    public string? ModuleTargetNamespace { get; set; }

    /// <summary>
    /// True when the function is declared with <c>%private</c> annotation.
    /// Private functions are not visible to importing modules.
    /// </summary>
    public bool IsPrivate { get; init; }

    /// <summary>
    /// The copy-namespaces mode declared in the library module that contains this function.
    /// Per XQuery §4.4, the copy-namespaces declaration applies to element constructors
    /// in the module where it is declared. When a library module does not declare
    /// copy-namespaces, this is null (meaning default <see cref="Analysis.CopyNamespacesMode.PreserveInherit"/>).
    /// Null for main-module functions (they use the context's mode set from the main query).
    /// </summary>
    public Analysis.CopyNamespacesMode? ModuleCopyNamespacesMode { get; set; }

    /// <summary>
    /// The prefixes the declaring library module binds itself (its namespace declarations, its
    /// module prefix, its import prefixes), for names resolved at RUN time inside the body:
    /// format-number's format name, xs:QName('p:x'), a computed element name. Null for
    /// main-module functions, which use the main module's bindings.
    /// </summary>
    public IReadOnlyDictionary<string, string>? ModulePrefixBindings { get; set; }

    public override T Accept<T>(IXQueryExpressionVisitor<T> visitor) => visitor.VisitFunctionDeclaration(this);
}
