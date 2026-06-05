using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Optimizer;

/// <summary>
/// Reorders a multi-binding <c>for</c> clause's bindings so the cheapest
/// (smallest expected cost) binding drives the outer loop. Preserves data
/// dependencies — a binding that references an earlier binding's variable
/// keeps its original relative position.
/// </summary>
/// <remarks>
/// The reorderer plans each binding's expression independently (with no outer
/// variables in scope) and scores via <see cref="CostModel"/>. Bindings with
/// dependencies on earlier bindings can't move ahead of those bindings, but
/// can move past independent ones.
/// </remarks>
public sealed class FlworJoinReorderer
{
    private readonly CostModel _costModel;
    private readonly QueryOptimizer _planner;

    public FlworJoinReorderer(CostModel costModel)
    {
        ArgumentNullException.ThrowIfNull(costModel);
        _costModel = costModel;
        _planner = new QueryOptimizer();
    }

    /// <summary>
    /// Returns bindings sorted by ascending estimated cost, respecting data
    /// dependencies. When <paramref name="bindings"/> has 0 or 1 entries,
    /// returns it unchanged.
    /// </summary>
    public IReadOnlyList<ForBinding> Reorder(IReadOnlyList<ForBinding> bindings, ContainerId container)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        if (bindings.Count <= 1) return bindings;

        // Plan + score each binding independently. Capture variable dependencies.
        var entries = new List<Entry>(bindings.Count);
        var ctx = new OptimizationContext { Container = container };
        for (int i = 0; i < bindings.Count; i++)
        {
            var b = bindings[i];
            var op = _planner.CreatePhysicalPlan(b.Expression, ctx);
            var cost = _costModel.EstimateCost(op, container);
            var deps = CollectVariableDeps(b.Expression);
            entries.Add(new Entry(b, i, cost, deps));
        }

        // Topological order constrained by deps, breaking ties by ascending cost.
        var remaining = new List<Entry>(entries);
        var emitted = new List<ForBinding>(bindings.Count);
        var emittedNames = new HashSet<string>();

        while (remaining.Count > 0)
        {
            int chosenIndex = -1;
            double bestCost = double.MaxValue;
            for (int i = 0; i < remaining.Count; i++)
            {
                var cand = remaining[i];
                // All deps that refer to OTHER bindings in this set must already be satisfied.
                bool satisfied = true;
                foreach (var d in cand.Deps)
                {
                    if (d == cand.Binding.Variable.LocalName) continue;
                    // Only constrain on deps that name another binding in this clause.
                    if (!ContainsBinding(entries, d)) continue;
                    if (!emittedNames.Contains(d)) { satisfied = false; break; }
                }
                if (!satisfied) continue;
                if (cand.Cost < bestCost)
                {
                    chosenIndex = i;
                    bestCost = cand.Cost;
                }
            }
            if (chosenIndex < 0)
            {
                // Cycle or unresolvable — emit remaining in original order to avoid losing bindings.
                remaining.Sort((a, b) => a.OriginalIndex.CompareTo(b.OriginalIndex));
                foreach (var r in remaining) emitted.Add(r.Binding);
                break;
            }
            emitted.Add(remaining[chosenIndex].Binding);
            emittedNames.Add(remaining[chosenIndex].Binding.Variable.LocalName);
            remaining.RemoveAt(chosenIndex);
        }
        return emitted;
    }

    private static bool ContainsBinding(IEnumerable<Entry> set, string name)
    {
        foreach (var e in set)
            if (e.Binding.Variable.LocalName == name) return true;
        return false;
    }

    private static HashSet<string> CollectVariableDeps(XQueryExpression expr)
    {
        var deps = new HashSet<string>();
        Visit(expr, deps);
        return deps;
    }

    private static void Visit(XQueryExpression? expr, HashSet<string> deps)
    {
        if (expr == null) return;
        switch (expr)
        {
            case VariableReference vr:
                deps.Add(vr.Name.LocalName);
                break;
            case PathExpression p:
                Visit(p.InitialExpression, deps);
                foreach (var s in p.Steps)
                    foreach (var pr in s.Predicates) Visit(pr, deps);
                break;
            case BinaryExpression bin:
                Visit(bin.Left, deps);
                Visit(bin.Right, deps);
                break;
            case UnaryExpression un:
                Visit(un.Operand, deps);
                break;
            case FunctionCallExpression fn:
                foreach (var a in fn.Arguments) Visit(a, deps);
                break;
            case IfExpression iff:
                Visit(iff.Condition, deps);
                Visit(iff.Then, deps);
                Visit(iff.Else, deps);
                break;
            case SequenceExpression seq:
                foreach (var i in seq.Items) Visit(i, deps);
                break;
            // Other expression kinds are conservatively treated as having no
            // outer-scope dependencies. Extend as needed when new patterns appear.
        }
    }

    private readonly record struct Entry(ForBinding Binding, int OriginalIndex, double Cost, HashSet<string> Deps);
}
