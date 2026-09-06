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
/// For clause operator.
/// </summary>
public sealed class ForClauseOperator : FlworClauseOperator
{
    public required IReadOnlyList<ForBindingOperator> Bindings { get; init; }

    private static bool IsAtomicForTarget(Ast.ItemType t) => t is not (
        Ast.ItemType.Item or Ast.ItemType.Node or Ast.ItemType.Element or Ast.ItemType.Attribute
        or Ast.ItemType.Text or Ast.ItemType.Document or Ast.ItemType.Comment
        or Ast.ItemType.ProcessingInstruction or Ast.ItemType.Function
        or Ast.ItemType.Map or Ast.ItemType.Array);
    /// <summary>True for "for member" (XPath 4.0) — iterates over array members.</summary>
    public bool IsMember { get; init; }

    public override async IAsyncEnumerable<Dictionary<QName, object?>> ExecuteAsync(QueryExecutionContext context)
    {
        await foreach (var tuple in ExecuteBindingsAsync(context, 0))
        {
            yield return tuple;
        }
    }

    private async IAsyncEnumerable<Dictionary<QName, object?>> ExecuteBindingsAsync(
        QueryExecutionContext context, int index)
    {
        if (index >= Bindings.Count)
        {
            yield return new Dictionary<QName, object?>();
            yield break;
        }

        var binding = Bindings[index];
        var position = 0;

        // XPath 4.0: "for member" iterates over array members
        if (IsMember)
        {
            // Collect the input (should be a single array value)
            var itemCount = 0;
            object? inputResult = null;
            await foreach (var item in binding.InputOperator.ExecuteAsync(context))
            {
                itemCount++;
                inputResult = item;
            }
            if (itemCount > 1)
                throw new XQueryRuntimeException("XPTY0004", "for member requires a single array value, not a sequence of multiple items");

            IList<object?> members;
            if (inputResult is List<object?> list) members = list;
            else if (inputResult is object?[] arr) members = arr;
            else members = inputResult != null ? new[] { inputResult } : System.Array.Empty<object?>();

            foreach (var member in members)
            {
                position++;
                var tuple = new Dictionary<QName, object?> { [binding.Variable] = member };
                if (binding.PositionalVariable.HasValue)
                    tuple[binding.PositionalVariable.Value] = (long)position;

                context.PushScope();
                foreach (var kvp in tuple)
                    context.BindVariable(kvp.Key, kvp.Value);

                await foreach (var innerTuple in ExecuteBindingsAsync(context, index + 1))
                {
                    var merged = new Dictionary<QName, object?>(tuple);
                    foreach (var kvp in innerTuple) merged[kvp.Key] = kvp.Value;
                    yield return merged;
                }
                context.PopScope();
            }
            yield break;
        }

        // Stream when no positional variable is needed; otherwise materialize
        if (!binding.PositionalVariable.HasValue && !binding.AllowingEmpty)
        {
            await foreach (var rawItem in binding.InputOperator.ExecuteAsync(context))
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                position++;
                var item = rawItem;
                if (binding.TypeDeclaration != null)
                {
                    // XQuery §3.8.1: for clause type declaration — strict SequenceType matching
                    // (no promotion, no untypedAtomic casting)
                    if (item != null && !TypeCastHelper.MatchesSequenceItemType(item, binding.TypeDeclaration))
                        throw new XQueryRuntimeException("XPTY0004",
                            $"for ${binding.Variable.LocalName}: value does not match declared type {binding.TypeDeclaration}");
                }
                var tuple = new Dictionary<QName, object?> { [binding.Variable] = item };

                context.PushScope();
                context.BindVariable(binding.Variable, item);

                try
                {
                    await foreach (var rest in ExecuteBindingsAsync(context, index + 1))
                    {
                        var merged = new Dictionary<QName, object?>(tuple);
                        foreach (var (name, value) in rest)
                            merged[name] = value;
                        yield return merged;
                    }
                }
                finally
                {
                    context.PopScope();
                }
            }
            yield break;
        }

        var items = new List<object?>();
        await foreach (var item in binding.InputOperator.ExecuteAsync(context))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            items.Add(item);
            context.CheckMaterializationLimit(items.Count);
        }

        if (items.Count == 0 && binding.AllowingEmpty)
        {
            // Per XQuery 3.1 §3.12.4.1: if allowing empty with a type declaration
            // that requires exactly-one or one-or-more, empty binding is a type error
            if (binding.TypeDeclaration != null
                && binding.TypeDeclaration.Occurrence is Ast.Occurrence.ExactlyOne or Ast.Occurrence.OneOrMore)
            {
                throw new XQueryRuntimeException("XPTY0004",
                    $"Variable ${binding.Variable.LocalName} declared as {binding.TypeDeclaration} " +
                    $"but 'allowing empty' produced an empty sequence");
            }
            var tuple = new Dictionary<QName, object?> { [binding.Variable] = null };
            if (binding.PositionalVariable.HasValue)
            {
                tuple[binding.PositionalVariable.Value] = 0;
            }

            await foreach (var rest in ExecuteBindingsAsync(context, index + 1))
            {
                var merged = new Dictionary<QName, object?>(tuple);
                foreach (var (name, value) in rest)
                    merged[name] = value;
                yield return merged;
            }
            yield break;
        }

        foreach (var rawItem in items)
        {
            position++;
            var item = rawItem;
            if (binding.TypeDeclaration != null && IsAtomicForTarget(binding.TypeDeclaration.ItemType))
            {
                item = context.AtomizeWithNodes(item);
                item = TypeCastHelper.CastValue(item, binding.TypeDeclaration.ItemType);
            }
            else if (binding.TypeDeclaration != null)
            {
                // Node/element/attribute/etc. target — require the item to be a node
                var it = binding.TypeDeclaration.ItemType;
                if (it is Ast.ItemType.Node or Ast.ItemType.Element or Ast.ItemType.Attribute
                    or Ast.ItemType.Text or Ast.ItemType.Document or Ast.ItemType.Comment
                    or Ast.ItemType.ProcessingInstruction)
                {
                    if (item is not Xdm.Nodes.XdmNode)
                        throw new XQueryRuntimeException("XPTY0004",
                            $"For binding requires {it}, got {item?.GetType().Name ?? "empty"}");
                }
            }
            var tuple = new Dictionary<QName, object?> { [binding.Variable] = item };
            if (binding.PositionalVariable.HasValue)
            {
                tuple[binding.PositionalVariable.Value] = position;
            }

            context.PushScope();
            context.BindVariable(binding.Variable, item);
            if (binding.PositionalVariable.HasValue)
            {
                context.BindVariable(binding.PositionalVariable.Value, position);
            }

            try
            {
                await foreach (var rest in ExecuteBindingsAsync(context, index + 1))
                {
                    var merged = new Dictionary<QName, object?>(tuple);
                    foreach (var (name, value) in rest)
                        merged[name] = value;
                    yield return merged;
                }
            }
            finally
            {
                context.PopScope();
            }
        }
    }
}
