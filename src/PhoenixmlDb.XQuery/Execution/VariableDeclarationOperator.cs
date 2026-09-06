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
/// Variable declaration operator: declare variable $name := expr;
/// </summary>
public sealed class VariableDeclarationOperator : PhysicalOperator
{
    public required QName VariableName { get; init; }
    public PhysicalOperator? ValueOperator { get; init; }
    public bool IsExternal { get; init; }
    public XdmSequenceType? TypeDeclaration { get; init; }

    /// <summary>
    /// Static base URI of the module that declared this variable. When the initializer
    /// runs, this overrides the importing query's static base URI so constructed nodes
    /// inherit the defining module's base URI (cbcl-module-002 / XQuery 3.1 §2.1.1).
    /// </summary>
    public string? ModuleBaseUri { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        // For external variables, check if a binding was provided before falling back to the default.
        if (IsExternal && context.TryGetExternalVariable(VariableName, out var externalValue))
        {
            // XQuery §2.2.5: implementations may apply function-conversion to externally
            // provided values. CLI-supplied bindings arrive as plain strings; when the
            // declared type is a non-string atomic, treat the string as xs:untypedAtomic
            // and cast (matches Saxon's CLI semantics so e.g. `-p n=10` works for
            // `declare variable $n as xs:integer external`).
            if (TypeDeclaration != null && externalValue is string strValue
                && TypeDeclaration.ItemType is not (ItemType.String or ItemType.AnyAtomicType or ItemType.UntypedAtomic
                    or ItemType.Item or ItemType.Node or ItemType.Element or ItemType.Attribute
                    or ItemType.Text or ItemType.Document or ItemType.Comment))
            {
                try
                {
                    externalValue = TypeCastHelper.CastValue(new Xdm.XsUntypedAtomic(strValue), TypeDeclaration.ItemType);
                }
                catch (XQueryRuntimeException)
                {
                    // Fall through to RequireSequenceTypeMatch below — it'll surface a
                    // clear XPTY0004 referencing the variable name.
                }
            }
            // XQuery §3.10.3: type check external variable values against declared type
            if (TypeDeclaration != null)
                TypeCastHelper.RequireSequenceTypeMatch(externalValue, TypeDeclaration, $"declare variable ${VariableName}");
            context.BindVariable(VariableName, externalValue);
            yield break;
        }

        if (ValueOperator == null)
        {
            // External variable with no default and no binding
            throw new XQueryRuntimeException("XPST0008",
                $"External variable ${VariableName} was not bound and has no default value");
        }

        var values = new List<object?>();
        var savedBaseUri = context.StaticBaseUri;
        if (ModuleBaseUri != null)
            context.StaticBaseUri = ModuleBaseUri;
        try
        {
            await foreach (var item in ValueOperator.ExecuteAsync(context))
                values.Add(item);
        }
        finally
        {
            if (ModuleBaseUri != null)
                context.StaticBaseUri = savedBaseUri;
        }

        object? value = values.Count switch
        {
            0 => null,
            1 => values[0],
            _ => values.ToArray()
        };

        // XQuery §3.10.3: type check variable value against declared type
        if (TypeDeclaration != null)
            TypeCastHelper.RequireSequenceTypeMatch(value, TypeDeclaration, $"declare variable ${VariableName}");

        context.BindVariable(VariableName, value);
        yield break;
    }
}
