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
/// Let clause operator.
/// </summary>
public sealed class LetClauseOperator : FlworClauseOperator
{
    public required IReadOnlyList<LetBindingOperator> Bindings { get; init; }

    public override async IAsyncEnumerable<Dictionary<QName, object?>> ExecuteAsync(QueryExecutionContext context)
    {
        var tuple = new Dictionary<QName, object?>();

        foreach (var binding in Bindings)
        {
            var values = new List<object?>();
            await foreach (var item in binding.InputOperator.ExecuteAsync(context))
            {
                values.Add(item);
            }

            // Let binds the entire sequence
            object? value = values.Count switch
            {
                0 => null,
                1 => values[0],
                _ => values.ToArray()
            };

            if (binding.TypeDeclaration != null)
            {
                var td = binding.TypeDeclaration;
                // XQuery §3.8.1: let clause type declaration uses SequenceType matching
                // (no promotion, no untypedAtomic casting — stricter than function coercion)
                TypeCastHelper.RequireSequenceTypeMatch(value, td, $"let ${binding.Variable.LocalName}",
                    namespaceResolver: context.NamespaceResolver);
            }

            tuple[binding.Variable] = value;
            context.BindVariable(binding.Variable, value);
        }

        yield return tuple;
    }
}
