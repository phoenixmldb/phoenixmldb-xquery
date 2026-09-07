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
/// Evaluates a FLWOR expression.
/// </summary>
public sealed class FlworOperator : PhysicalOperator
{
    public required IReadOnlyList<FlworClauseOperator> Clauses { get; init; }
    public required PhysicalOperator ReturnOperator { get; init; }
    /// <summary>XPath 4.0: otherwise expression — evaluated when FLWOR produces empty.</summary>
    public PhysicalOperator? OtherwiseOperator { get; init; }

    /// <summary>
    /// XQuery 4.0 <c>while</c>-clause stop signal. Set true once a while-condition first
    /// evaluates to false; the recursive tuple generators observe it and unwind, halting
    /// the entire FLWOR iteration (including the driving <c>for</c>). Reset at each
    /// <see cref="ExecuteAsync"/> entry so re-entered FLWORs (nested loops) restart cleanly.
    /// </summary>
    private bool _whileTerminated;

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        _whileTerminated = false;
        // Reset all count clause counters at the start of each FLWOR execution.
        // This ensures that when a FLWOR is re-entered (e.g., inner for inside outer for),
        // counters restart from 0.
        foreach (var c in Clauses)
        {
            if (c is CountClauseOperator cOp) cOp.ResetCounter();
        }

