using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Functions;

namespace PhoenixmlDb.XQuery.Execution;

/// <summary>
/// A reference to a built-in function that keeps the module it was made in: on each call the
/// static base URI and the module location of that place are in force, whichever module calls.
/// </summary>
internal sealed class ModuleBoundFunctionRef(XQueryFunction inner, string? staticBaseUri, string? moduleLocation) : XQueryFunction
{
    public override QName Name => inner.Name;
    public override XdmSequenceType ReturnType => inner.ReturnType;
    public override IReadOnlyList<FunctionParameterDef> Parameters => inner.Parameters;
    public override bool IsVariadic => inner.IsVariadic;
    public override int MaxArity => inner.MaxArity;

    public override async ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        if (context is not QueryExecutionContext qec)
            return await inner.InvokeAsync(arguments, context).ConfigureAwait(false);
        var (savedBaseUri, savedLocation) = (qec.StaticBaseUri, qec.ModuleLocation);
        (qec.StaticBaseUri, qec.ModuleLocation) = (staticBaseUri, moduleLocation);
        try
        {
            return await inner.InvokeAsync(arguments, context).ConfigureAwait(false);
        }
        finally
        {
            (qec.StaticBaseUri, qec.ModuleLocation) = (savedBaseUri, savedLocation);
        }
    }
}
