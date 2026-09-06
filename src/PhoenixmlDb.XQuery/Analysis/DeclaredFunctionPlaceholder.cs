using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Functions;

namespace PhoenixmlDb.XQuery.Analysis;

/// <summary>
/// Placeholder function registered during static analysis for user-defined functions.
/// Allows FunctionResolver to recognize calls to prolog-declared functions.
/// </summary>
internal sealed class DeclaredFunctionPlaceholder : XQueryFunction
{
    private readonly FunctionDeclarationExpression _decl;

    public DeclaredFunctionPlaceholder(FunctionDeclarationExpression decl, bool isFromImportedModule = false)
    {
        _decl = decl;
        IsFromImportedModule = isFromImportedModule;
        IsModulePrivate = isFromImportedModule && decl.IsPrivate;
    }

    /// <summary>
    /// True when this function was registered from an imported module (used for XQST0034 collision detection).
    /// </summary>
    public bool IsFromImportedModule { get; }

    public override QName Name => _decl.Name;
    public override XdmSequenceType ReturnType => _decl.ReturnType ?? XdmSequenceType.ZeroOrMoreItems;

    /// <summary>
    /// True when this function was declared with <c>%private</c> in an imported module.
    /// Private functions are accessible within the module but not from importing modules.
    /// Main module %private functions are always accessible.
    /// </summary>
    public bool IsModulePrivate { get; }

    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        _decl.Parameters.Select(p => new FunctionParameterDef
        {
            Name = p.Name,
            Type = p.Type ?? XdmSequenceType.ZeroOrMoreItems
        }).ToList();

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        // This placeholder should never be invoked at runtime;
        // the actual DeclaredFunction from FunctionDeclarationOperator replaces it.
        throw new InvalidOperationException(
            $"Placeholder for declared function {Name.LocalName} invoked at runtime");
    }
}
