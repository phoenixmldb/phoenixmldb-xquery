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
/// Function declaration operator: declare function local:name($params) { body };
/// </summary>
public sealed class FunctionDeclarationOperator : PhysicalOperator
{
    public required QName FunctionName { get; init; }
    public required IReadOnlyList<FunctionParameter> Parameters { get; init; }
    public required XQueryExpression Body { get; init; }
    public XdmSequenceType? DeclaredReturnType { get; init; }
    /// <summary>
    /// Static base URI of the module in which this function was declared. When the
    /// function executes, this overrides the caller's static base URI.
    /// </summary>
    public string? ModuleBaseUri { get; init; }
    /// <summary>
    /// Target namespace URI of the library module that declares this function.
    /// Propagated to <see cref="InlineFunctionItem"/> so that unqualified decimal-format
    /// names in <c>format-number</c> calls are resolved against the declaring module's
    /// namespace (XQuery 4.0 §4.18 module isolation for decimal-format declarations).
    /// </summary>
    public string? ModuleTargetNamespace { get; init; }

    /// <summary>
    /// The copy-namespaces mode declared in the library module that contains this function.
    /// When a library module does not declare copy-namespaces, null means default
    /// (<see cref="Analysis.CopyNamespacesMode.PreserveInherit"/>). Null for main-module functions.
    /// </summary>
    public Analysis.CopyNamespacesMode? ModuleCopyNamespacesMode { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        await Task.CompletedTask;
        var func = new InlineFunctionItem(Parameters, Body, context, moduleBaseUri: ModuleBaseUri,
            moduleTargetNamespace: ModuleTargetNamespace,
            moduleCopyNamespacesMode: ModuleCopyNamespacesMode);
        context.Functions.Register(new DeclaredFunction(FunctionName, Parameters, func, DeclaredReturnType, ModuleBaseUri));
        yield break;
    }
}