        var hasResults = false;
        // FLWOR can produce a cartesian-product number of tuples (nested `for`
        // over large sequences hits 100K+ easily — QT3 XMark-Q8 generates 360K
        // person × closed_auction pairs). Poll cancellation on every tuple so
        // the per-test timeout in the conformance runner — and any caller-side
        // CancellationTokenSource — is actually observed.
        await foreach (var tuple in ExecuteClausesAsync(context, 0))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            context.PushScope();
            try
            {
                foreach (var (name, value) in tuple)
                {
                    context.BindVariable(name, value);
                }

                await foreach (var result in ReturnOperator.ExecuteAsync(context))
                {
                    hasResults = true;
                    yield return result;
                }
            }
            finally
            {
                context.PopScope();
            }
        }

        // XPath 4.0: if FLWOR produced nothing, evaluate otherwise
        if (!hasResults && OtherwiseOperator != null)
        {
            await foreach (var result in OtherwiseOperator.ExecuteAsync(context))
                yield return result;
        }
    }

    private async IAsyncEnumerable<Dictionary<QName, object?>> ExecuteClausesAsync(
        QueryExecutionContext context, int index)
    {
        if (index >= Clauses.Count)
        {
            yield return new Dictionary<QName, object?>();
            yield break;
        }

        var clause = Clauses[index];

        // Barrier clauses (order by, group by) need ALL upstream tuples materialized first.
        // We split the clause chain: collect tuples from clauses [0..barrier-1] via recursive
        // materialization, apply the barrier, then continue with clauses [barrier+1..N].
        if (clause is OrderByClauseOperator orderBy)
        {
            // Caller already materialized tuples — this should not be reached directly.
            // But if it is, just pass through to next clause.
            await foreach (var restTuple in ExecuteClausesAsync(context, index + 1))
                yield return restTuple;
            yield break;
        }

        if (clause is GroupByClauseOperator groupBy)
        {
            // Caller already materialized tuples — this should not be reached directly.
            await foreach (var restTuple in ExecuteClausesAsync(context, index + 1))
                yield return restTuple;
            yield break;
        }

        // Find the next barrier clause (order by or group by) in the remaining clauses.
        // If found, we must materialize ALL tuples from clauses [index..barrier-1] before
        // applying the barrier operation.
        int? barrierIndex = null;
        for (int i = index + 1; i < Clauses.Count; i++)
        {
            if (Clauses[i] is OrderByClauseOperator or GroupByClauseOperator)
            {
                barrierIndex = i;
                break;
            }
        }

        if (barrierIndex.HasValue)
        {
            // Materialize all tuples from clauses [index..barrier-1]
            var allTuples = new List<Dictionary<QName, object?>>();
            await foreach (var tuple in MaterializeUpToAsync(context, index, barrierIndex.Value))
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                allTuples.Add(tuple);
                context.CheckMaterializationLimit(allTuples.Count);
            }

            // Apply the barrier operation — and ALL remaining barriers, even when separated
            // by non-barrier clauses (let/where/count). Per XQuery semantics, once tuples are
            // materialized, let/where/count must be applied to the whole collection so that a
            // downstream `order by` or `group by` sees the full post-filter stream (QT3
            // use-case-groupby-Q6: group by → let → where → order by descending).
            int afterBarrierIndex = barrierIndex.Value;
            while (afterBarrierIndex < Clauses.Count)
            {
                var barrier = Clauses[afterBarrierIndex];
                if (barrier is OrderByClauseOperator orderByBarrier)
                {
                    allTuples = await SortTuplesAsync(allTuples, orderByBarrier, context);
                    afterBarrierIndex++;
                }
                else if (barrier is GroupByClauseOperator groupByBarrier)
                {
                    allTuples = await GroupTuplesAsync(allTuples, groupByBarrier, context);
                    afterBarrierIndex++;
                }
                else if (barrier is LetClauseOperator or WhereClauseOperator or CountClauseOperator)
                {
                    // Is there another barrier ahead? If not, bail out and let per-tuple
                    // processing handle the remaining suffix (preserves streaming).
                    bool hasDownstreamBarrier = false;
                    for (int j = afterBarrierIndex + 1; j < Clauses.Count; j++)
                    {
                        if (Clauses[j] is OrderByClauseOperator or GroupByClauseOperator)
                        { hasDownstreamBarrier = true; break; }
                    }
                    if (!hasDownstreamBarrier) break;

                    // Apply this non-barrier clause to every tuple in the collection.
                    var nextTuples = new List<Dictionary<QName, object?>>(allTuples.Count);
                    if (barrier is CountClauseOperator countOp) countOp.ResetCounter();
                    foreach (var t in allTuples)
                    {
                        context.PushScope();
                        foreach (var (n, v) in t) context.BindVariable(n, v);
                        try
                        {
                            if (barrier is LetClauseOperator letOp)
                            {
                                await foreach (var bindings in letOp.ExecuteAsync(context))
                                {
                                    var merged = new Dictionary<QName, object?>(t);
                                    foreach (var (n, v) in bindings) merged[n] = v;
                                    nextTuples.Add(merged);
                                }
                            }
                            else if (barrier is WhereClauseOperator whereOp)
                            {
                                bool pass = false;
                                await foreach (var _ in whereOp.ExecuteAsync(context)) { pass = true; break; }
                                if (pass) nextTuples.Add(t);
                            }
                            else if (barrier is CountClauseOperator cOp)
                            {
                                cOp.IncrementCounter();
                                var merged = new Dictionary<QName, object?>(t);
                                merged[cOp.Variable] = cOp.CurrentCount;
                                nextTuples.Add(merged);
                            }
                        }
                        finally { context.PopScope(); }
                    }
                    allTuples = nextTuples;
                    afterBarrierIndex++;
                }
                else
                {
                    break;
                }
            }

            // Continue with clauses after the last consecutive barrier
            foreach (var tuple in allTuples)
            {
                context.PushScope();
                foreach (var (name, value) in tuple)
                    context.BindVariable(name, value);

                try
                {
                    await foreach (var restTuple in ExecuteClausesAsync(context, afterBarrierIndex))
                    {
                        var merged = new Dictionary<QName, object?>(tuple);
                        foreach (var (name, value) in restTuple)
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

        // While clause (XQuery 4.0): stop the whole tuple stream the moment the condition
        // is false. Because the driving `for` recursion checks _whileTerminated after each
        // sub-iteration, setting the flag here halts all further outer iterations too —
        // iteration does NOT resume even if a later item would satisfy the condition.
        if (clause is WhileClauseOperator whileClause)
        {
            if (await whileClause.EvaluateConditionAsync(context))
            {
                await foreach (var restTuple in ExecuteClausesAsync(context, index + 1))
                {
                    yield return restTuple;
                    if (_whileTerminated) yield break;
                }
            }
            else
            {
                _whileTerminated = true;
            }
            yield break;
        }

        // Count clause: assigns a 1-based counter to each tuple from upstream.
        // The count maintains a running counter across all invocations within this FLWOR.
        if (clause is CountClauseOperator countClause)
        {
            countClause.IncrementCounter();
            context.BindVariable(countClause.Variable, countClause.CurrentCount);
            await foreach (var restTuple in ExecuteClausesAsync(context, index + 1))
            {
                // Only set in restTuple if a downstream clause hasn't already set this variable
                // (e.g., a second `count $index` re-assigning the same variable name).
                restTuple.TryAdd(countClause.Variable, countClause.CurrentCount);
                yield return restTuple;
            }
            yield break;
        }

        // No barrier ahead — normal streaming clause processing
        var allBindings = new List<Dictionary<QName, object?>>();
        await foreach (var bindings in clause.ExecuteAsync(context))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            allBindings.Add(bindings);
            context.CheckMaterializationLimit(allBindings.Count);
        }

        foreach (var bindings in allBindings)
        {
            // A while-clause deeper in the chain may have terminated the stream on a
            // previous sub-iteration — stop pulling further tuples from this clause.
            if (_whileTerminated) yield break;

            context.PushScope();
            foreach (var (name, value) in bindings)
                context.BindVariable(name, value);

            try
            {
                await foreach (var restTuple in ExecuteClausesAsync(context, index + 1))
                {
                    var merged = new Dictionary<QName, object?>(bindings);
                    foreach (var (name, value) in restTuple)
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

    /// <summary>
    /// Materializes all tuples produced by clauses [startIndex..endIndex-1].
    /// Each tuple contains all variable bindings accumulated through the clause chain.
    /// </summary>
    private async IAsyncEnumerable<Dictionary<QName, object?>> MaterializeUpToAsync(
        QueryExecutionContext context, int startIndex, int endIndex)
    {
        if (startIndex >= endIndex)
        {
            yield return new Dictionary<QName, object?>();
            yield break;
        }

        var clause = Clauses[startIndex];

        // While clause inside a materialized (pre-barrier) prefix: stop the stream on the
        // first false condition, mirroring the streaming path so `while` before an
        // `order by` / `group by` behaves identically.
        if (clause is WhileClauseOperator whileClause)
        {
            if (await whileClause.EvaluateConditionAsync(context))
            {
                await foreach (var rest in MaterializeUpToAsync(context, startIndex + 1, endIndex))
                {
                    yield return rest;
                    if (_whileTerminated) yield break;
                }
            }
            else
            {
                _whileTerminated = true;
            }
            yield break;
        }

        var allBindings = new List<Dictionary<QName, object?>>();
        await foreach (var bindings in clause.ExecuteAsync(context))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            allBindings.Add(bindings);
            context.CheckMaterializationLimit(allBindings.Count);
        }

        foreach (var bindings in allBindings)
        {
            if (_whileTerminated) yield break;

            context.PushScope();
            foreach (var (name, value) in bindings)
                context.BindVariable(name, value);

            try
            {
                await foreach (var rest in MaterializeUpToAsync(context, startIndex + 1, endIndex))
                {
                    var merged = new Dictionary<QName, object?>(bindings);
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

    private static async Task<List<Dictionary<QName, object?>>> SortTuplesAsync(
        List<Dictionary<QName, object?>> tuples,
        OrderByClauseOperator orderBy,
        QueryExecutionContext context)
    {
        var keyed = new List<(Dictionary<QName, object?> Tuple, List<object?> Keys)>();

        foreach (var tuple in tuples)
        {
            context.PushScope();
            foreach (var (name, value) in tuple)
                context.BindVariable(name, value);

            var keys = new List<object?>();
            foreach (var spec in orderBy.OrderSpecs)
            {
                object? key = null;
                int keyCount = 0;
                await foreach (var item in spec.KeyOperator.ExecuteAsync(context))
                {
                    keyCount++;
                    if (keyCount == 1) key = item;
                    else
                        throw new XQueryRuntimeException("XPTY0004",
                            "Order-by key expression must return a single value, got a sequence");
                }
                // Atomize sort keys — per XQuery spec, order by atomizes its key expressions.
                // For untyped elements, this produces xs:untypedAtomic which compares as string values.
                key = context.AtomizeWithNodes(key);
                keys.Add(key);
            }

            context.PopScope();
            keyed.Add((tuple, keys));
        }

        // Precompute StringComparison for each order spec (per-spec collation or default)
        var defaultComparison = context.DefaultCollation != null
            ? Functions.CollationHelper.GetStringComparison(context.DefaultCollation)
            : StringComparison.Ordinal;
        var specComparisons = orderBy.OrderSpecs.Select(s =>
            s.Collation != null
                ? Functions.CollationHelper.GetStringComparison(s.Collation)
                : defaultComparison).ToArray();

        // Use LINQ OrderBy for a stable sort — List<T>.Sort uses IntroSort which is
        // unstable and would violate the XQuery "stable order by" semantics that require
        // items with equal sort keys to preserve their original relative order.
        var sorted = keyed.OrderBy(x => x, Comparer<(Dictionary<QName, object?> Tuple, List<object?> Keys)>.Create(
            (a, b) =>
            {
                for (int i = 0; i < orderBy.OrderSpecs.Count; i++)
                {
                    var spec = orderBy.OrderSpecs[i];
                    var ka = i < a.Keys.Count ? a.Keys[i] : null;
                    var kb = i < b.Keys.Count ? b.Keys[i] : null;

                    var cmp = CompareValues(ka, kb, spec.EmptyOrder, specComparisons[i]);
                    if (spec.Direction == Ast.OrderDirection.Descending)
                        cmp = -cmp;

                    if (cmp != 0)
                        return cmp;
                }
                return 0;
            }));

        return sorted.Select(k => k.Tuple).ToList();
    }

    /// <summary>
    /// Groups tuples by grouping key variables. Non-key variables are aggregated into sequences.
    /// Per XQuery spec: grouping key variables retain a single value per group, while non-key
    /// variables accumulate all values from tuples in the group.
    /// </summary>
    private static async Task<List<Dictionary<QName, object?>>> GroupTuplesAsync(
        List<Dictionary<QName, object?>> tuples,
        GroupByClauseOperator groupBy,
        QueryExecutionContext context)
    {
        var groupKeyVarNames = new HashSet<QName>();
        foreach (var spec in groupBy.GroupingSpecs)
            groupKeyVarNames.Add(spec.Variable);

        // Per XQuery 3.0 spec, when successive grouping specs reference the same
        // variable name, only the LAST binding contributes to the composite key.
        // The "effective" key positions are the DISTINCT variable names, in the order of
        // their LAST occurrence among the specs. (group-013/016)
        var effectiveKeyVarNames = new List<QName>();
        var effectiveSpecIndex = new Dictionary<QName, int>();
        for (int si = 0; si < groupBy.GroupingSpecs.Count; si++)
        {
            var v = groupBy.GroupingSpecs[si].Variable;
            effectiveSpecIndex[v] = si;
        }
        // Preserve left-to-right order of last occurrences
        var seen = new HashSet<QName>();
        for (int si = groupBy.GroupingSpecs.Count - 1; si >= 0; si--)
        {
            var v = groupBy.GroupingSpecs[si].Variable;
            if (effectiveSpecIndex[v] == si && seen.Add(v))
                effectiveKeyVarNames.Insert(0, v);
        }

        // Build groups: key is a composite of all grouping variable values
        var groups = new List<(List<object?> KeyValues, List<Dictionary<QName, object?>> Tuples)>();

        foreach (var tuple in tuples)
        {
            // Compute grouping key values for this tuple — specs are evaluated left-to-right
            // and each rebind is visible to subsequent specs (within ONE scope push).
            var perVarKey = new Dictionary<QName, object?>();
            context.PushScope();
            foreach (var (name, value) in tuple)
                context.BindVariable(name, value);
            foreach (var spec in groupBy.GroupingSpecs)
            {
                object? keyVal;
                if (spec.KeyOperator != null)
                {
                    // Explicit key expression: group by $var := expr — evaluated in the
                    // current scope (which already reflects any earlier spec rebindings).
                    keyVal = null;
                    await foreach (var item in spec.KeyOperator.ExecuteAsync(context))
                    {
                        keyVal = item;
                        break;
                    }
                }
                else
                {
                    // Implicit: group by $var — uses the variable's current binding.
                    // Per XQuery 3.0 spec, the variable MUST be bound in the tuple stream of
                    // the enclosing FLWOR (i.e. introduced by for/let/window/count inside it).
                    if (!tuple.ContainsKey(spec.Variable) && !perVarKey.ContainsKey(spec.Variable))
                        throw new PhoenixmlDb.XQuery.Functions.XQueryException("XQST0094",
                            $"Grouping variable ${spec.Variable.LocalName} is not bound by a for/let/window/count clause of the enclosing FLWOR");
                    keyVal = perVarKey.TryGetValue(spec.Variable, out var cur) ? cur : tuple[spec.Variable];
                }
                // When a type declaration is present we must preserve xs:untypedAtomic so
                // that the type check below fails with XPTY0004 rather than silently coercing.
                keyVal = spec.TypeDeclaration != null
                    ? QueryExecutionContext.AtomizeTyped(keyVal)
                    : context.AtomizeWithNodes(keyVal);
                // Per XQuery spec: the grouping key value must be zero or one atomic item.
                // If atomization produces a sequence of more than one item, raise XPTY0004.
                if (keyVal is System.Collections.IEnumerable en && keyVal is not string && keyVal is not byte[])
                {
                    var items = new List<object?>();
                    foreach (var it in en) items.Add(it);
                    if (items.Count > 1)
                        throw new PhoenixmlDb.XQuery.Functions.XQueryException("XPTY0004",
                            "Grouping key must be zero or one atomic value; got sequence of " + items.Count);
                    keyVal = items.Count == 1 ? items[0] : null;
                }
                // Normalize dateTime/date/time keys to UTC so values that differ only in
                // timezone representation compare as equal (group-019).
                keyVal = NormalizeGroupingKey(keyVal);

                // Type declaration check (group by $var as T := ...).
                // Per XQuery 3.0, the declared type applies to the POST-ATOMIZED key. The type
                // must be an atomic SequenceType; non-atomic declared types (e.g. attribute(*))
                // can never match atomized values and raise XPTY0004. Similarly, xs:string does
                // not accept xs:untypedAtomic without explicit cast ⇒ XPTY0004.
                if (spec.TypeDeclaration != null)
                {
                    var td = spec.TypeDeclaration;
                    bool isAtomicTarget = td.ItemType is not (
                        Ast.ItemType.Item or Ast.ItemType.Node or Ast.ItemType.Element or Ast.ItemType.Attribute
                        or Ast.ItemType.Text or Ast.ItemType.Document or Ast.ItemType.Comment
                        or Ast.ItemType.ProcessingInstruction or Ast.ItemType.Function
                        or Ast.ItemType.Map or Ast.ItemType.Array);
                    if (!isAtomicTarget)
                        throw new PhoenixmlDb.XQuery.Functions.XQueryException("XPTY0004",
                            $"Grouping key type {td.ItemType} is not an atomic type");
                    if (keyVal != null)
                    {
                        // Atomized untypedAtomic does NOT implicitly convert to other atomic types
                        // in group-by type declarations (per spec: no implicit cast).
                        if (keyVal is Xdm.XsUntypedAtomic && td.ItemType != Ast.ItemType.UntypedAtomic && td.ItemType != Ast.ItemType.AnyAtomicType)
                            throw new PhoenixmlDb.XQuery.Functions.XQueryException("XPTY0004",
                                $"Grouping key has type xs:untypedAtomic but declared type is {td.ItemType}");
                        TypeCastHelper.RequireAtomicTypeMatch(keyVal, td.ItemType, $"group by ${spec.Variable.LocalName}");
                    }
                }

                perVarKey[spec.Variable] = keyVal;
                // Rebind variable in context so subsequent specs see this value.
                context.BindVariable(spec.Variable, keyVal);
            }
            context.PopScope();

            // Assemble the effective composite key in the order of distinct variable names.
            var keyValues = new List<object?>();
            foreach (var vn in effectiveKeyVarNames)
                keyValues.Add(perVarKey[vn]);

            // For collation, build a list of the LAST spec for each effective var.
            var effectiveSpecs = effectiveKeyVarNames
                .Select(vn => groupBy.GroupingSpecs[effectiveSpecIndex[vn]]).ToList();

            // Find existing group with matching key
            var found = false;
            foreach (var group in groups)
            {
                if (GroupKeysEqual(group.KeyValues, keyValues, effectiveSpecs, context.DefaultCollation))
                {
                    group.Tuples.Add(tuple);
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                groups.Add((keyValues, new List<Dictionary<QName, object?>> { tuple }));
            }
        }

        // Build result tuples: one per group
        var result = new List<Dictionary<QName, object?>>();
        foreach (var (keyValues, groupTuples) in groups)
        {
            var resultTuple = new Dictionary<QName, object?>();

            // Set grouping key variables to the single key value (only distinct var names).
            for (int i = 0; i < effectiveKeyVarNames.Count; i++)
            {
                resultTuple[effectiveKeyVarNames[i]] = keyValues[i];
            }

            // Aggregate non-key variables into sequences
            var allVarNames = new HashSet<QName>();
            foreach (var t in groupTuples)
                foreach (var name in t.Keys)
                    allVarNames.Add(name);

            foreach (var varName in allVarNames)
            {
                if (groupKeyVarNames.Contains(varName))
                    continue;

                var values = new List<object?>();
                foreach (var t in groupTuples)
                {
                    if (t.TryGetValue(varName, out var val))
                        values.Add(val);
                }

                resultTuple[varName] = values.Count switch
                {
                    0 => null,
                    1 => values[0],
                    _ => values.ToArray()
                };
            }

            result.Add(resultTuple);
        }

        return result;
    }

    /// <summary>
    /// Normalizes a grouping key so that equivalent values compare equal.
    /// Notably: xs:dateTime/date/time values with timezone are normalized to UTC
    /// so that two dateTimes referring to the same instant in different offsets
    /// fall into the same group (per QT3 test group-019).
    /// </summary>
    private static object? NormalizeGroupingKey(object? key)
    {
        if (key is PhoenixmlDb.Xdm.XsDateTime xdt)
        {
            // For grouping-key equality, compare on the absolute instant so that values
            // differing only in timezone representation collapse into the same group.
            // Per XQuery spec, a value without a timezone is treated as being in implicit
            // timezone (local) for comparison. Our parser stores no-tz values with offset 0
            // (UTC) so we must re-interpret the clock values as local before converting.
            DateTimeOffset dto = xdt.Value;
            if (!xdt.HasTimezone)
            {
                var local = DateTime.SpecifyKind(dto.DateTime, DateTimeKind.Unspecified);
                var offset = TimeZoneInfo.Local.GetUtcOffset(local);
                dto = new DateTimeOffset(local, offset);
            }
            var utc = dto.ToUniversalTime();
            return new PhoenixmlDb.Xdm.XsDateTime(utc, true) { ExtendedYear = xdt.ExtendedYear };
        }
        return key;
    }

    private static bool GroupKeysEqual(List<object?> a, List<object?> b, IReadOnlyList<GroupingSpecOperator>? specs = null, string? defaultCollation = null)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i] == null && b[i] == null) continue;
            if (a[i] == null || b[i] == null) return false;
            // Apply collation for string-typed grouping keys.
            // Per XQuery 3.0 §3.12.7: if no explicit collation is specified on the grouping
            // spec, the default collation from the static context applies.
            var coll = specs != null && i < specs.Count ? specs[i].Collation : null;
            coll ??= defaultCollation;
            if (coll != null && (a[i] is string || a[i] is Xdm.XsUntypedAtomic) && (b[i] is string || b[i] is Xdm.XsUntypedAtomic))
            {
                var sa = a[i]!.ToString() ?? "";
                var sb = b[i]!.ToString() ?? "";
                var cmp = Functions.CollationHelper.GetStringComparison(coll);
                if (!string.Equals(sa, sb, cmp)) return false;
                continue;
            }
            // Fall back to the shared XQuery value comparer so group-by keys see the same
            // equality semantics as distinct-values (numeric promotion, tz-aware date/time,
            // gYear-family implicit-tz handling).
            if (!Functions.XQueryValueComparer.Instance.Equals(a[i], b[i])) return false;
        }
        return true;
    }

    private static int CompareValues(object? a, object? b, Ast.EmptyOrder emptyOrder,
        StringComparison stringComparison = StringComparison.Ordinal)
    {
        // Treat NaN as empty per XQuery spec — NaN follows empty order policy
        bool aNaN = a is double da && double.IsNaN(da) || a is float fa && float.IsNaN(fa);
        bool bNaN = b is double db && double.IsNaN(db) || b is float fb && float.IsNaN(fb);
        if (aNaN) a = null;
        if (bNaN) b = null;

        if (a == null && b == null)
            return 0;
        if (a == null)
            return emptyOrder == Ast.EmptyOrder.Least ? -1 : 1;
        if (b == null)
            return emptyOrder == Ast.EmptyOrder.Least ? 1 : -1;

        // Unwrap XsTypedString and XsTypedInteger to plain values for comparison —
        // the type tag matters for instance-of but not for ordering.
        if (a is Xdm.XsTypedString tsA2) a = tsA2.Value;
        if (b is Xdm.XsTypedString tsB2) b = tsB2.Value;
        if (a is Xdm.XsTypedInteger tiA1) a = tiA1.Value;
        if (b is Xdm.XsTypedInteger tiB1) b = tiB1.Value;

        // XPTY0004: incomparable types in order-by (e.g., xs:string vs xs:integer / xs:date)
        bool aIsStr = a is string or Xdm.XsUntypedAtomic;
        bool bIsStr = b is string or Xdm.XsUntypedAtomic;
        bool aIsNum = a is long or int or short or byte or double or float or decimal;
        bool bIsNum = b is long or int or short or byte or double or float or decimal;
        bool aIsDate = a is Xdm.XsDate or Xdm.XsDateTime or Xdm.XsTime;
        bool bIsDate = b is Xdm.XsDate or Xdm.XsDateTime or Xdm.XsTime;
        if ((aIsStr && (bIsNum || bIsDate)) || (bIsStr && (aIsNum || aIsDate))
            || (aIsNum && bIsDate) || (bIsNum && aIsDate))
            throw new XQueryRuntimeException("XPTY0004",
                $"order-by keys have incomparable types: {a.GetType().Name} vs {b.GetType().Name}");

        // String comparisons use the per-spec collation or the default collation.
        // Use CollationHelper.CompareStrings for correct Unicode codepoint ordering.
        if (a is string sa && b is string sb)
            return Functions.CollationHelper.CompareStrings(sa, sb, stringComparison);
        if (a is Xdm.XsUntypedAtomic && b is Xdm.XsUntypedAtomic)
            return Functions.CollationHelper.CompareStrings(a.ToString()!, b.ToString()!, stringComparison);
        if ((a is string || a is Xdm.XsUntypedAtomic) && (b is string || b is Xdm.XsUntypedAtomic))
            return Functions.CollationHelper.CompareStrings(a.ToString()!, b.ToString()!, stringComparison);

        // Binary comparison: octet-by-octet unsigned byte ordering for same binary type.
        if (a is Xdm.XdmValue abv && abv.RawValue is byte[] aBytes)
        {
            if (b is Xdm.XdmValue bbv && bbv.RawValue is byte[] bBytes && abv.Type == bbv.Type)
                return aBytes.AsSpan().SequenceCompareTo(bBytes);
            throw new XQueryRuntimeException("XPTY0004",
                $"order-by keys have incomparable types: {abv.Type} vs {b.GetType().Name}");
        }
        if (b is Xdm.XdmValue bbv2 && bbv2.RawValue is byte[])
            throw new XQueryRuntimeException("XPTY0004",
                $"order-by keys have incomparable types: {a.GetType().Name} vs {bbv2.Type}");

        if (a is IComparable ca)
        {
            try
            { return ca.CompareTo(b); }
            catch (ArgumentException)
            { return string.Compare(a?.ToString(), b?.ToString(), StringComparison.Ordinal); }
        }

        return string.Compare(a.ToString(), b.ToString(), StringComparison.Ordinal);
    }
}
