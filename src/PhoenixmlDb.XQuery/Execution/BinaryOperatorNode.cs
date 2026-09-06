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
/// Binary operator node.
/// </summary>
public sealed class BinaryOperatorNode : PhysicalOperator
{
    public required PhysicalOperator Left { get; init; }
    public required PhysicalOperator Right { get; init; }
    public required BinaryOperator Operator { get; init; }

    // Thread-local default string comparison from execution context (set at start of ExecuteAsync)
    private StringComparison _stringComparison;
    private bool _backwardsCompatible;

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        // Resolve default collation for string comparisons
        _stringComparison = context.DefaultCollation != null
            ? Functions.CollationHelper.GetStringComparison(context.DefaultCollation)
            : StringComparison.Ordinal;
        _backwardsCompatible = context.BackwardsCompatible;
        // Union/Intersect/Except are sequence operations, not scalar operations
        if (Operator is BinaryOperator.Union)
        {
            // Union must return nodes in document order with duplicates eliminated
            var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
            var items = new List<object>();
            await foreach (var item in Left.ExecuteAsync(context).ConfigureAwait(false))
            {
                if (item != null)
                {
                    if (item is not Xdm.Nodes.XdmNode)
                        throw new XQueryRuntimeException("XPTY0004", $"An operand of the union operator is not a node");
                    if (seen.Add(item))
                        items.Add(item);
                }
            }
            await foreach (var item in Right.ExecuteAsync(context).ConfigureAwait(false))
            {
                if (item != null)
                {
                    if (item is not Xdm.Nodes.XdmNode)
                        throw new XQueryRuntimeException("XPTY0004", $"An operand of the union operator is not a node");
                    if (seen.Add(item))
                        items.Add(item);
                }
            }
            // Sort by document order (NodeId) if all items are XdmNodes
            if (items.Count > 1 && items[0] is Xdm.Nodes.XdmNode)
            {
                items.Sort((a, b) =>
                {
                    if (a is Xdm.Nodes.XdmNode na && b is Xdm.Nodes.XdmNode nb)
                        return Xdm.Nodes.XdmNode.CompareDocumentOrder(na, nb);
                    return 0;
                });
            }
            foreach (var item in items)
                yield return item;
            yield break;
        }

        if (Operator is BinaryOperator.Intersect)
        {
            var rightItems = new HashSet<object>(ReferenceEqualityComparer.Instance);
            await foreach (var item in Right.ExecuteAsync(context).ConfigureAwait(false))
            {
                if (item != null)
                {
                    if (item is not Xdm.Nodes.XdmNode)
                        throw new XQueryRuntimeException("XPTY0004", "An operand of the intersect operator is not a node");
                    rightItems.Add(item);
                }
            }
            var resultItems = new List<object>();
            var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
            await foreach (var item in Left.ExecuteAsync(context).ConfigureAwait(false))
            {
                if (item != null)
                {
                    if (item is not Xdm.Nodes.XdmNode)
                        throw new XQueryRuntimeException("XPTY0004", "An operand of the intersect operator is not a node");
                    if (rightItems.Contains(item) && seen.Add(item))
                        resultItems.Add(item);
                }
            }
            // Return in document order
            if (resultItems.Count > 1)
            {
                resultItems.Sort((a, b) =>
                {
                    if (a is Xdm.Nodes.XdmNode na && b is Xdm.Nodes.XdmNode nb)
                        return Xdm.Nodes.XdmNode.CompareDocumentOrder(na, nb);
                    return 0;
                });
            }
            foreach (var item in resultItems)
                yield return item;
            yield break;
        }

        // Naming the side and the actual item is the whole diagnosis: "an operand ... is not a
        // node" repeats the error code and omits which of the two operands, and what it held.
        static string DescribeNonNodeOperand(object item) => item switch
        {
            string str => $"the string '{(str.Length > 40 ? str[..40] + "..." : str)}'",
            Xdm.TextNodeItem t => $"an internal text marker '{(t.Value.Length > 40 ? t.Value[..40] + "..." : t.Value)}'",
            System.Collections.IDictionary => "a map",
            System.Collections.IEnumerable and not string => "an array or sequence",
            _ => $"a value of type {item.GetType().Name}"
        };

        if (Operator is BinaryOperator.Except)
        {
            var rightItems = new HashSet<object>(ReferenceEqualityComparer.Instance);
            await foreach (var item in Right.ExecuteAsync(context).ConfigureAwait(false))
            {
                if (item != null)
                {
                    if (item is not Xdm.Nodes.XdmNode)
                        throw new XQueryRuntimeException("XPTY0004",
                            "The right operand of the except operator is not a node: "
                            + DescribeNonNodeOperand(item));
                    rightItems.Add(item);
                }
            }
            var resultItems = new List<object>();
            var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
            await foreach (var item in Left.ExecuteAsync(context).ConfigureAwait(false))
            {
                if (item != null)
                {
                    if (item is not Xdm.Nodes.XdmNode)
                        throw new XQueryRuntimeException("XPTY0004",
                            "The left operand of the except operator is not a node: "
                            + DescribeNonNodeOperand(item));
                    if (!rightItems.Contains(item) && seen.Add(item))
                        resultItems.Add(item);
                }
            }
            // Return in document order
            if (resultItems.Count > 1)
            {
                resultItems.Sort((a, b) =>
                {
                    if (a is Xdm.Nodes.XdmNode na && b is Xdm.Nodes.XdmNode nb)
                        return Xdm.Nodes.XdmNode.CompareDocumentOrder(na, nb);
                    return 0;
                });
            }
            foreach (var item in resultItems)
                yield return item;
            yield break;
        }

        // General comparisons use existential semantics: iterate all items in
        // both operands and return true if ANY pair satisfies the comparison.
        if (Operator is BinaryOperator.GeneralEqual or BinaryOperator.GeneralNotEqual or
            BinaryOperator.GeneralLessThan or BinaryOperator.GeneralLessOrEqual or
            BinaryOperator.GeneralGreaterThan or BinaryOperator.GeneralGreaterOrEqual)
        {
            var leftItems = new List<object?>();
            await foreach (var item in Left.ExecuteAsync(context).ConfigureAwait(false))
            {
                // XQuery 3.1: arrays in general comparison operands are atomized and expanded
                if (item is List<object?> arr)
                {
                    var atomized = QueryExecutionContext.AtomizeTyped(item);
                    if (atomized is object?[] seq) leftItems.AddRange(seq);
                    else if (atomized != null) leftItems.Add(atomized);
                }
                else
                    leftItems.Add(item);
            }

            // XPath 2.0+ general comparison has existential semantics: the result is
            // true as soon as ANY (left, right) pair satisfies the operator. When the
            // left operand is small (the common case) and the right operand is a large
            // — possibly enormous — lazily produced sequence (e.g. a `to` range spanning
            // billions of integers), eagerly materialising the right side is a needless
            // scaling cliff: we can stream it and short-circuit on the first matching
            // pair. The XPath 1.0 backwards-compatibility paths below require the whole
            // right operand (boolean EBV / numeric coercion over the full set), so we
            // only stream in the non-backwards-compatible case.
            if (!context.BackwardsCompatible && leftItems.Count > 0)
            {
                await foreach (var rItem in Right.ExecuteAsync(context).ConfigureAwait(false))
                {
                    var rExpanded = new List<object?>();
                    if (rItem is List<object?>)
                    {
                        var atomized = QueryExecutionContext.AtomizeTyped(rItem);
                        if (atomized is object?[] seq) rExpanded.AddRange(seq);
                        else if (atomized != null) rExpanded.Add(atomized);
                    }
                    else
                        rExpanded.Add(rItem);

                    foreach (var l in leftItems)
                    {
                        foreach (var r in rExpanded)
                        {
                            var lv = QueryExecutionContext.AtomizeTyped(l);
                            var rv = QueryExecutionContext.AtomizeTyped(r);
                            (lv, rv) = CastUntypedForGeneralComparison(lv, rv, context);
                            if (EvaluateBinary(lv, rv) is true)
                            {
                                yield return true;
                                yield break;
                            }
                        }
                    }
                }
                // No pair matched (or the right operand was empty) → false.
                yield return false;
                yield break;
            }

            var rightItemsList = new List<object?>();
            await foreach (var item in Right.ExecuteAsync(context).ConfigureAwait(false))
            {
                if (item is List<object?> arr)
                {
                    var atomized = QueryExecutionContext.AtomizeTyped(item);
                    if (atomized is object?[] seq) rightItemsList.AddRange(seq);
                    else if (atomized != null) rightItemsList.Add(atomized);
                }
                else
                    rightItemsList.Add(item);
            }

            // If either operand is empty, general comparison is false
            if (leftItems.Count == 0 || rightItemsList.Count == 0)
            {
                yield return false;
                yield break;
            }

            // XPath 1.0 backwards-compat: if either operand contains a boolean
            // and the operator is = or !=, convert both to boolean before comparison.
            // For relational operators (<, >, <=, >=), XPath 1.0 always converts to numbers.
            if (context.BackwardsCompatible && Operator is BinaryOperator.GeneralEqual or BinaryOperator.GeneralNotEqual)
            {
                bool anyBoolLeft = leftItems.Any(i => context.AtomizeWithNodes(i) is bool);
                bool anyBoolRight = rightItemsList.Any(i => context.AtomizeWithNodes(i) is bool);
                if (anyBoolLeft || anyBoolRight)
                {
                    // Compare as booleans using effective boolean values
                    var leftEbv = QueryExecutionContext.EffectiveBooleanValue(
                        leftItems.Count == 1 ? leftItems[0] : leftItems.ToArray());
                    var rightEbv = QueryExecutionContext.EffectiveBooleanValue(
                        rightItemsList.Count == 1 ? rightItemsList[0] : rightItemsList.ToArray());
                    var boolResult = Operator is BinaryOperator.GeneralEqual
                        ? leftEbv == rightEbv
                        : leftEbv != rightEbv;
                    yield return boolResult;
                    yield break;
                }
            }

            // XPath 1.0 backwards-compat: for <, >, <=, >= when neither operand
            // is a node-set, convert both to numbers before comparison.
            // For = and !=, if at least one operand is a number, convert both to numbers.
            bool bc1NumericConvert = context.BackwardsCompatible && Operator is
                BinaryOperator.GeneralLessThan or BinaryOperator.GeneralLessOrEqual or
                BinaryOperator.GeneralGreaterThan or BinaryOperator.GeneralGreaterOrEqual;
            bool bc1EqNumericConvert = context.BackwardsCompatible && Operator is
                BinaryOperator.GeneralEqual or BinaryOperator.GeneralNotEqual;

            // Check all pairs for existential match
            foreach (var l in leftItems)
            {
                foreach (var r in rightItemsList)
                {
                    var lv = QueryExecutionContext.AtomizeTyped(l);
                    var rv = QueryExecutionContext.AtomizeTyped(r);
                    // XPath 1.0: ordering operators always convert to number;
                    // equality operators convert to number if either operand is numeric
                    if (bc1NumericConvert ||
                        (bc1EqNumericConvert && (IsNumericOrUntyped(lv) || IsNumericOrUntyped(rv))))
                    {
                        lv = CoerceToDouble(lv);
                        rv = CoerceToDouble(rv);
                    }
                    else if (!context.BackwardsCompatible)
                    {
                        // XPath 2.0+ general comparison: handle xs:untypedAtomic casting
                        (lv, rv) = CastUntypedForGeneralComparison(lv, rv, context);
                    }
                    var pairResult = EvaluateBinary(lv, rv);
                    if (pairResult is true)
                    {
                        yield return true;
                        yield break;
                    }
                }
            }
            yield return false;
            yield break;
        }

        // Short-circuit logical operators: do not evaluate right operand
        // when left operand already determines the result.
        if (Operator is BinaryOperator.Or)
        {
            object? leftVal = null;
            await foreach (var item in Left.ExecuteAsync(context).ConfigureAwait(false))
            { leftVal = item; break; }
            if (QueryExecutionContext.EffectiveBooleanValue(leftVal))
            {
                yield return true;
                yield break;
            }
            object? rightVal = null;
            await foreach (var item in Right.ExecuteAsync(context).ConfigureAwait(false))
            { rightVal = item; break; }
            yield return QueryExecutionContext.EffectiveBooleanValue(rightVal);
            yield break;
        }

        // XPath 4.0 otherwise: return left sequence if non-empty, else right sequence
        if (Operator is BinaryOperator.Otherwise)
        {
            var hasLeft = false;
            await foreach (var item in Left.ExecuteAsync(context).ConfigureAwait(false))
            {
                hasLeft = true;
                yield return item;
            }
            if (!hasLeft)
            {
                await foreach (var item in Right.ExecuteAsync(context).ConfigureAwait(false))
                    yield return item;
            }
            yield break;
        }

        if (Operator is BinaryOperator.And)
        {
            object? leftVal = null;
            await foreach (var item in Left.ExecuteAsync(context).ConfigureAwait(false))
            { leftVal = item; break; }
            if (!QueryExecutionContext.EffectiveBooleanValue(leftVal))
            {
                yield return false;
                yield break;
            }
            object? rightVal = null;
            await foreach (var item in Right.ExecuteAsync(context).ConfigureAwait(false))
            { rightVal = item; break; }
            yield return QueryExecutionContext.EffectiveBooleanValue(rightVal);
            yield break;
        }

        // Value comparisons (eq, ne, lt, etc.) and other operators: single item
        object? leftValue = null;
        int leftCount = 0;
        await foreach (var item in Left.ExecuteAsync(context))
        {
            // XQuery 3.1: arrays are atomized and expanded for value comparisons
            if (item is List<object?> leftArr)
            {
                var atomized = QueryExecutionContext.AtomizeTyped(item);
                if (atomized is object?[] leftSeq)
                {
                    foreach (var ai in leftSeq)
                    {
                        if (leftCount == 0) leftValue = ai;
                        leftCount++;
                        if (leftCount > 1) break;
                    }
                }
                else if (atomized != null)
                {
                    if (leftCount == 0) leftValue = atomized;
                    leftCount++;
                }
                // else: empty array → contributes nothing (leftCount stays 0)
            }
            else
            {
                if (leftCount == 0)
                    leftValue = item;
                leftCount++;
            }
            if (leftCount > 1)
                break;
        }

        object? rightValue = null;
        int rightCount = 0;
        await foreach (var item in Right.ExecuteAsync(context))
        {
            // XQuery 3.1: arrays are atomized and expanded for value comparisons
            if (item is List<object?> rightArr)
            {
                var atomized = QueryExecutionContext.AtomizeTyped(item);
                if (atomized is object?[] rightSeq)
                {
                    foreach (var ai in rightSeq)
                    {
                        if (rightCount == 0) rightValue = ai;
                        rightCount++;
                        if (rightCount > 1) break;
                    }
                }
                else if (atomized != null)
                {
                    if (rightCount == 0) rightValue = atomized;
                    rightCount++;
                }
            }
            else
            {
                if (rightCount == 0)
                    rightValue = item;
                rightCount++;
            }
            if (rightCount > 1)
                break;
        }

        // XPTY0004: Value comparisons require single item operands
        if ((leftCount > 1 || rightCount > 1) && Operator is
            BinaryOperator.Equal or BinaryOperator.NotEqual or
            BinaryOperator.LessThan or BinaryOperator.LessOrEqual or
            BinaryOperator.GreaterThan or BinaryOperator.GreaterOrEqual)
        {
            throw new XQueryRuntimeException("XPTY0004",
                "A value comparison operand is a sequence of more than one item");
        }

        // XPTY0004: arithmetic operators require singleton atomic operands
        if ((leftCount > 1 || rightCount > 1) && Operator is
            BinaryOperator.Add or BinaryOperator.Subtract or
            BinaryOperator.Multiply or BinaryOperator.Divide or
            BinaryOperator.IntegerDivide or BinaryOperator.Modulo)
        {
            throw new XQueryRuntimeException("XPTY0004",
                "An arithmetic operand is a sequence of more than one item");
        }

        // Value comparisons return empty sequence when either operand is empty
        if ((leftValue is null || rightValue is null) && Operator is
            BinaryOperator.Equal or BinaryOperator.NotEqual or
            BinaryOperator.LessThan or BinaryOperator.LessOrEqual or
            BinaryOperator.GreaterThan or BinaryOperator.GreaterOrEqual or
            BinaryOperator.Is or BinaryOperator.Precedes or BinaryOperator.Follows)
        {
            // Empty sequence — yield nothing
            yield break;
        }

        // XPath 3.1 §4.2: arithmetic operators return the empty sequence when either
        // operand is the empty sequence (leftCount==0 or rightCount==0). Skip this in
        // XPath 1.0 backwards-compatible mode, which coerces empty → NaN.
        if ((leftCount == 0 || rightCount == 0) && !context.BackwardsCompatible && Operator is
            BinaryOperator.Add or BinaryOperator.Subtract or
            BinaryOperator.Multiply or BinaryOperator.Divide or
            BinaryOperator.IntegerDivide or BinaryOperator.Modulo)
        {
            yield break;
        }

        // XPath 1.0 backwards-compatible: arithmetic always uses doubles,
        // and empty sequences become NaN.
        if (context.BackwardsCompatible && Operator is
            BinaryOperator.Add or BinaryOperator.Subtract or
            BinaryOperator.Multiply or BinaryOperator.Divide or BinaryOperator.Modulo)
        {
            var ld = CoerceToDouble(context.AtomizeWithNodes(leftValue));
            var rd = CoerceToDouble(context.AtomizeWithNodes(rightValue));
            yield return EvaluateBinary(ld, rd);
            yield break;
        }

        var result = EvaluateBinary(leftValue, rightValue);
        yield return result;
    }

    private object? EvaluateBinary(object? left, object? right)
    {
        // General comparisons use existential semantics: if either operand is an
        // empty sequence (null), the result is always false (no pairs to compare).
        if ((left is null || right is null) && Operator is
            BinaryOperator.GeneralEqual or BinaryOperator.GeneralNotEqual or
            BinaryOperator.GeneralLessThan or BinaryOperator.GeneralLessOrEqual or
            BinaryOperator.GeneralGreaterThan or BinaryOperator.GeneralGreaterOrEqual)
        {
            return false;
        }

        // Value comparisons (eq/ne/lt/le/gt/ge): per XPath 3.1 §3.7.1, if either
        // operand is the empty sequence the result is the empty sequence (NOT
        // false, NOT a type error). Without this an empty operand fell through to
        // atomization and could raise a spurious XPTY0004.
        if ((left is null || right is null) && Operator is
            BinaryOperator.Equal or BinaryOperator.NotEqual or
            BinaryOperator.LessThan or BinaryOperator.LessOrEqual or
            BinaryOperator.GreaterThan or BinaryOperator.GreaterOrEqual)
        {
            return null;
        }

        // Node comparison operators must use un-atomized node values
        if (Operator is BinaryOperator.Is or BinaryOperator.Precedes or BinaryOperator.Follows)
        {
            // Empty sequence operand → empty sequence result
            if (left is null || right is null)
                return null;
            var leftNode = left as Xdm.Nodes.XdmNode;
            var rightNode = right as Xdm.Nodes.XdmNode;
            // Non-empty, non-node operand → XPTY0004
            if (leftNode is null || rightNode is null)
                throw new XQueryRuntimeException("XPTY0004",
                    "Operands of node comparison operator must be nodes");
            return Operator switch
            {
                BinaryOperator.Is => ReferenceEquals(leftNode, rightNode) || leftNode.DocumentOrderKey == rightNode.DocumentOrderKey,
                BinaryOperator.Precedes => XdmNode.CompareDocumentOrder(leftNode, rightNode) < 0,
                BinaryOperator.Follows => XdmNode.CompareDocumentOrder(leftNode, rightNode) > 0,
                _ => null
            };
        }

        // Atomize XDM nodes for all binary operations, preserving xs:untypedAtomic type
        left = QueryExecutionContext.AtomizeTyped(left);
        right = QueryExecutionContext.AtomizeTyped(right);

        // XPath 2.0+ type handling for xs:untypedAtomic (when not in backwards-compatible mode)
        if (!_backwardsCompatible)
        {
            bool isArithmetic = Operator is BinaryOperator.Add or BinaryOperator.Subtract or
                BinaryOperator.Multiply or BinaryOperator.Divide or
                BinaryOperator.IntegerDivide or BinaryOperator.Modulo;
            bool isValueComparison = Operator is BinaryOperator.Equal or BinaryOperator.NotEqual or
                BinaryOperator.LessThan or BinaryOperator.LessOrEqual or
                BinaryOperator.GreaterThan or BinaryOperator.GreaterOrEqual;
            bool isGeneralComparison = Operator is BinaryOperator.GeneralEqual or BinaryOperator.GeneralNotEqual or
                BinaryOperator.GeneralLessThan or BinaryOperator.GeneralLessOrEqual or
                BinaryOperator.GeneralGreaterThan or BinaryOperator.GeneralGreaterOrEqual;

            if (isArithmetic)
            {
                // XPath 3.1 Section 4.2: xs:untypedAtomic → cast to xs:double for arithmetic
                if (left is Xdm.XsUntypedAtomic lua)
                    left = ToDoubleOrThrow(lua.Value);
                if (right is Xdm.XsUntypedAtomic rua)
                    right = ToDoubleOrThrow(rua.Value);
                // XPTY0004: xs:string is not a valid operand for arithmetic (XPath 2.0+)
                if (left is string || right is string)
                    throw new XQueryRuntimeException("XPTY0004",
                        "Arithmetic operators are not defined for xs:string");
            }
            else if (isValueComparison)
            {
                // XPath 3.1 Section 3.7.2: xs:untypedAtomic → always cast to xs:string
                // (Unlike general comparisons which cast to the other operand's type)
                if (left is Xdm.XsUntypedAtomic lua)
                    left = lua.Value;
                if (right is Xdm.XsUntypedAtomic rua)
                    right = rua.Value;
            }
            // For general comparisons, XsUntypedAtomic is already handled in the loop above
            // (CastUntypedForGeneralComparison). Any remaining XsUntypedAtomic → extract value.
            if (left is Xdm.XsUntypedAtomic lua3)
                left = lua3.Value;
            if (right is Xdm.XsUntypedAtomic rua3)
                right = rua3.Value;
            // Unwrap XsTypedString and XsTypedInteger to plain values for comparison/arithmetic.
            // Type tags only affect instance-of/typeswitch, not value semantics.
            if (left is Xdm.XsTypedString tsL)
                left = tsL.Value;
            if (right is Xdm.XsTypedString tsR)
                right = tsR.Value;
            if (left is Xdm.XsTypedInteger tiL)
                left = tiL.Value;
            if (right is Xdm.XsTypedInteger tiR)
                right = tiR.Value;

            // XPTY0004: incompatible types for comparison/arithmetic (XPath 2.0+)
            if (isValueComparison || isGeneralComparison)
            {
                bool leftIsBool = left is bool;
                bool rightIsBool = right is bool;
                bool leftIsStr = left is string;
                bool rightIsStr = right is string;
                bool leftIsNum = IsNumeric(left);
                bool rightIsNum = IsNumeric(right);

                if ((leftIsBool && !rightIsBool && right is not null) ||
                    (rightIsBool && !leftIsBool && left is not null))
                    throw new XQueryRuntimeException("XPTY0004",
                        "Cannot compare xs:boolean with non-boolean type");
                // xs:string vs numeric → XPTY0004
                if ((leftIsStr && rightIsNum) || (rightIsStr && leftIsNum))
                    throw new XQueryRuntimeException("XPTY0004",
                        "Cannot compare xs:string with numeric type");
                // xs:anyURI vs numeric → XPTY0004
                bool leftIsUri = left is Xdm.XsAnyUri;
                bool rightIsUri = right is Xdm.XsAnyUri;
                if ((leftIsUri && rightIsNum) || (rightIsUri && leftIsNum))
                    throw new XQueryRuntimeException("XPTY0004",
                        "Cannot compare xs:anyURI with numeric type");
                // Cross-type date/time comparison → XPTY0004
                // (dateTime eq date, time eq date, date eq integer, date eq string, etc.)
                if (isValueComparison)
                {
                    bool leftIsDate = left is Xdm.XsDateTime or Xdm.XsDate or Xdm.XsTime;
                    bool rightIsDate = right is Xdm.XsDateTime or Xdm.XsDate or Xdm.XsTime;
                    if (leftIsDate || rightIsDate)
                    {
                        if (!leftIsDate || !rightIsDate || left!.GetType() != right!.GetType())
                            throw new XQueryRuntimeException("XPTY0004",
                                $"Cannot compare {left?.GetType().Name ?? "empty-sequence"} with {right?.GetType().Name ?? "empty-sequence"}");
                    }
                }
            }
        }
        else
        {
            // Backwards-compatible mode: XsUntypedAtomic is just a string
            if (left is Xdm.XsUntypedAtomic lua)
                left = lua.Value;
            if (right is Xdm.XsUntypedAtomic rua)
                right = rua.Value;
        }

        // IEEE 754 NaN handling: NaN is not less than, equal to, or greater than anything.
        // .NET's CompareTo incorrectly orders NaN < everything, so handle NaN explicitly.
        if (IsComparisonOperator(Operator))
        {
            var leftIsNaN = left is double dl && double.IsNaN(dl) || left is float fl && float.IsNaN(fl);
            var rightIsNaN = right is double dr && double.IsNaN(dr) || right is float fr && float.IsNaN(fr);
            if (leftIsNaN || rightIsNaN)
            {
                return Operator is BinaryOperator.NotEqual or BinaryOperator.GeneralNotEqual
                    ? (object)true : false;
            }
        }

        return Operator switch
        {
            // Arithmetic
            BinaryOperator.Add => Add(left, right),
            BinaryOperator.Subtract => Subtract(left, right),
            BinaryOperator.Multiply => Multiply(left, right),
            BinaryOperator.Divide => Divide(left, right),
            BinaryOperator.IntegerDivide => IntegerDivide(left, right),
            BinaryOperator.Modulo => Modulo(left, right),

            // Comparisons
            BinaryOperator.Equal or BinaryOperator.GeneralEqual => ValueEquals(left, right, _stringComparison),
            BinaryOperator.NotEqual or BinaryOperator.GeneralNotEqual => !ValueEquals(left, right, _stringComparison),
            BinaryOperator.LessThan or BinaryOperator.GeneralLessThan => ValueCompare(left, right, _stringComparison) < 0,
            BinaryOperator.LessOrEqual or BinaryOperator.GeneralLessOrEqual => ValueCompare(left, right, _stringComparison) <= 0,
            BinaryOperator.GreaterThan or BinaryOperator.GeneralGreaterThan => ValueCompare(left, right, _stringComparison) > 0,
            BinaryOperator.GreaterOrEqual or BinaryOperator.GeneralGreaterOrEqual => ValueCompare(left, right, _stringComparison) >= 0,

            // Logical (And/Or now handled via short-circuit in ExecuteAsync;
            // this path only reached from general comparison pairs)
            BinaryOperator.And => QueryExecutionContext.EffectiveBooleanValue(left) &&
                                  QueryExecutionContext.EffectiveBooleanValue(right),
            BinaryOperator.Or => QueryExecutionContext.EffectiveBooleanValue(left) ||
                                 QueryExecutionContext.EffectiveBooleanValue(right),

            // String
            BinaryOperator.Concat => $"{left}{right}",

            _ => throw new XQueryRuntimeException("XPTY0004", $"Unsupported operator {Operator}")
        };
    }

    /// <summary>
    /// Converts a value to double (boxed) per XPath 1.0 rules for backwards-compatible comparisons.
    /// </summary>
    private static object? CoerceToDouble(object? value) => value switch
    {
        null => double.NaN,
        double d => d,
        float f => (double)f,
        long l => (double)l,
        int i => (double)i,
        decimal m => (double)m,
        bool b => b ? 1.0 : 0.0,
        Xdm.XsUntypedAtomic ua => double.TryParse(ua.Value, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var dua) ? dua : double.NaN,
        string s => double.TryParse(s, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : double.NaN,
        _ => double.NaN
    };

    /// <summary>
    /// Returns true if the value is numeric or xs:untypedAtomic (which may be coerced to numeric).
    /// Used in backwards-compatible general comparisons.
    /// </summary>
    private static bool IsNumericOrUntyped(object? value) =>
        value is long or int or double or float or decimal or BigInteger;

    private static bool IsDuration(object? value) =>
        value is Xdm.XsDuration or Xdm.YearMonthDuration or TimeSpan;

    private static (int months, TimeSpan dayTime) NormalizeDuration(object? value)
    {
        return value switch
        {
            Xdm.XsDuration d => (d.TotalMonths, d.DayTime),
            Xdm.YearMonthDuration ym => (ym.TotalMonths, TimeSpan.Zero),
            TimeSpan ts => (0, ts),
            _ => (0, TimeSpan.Zero)
        };
    }

    /// <summary>
    /// XPath 2.0+ general comparison: cast xs:untypedAtomic to the type of the other operand.
    /// Per XPath 3.1 Section 3.7.1.
    /// </summary>
    private static (object? left, object? right) CastUntypedForGeneralComparison(object? left, object? right, QueryExecutionContext? context = null)
    {
        var leftIsUntyped = left is Xdm.XsUntypedAtomic;
        var rightIsUntyped = right is Xdm.XsUntypedAtomic;

        if (leftIsUntyped && rightIsUntyped)
        {
            // Both untyped → compare as strings
            return (((Xdm.XsUntypedAtomic)left!).Value, ((Xdm.XsUntypedAtomic)right!).Value);
        }
        if (leftIsUntyped)
        {
            var ua = (Xdm.XsUntypedAtomic)left!;
            if (IsNumeric(right))
                return (ToDoubleOrThrow(ua.Value), right);
            if (right is bool)
                return (CastUntypedToBoolean(ua.Value), right);
            // Cast untyped to match date/time/duration/QName types (FORG0001 on failure)
            var rightItemType = GetItemTypeForValue(right);
            if (rightItemType != null)
                return (CastUntypedToType(ua.Value, rightItemType.Value, context), right);
            return (ua.Value, right); // Cast to string for string/other comparisons
        }
        if (rightIsUntyped)
        {
            var ua = (Xdm.XsUntypedAtomic)right!;
            if (IsNumeric(left))
                return (left, ToDoubleOrThrow(ua.Value));
            if (left is bool)
                return (left, CastUntypedToBoolean(ua.Value));
            // Cast untyped to match date/time/duration/QName types (FORG0001 on failure)
            var leftItemType = GetItemTypeForValue(left);
            if (leftItemType != null)
                return (left, CastUntypedToType(ua.Value, leftItemType.Value, context));
            return (left, ua.Value); // Cast to string for string/other comparisons
        }
        return (left, right); // Neither is untyped — no conversion needed
    }

    /// <summary>
    /// Casts an xs:untypedAtomic string value to xs:boolean per XPath casting rules.
    /// "true"/"1" → true, "false"/"0" → false, anything else → FORG0001.
    /// </summary>
    private static bool CastUntypedToBoolean(string value) => value.Trim() switch
    {
        "true" or "1" => true,
        "false" or "0" => false,
        _ => throw new XQueryRuntimeException("FORG0001",
            $"Cannot cast '{value}' to xs:boolean")
    };

    /// <summary>
    /// Casts an xs:untypedAtomic string value to a target type, wrapping parse errors as FORG0001.
    /// When casting to xs:QName, prefixes are resolved using the execution context's
    /// in-scope namespace bindings (XQuery 3.1 §3.7.2 general comparison: untyped
    /// operands cast to the type of the other operand using the static context).
    /// Without this, casting untypedAtomic 'z:local' to xs:QName produced a QName
    /// with a hash-derived NamespaceId that never matched any real element's
    /// QName — breaking QT3 GenCompEq-22.
    /// </summary>
    private static object? CastUntypedToType(string value, ItemType targetType, QueryExecutionContext? context = null)
    {
        try
        {
            if (targetType == ItemType.QName && context != null)
                return CastStringToQNameInContext(value, context);
            return TypeCastHelper.CastValue(value, targetType);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException
            && ex is not XQueryRuntimeException)
        {
            throw new XQueryRuntimeException("FORG0001",
                $"Cannot cast '{value}' to {targetType}: {ex.Message}");
        }
    }

    /// <summary>
    /// Casts a string to xs:QName using the supplied execution context's namespace
    /// bindings (PrefixNamespaceBindings, then well-known fn/xs/xsi/etc). Mirrors
    /// the CastExpression branch at line ~6878 so general comparison and explicit
    /// cast produce the same QName.
    /// </summary>
    private static QName CastStringToQNameInContext(string s, QueryExecutionContext context)
    {
        var trimmed = s.Trim();
        if (trimmed.Length == 0)
            throw new XQueryRuntimeException("FORG0001", "Cannot cast empty string to xs:QName");
        var colonIdx = trimmed.IndexOf(':', StringComparison.Ordinal);
        if (colonIdx > 0)
        {
            var prefix = trimmed[..colonIdx];
            var localName = trimmed[(colonIdx + 1)..];
            if (!TypeCastHelper.IsValidNCNameLex(prefix) || !TypeCastHelper.IsValidNCNameLex(localName))
                throw new XQueryRuntimeException("FORG0001",
                    $"'{s}' is not a valid lexical xs:QName");
            string? nsUri = null;
            context.PrefixNamespaceBindings?.TryGetValue(prefix, out nsUri);
            if (string.IsNullOrEmpty(nsUri))
            {
                nsUri = prefix switch
                {
                    "fn" => "http://www.w3.org/2005/xpath-functions",
                    "xs" => "http://www.w3.org/2001/XMLSchema",
                    "xsi" => "http://www.w3.org/2001/XMLSchema-instance",
                    "math" => "http://www.w3.org/2005/xpath-functions/math",
                    "map" => "http://www.w3.org/2005/xpath-functions/map",
                    "array" => "http://www.w3.org/2005/xpath-functions/array",
                    "xml" => "http://www.w3.org/XML/1998/namespace",
                    _ => null
                };
            }
            if (string.IsNullOrEmpty(nsUri))
                throw new XQueryRuntimeException("FONS0004",
                    $"No namespace declaration found for prefix '{prefix}' when casting to xs:QName");
            var nsId = context.NodeStore is INodeBuilder builder ? builder.InternNamespace(nsUri) : NamespaceId.None;
            return new QName(nsId, localName, prefix) { RuntimeNamespace = nsUri };
        }
        if (!TypeCastHelper.IsValidNCNameLex(trimmed))
            throw new XQueryRuntimeException("FORG0001",
                $"'{s}' is not a valid lexical xs:QName");
        return new QName(NamespaceId.None, trimmed);
    }

    /// <summary>
    /// Returns the ItemType for date/time/duration/QName values, or null for string/numeric/bool.
    /// Used to determine how to cast xs:untypedAtomic in general comparisons.
    /// </summary>
    private static ItemType? GetItemTypeForValue(object? value) => value switch
    {
        Xdm.XsDate => ItemType.Date,
        DateOnly => ItemType.Date,
        Xdm.XsDateTime => ItemType.DateTime,
        DateTimeOffset => ItemType.DateTime,
        Xdm.XsTime => ItemType.Time,
        TimeOnly => ItemType.Time,
        TimeSpan => ItemType.DayTimeDuration,
        Xdm.DayTimeDuration => ItemType.DayTimeDuration,
        Xdm.YearMonthDuration => ItemType.YearMonthDuration,
        Xdm.XsDuration => ItemType.Duration,
        Xdm.XsGYearMonth => ItemType.GYearMonth,
        Xdm.XsGYear => ItemType.GYear,
        Xdm.XsGMonthDay => ItemType.GMonthDay,
        Xdm.XsGDay => ItemType.GDay,
        Xdm.XsGMonth => ItemType.GMonth,
        Xdm.XsAnyUri => ItemType.AnyUri,
        Core.QName => ItemType.QName,
        Xdm.XdmValue xv when xv.Type == Xdm.XdmType.Base64Binary => ItemType.Base64Binary,
        Xdm.XdmValue xv2 when xv2.Type == Xdm.XdmType.HexBinary => ItemType.HexBinary,
        _ => null
    };

    private static (object? left, object? right) PromoteNumeric(object? left, object? right)
    {
        // Atomize XDM nodes before numeric promotion
        left = QueryExecutionContext.Atomize(left);
        right = QueryExecutionContext.Atomize(right);

        // Convert string values to doubles for numeric operations
        if (left is string ls)
            left = double.TryParse(ls, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var ld) ? ld : double.NaN;
        if (right is string rs)
            right = double.TryParse(rs, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var rd) ? rd : double.NaN;

        // If both are float (no double), stay in float to preserve xs:float type
        if (left is float lf && right is float rf)
            return (lf, rf);
        // If either is double, promote both to double
        if (left is double || right is double)
            return (left is BigInteger lbi ? (double)lbi : Convert.ToDouble(left),
                    right is BigInteger rbi ? (double)rbi : Convert.ToDouble(right));
        // If either is float (no double), promote both to float
        if (left is float || right is float)
            return (left is BigInteger lbi2 ? (float)lbi2 : Convert.ToSingle(left),
                    right is BigInteger rbi2 ? (float)rbi2 : Convert.ToSingle(right));
        // If either is decimal, promote both to decimal
        if (left is decimal || right is decimal)
            return (left is BigInteger lbi2 ? (decimal)lbi2 : Convert.ToDecimal(left),
                    right is BigInteger rbi2 ? (decimal)rbi2 : Convert.ToDecimal(right));
        // If either is BigInteger, promote both to BigInteger
        if (left is BigInteger || right is BigInteger)
            return (ToBigInteger(left), ToBigInteger(right));
        // Otherwise promote to long (handles int+long, int+int, long+long)
        return (Convert.ToInt64(left), Convert.ToInt64(right));
    }

    private static object? Add(object? left, object? right)
    {
        // XPath 2.0: arithmetic with empty sequence returns empty sequence
        if (left is null || right is null)
            return null;
        try { return AddCore(left, right); }
        catch (ArgumentOutOfRangeException)
        { throw new XQueryRuntimeException("FODT0002", "Date/time overflow in addition"); }
    }

    /// <summary>
    /// Per F&amp;O §10.6.1/§10.6.5: promote an xs:date to xs:dateTime at midnight, add the
    /// dayTimeDuration, then extract the date. Routed through proleptic-Gregorian arithmetic
    /// so results below year 1 are correct.
    /// </summary>
    private static Xdm.XsDate AddDurationToDate(Xdm.XsDate date, TimeSpan delta)
    {
        // Build a midnight dateTime preserving the date's effective (possibly extended) year.
        // date.Date carries the true month/day (year is clamped only for extended years).
        var midnight = new DateTimeOffset(date.Date.ToDateTime(TimeOnly.MinValue), date.Timezone ?? TimeSpan.Zero);
        var dt = new Xdm.XsDateTime(midnight, date.Timezone.HasValue) { ExtendedYear = date.ExtendedYear };
        var added = dt.Add(delta);
        // Extract the date component, preserving the effective year and timezone.
        var y = added.EffectiveYear;
        if (y is >= 1 and <= 9999)
            return new Xdm.XsDate(new DateOnly((int)y, added.Value.Month, added.Value.Day), date.Timezone);
        var isLeap = Xdm.XsDateTime.IsLeapYearProleptic(y);
        return new Xdm.XsDate(new DateOnly(isLeap ? 4 : 1, added.Value.Month, added.Value.Day), date.Timezone)
            { ExtendedYear = y };
    }

    private static object? AddCore(object left, object right)
    {

        // Date/time + duration arithmetic (new wrapper types).
        // Routed through proleptic-Gregorian Core arithmetic so crossings below year 1 are correct.
        if (left is Xdm.XsDate xld && right is Xdm.YearMonthDuration ymd)
            return xld.AddMonths(ymd.TotalMonths);
        if (left is Xdm.YearMonthDuration ymd2 && right is Xdm.XsDate xrd)
            return xrd.AddMonths(ymd2.TotalMonths);
        if (left is Xdm.XsDate xld2 && right is TimeSpan ts)
            return AddDurationToDate(xld2, ts);
        if (left is TimeSpan ts2 && right is Xdm.XsDate xrd2)
            return AddDurationToDate(xrd2, ts2);
        if (left is Xdm.XsDateTime xldt && right is Xdm.YearMonthDuration ymd3)
            return xldt.AddMonths(ymd3.TotalMonths);
        if (left is Xdm.YearMonthDuration ymd4 && right is Xdm.XsDateTime xrdt)
            return xrdt.AddMonths(ymd4.TotalMonths);
        if (left is Xdm.XsDateTime xldt2 && right is TimeSpan ts3)
            return xldt2.Add(ts3);
        if (left is TimeSpan ts4 && right is Xdm.XsDateTime xrdt2)
            return xrdt2.Add(ts4);
        if (left is Xdm.XsTime xlt && right is TimeSpan ts5)
        {
            var total = xlt.Time.ToTimeSpan() + ts5;
            var ticks = ((total.Ticks % TimeSpan.TicksPerDay) + TimeSpan.TicksPerDay) % TimeSpan.TicksPerDay;
            return new Xdm.XsTime(new TimeOnly(ticks), xlt.Timezone, (int)(ticks % TimeSpan.TicksPerSecond));
        }
        if (left is TimeSpan ts6 && right is Xdm.XsTime xrt)
        {
            var total = xrt.Time.ToTimeSpan() + ts6;
            var ticks = ((total.Ticks % TimeSpan.TicksPerDay) + TimeSpan.TicksPerDay) % TimeSpan.TicksPerDay;
            return new Xdm.XsTime(new TimeOnly(ticks), xrt.Timezone, (int)(ticks % TimeSpan.TicksPerSecond));
        }
        // Date/time + duration arithmetic (legacy raw types)
        if (left is DateOnly ld && right is Xdm.YearMonthDuration ymd0)
            return ld.AddMonths(ymd0.TotalMonths);
        if (left is Xdm.YearMonthDuration ymd02 && right is DateOnly rd)
            return rd.AddMonths(ymd02.TotalMonths);
        if (left is DateOnly ld2 && right is TimeSpan ts0)
            return ld2.AddDays((int)ts0.TotalDays);
        if (left is TimeSpan ts02 && right is DateOnly rd2)
            return rd2.AddDays((int)ts02.TotalDays);
        if (left is DateTimeOffset ldt && right is Xdm.YearMonthDuration ymd03)
            return ldt.AddMonths(ymd03.TotalMonths);
        if (left is Xdm.YearMonthDuration ymd04 && right is DateTimeOffset rdt)
            return rdt.AddMonths(ymd04.TotalMonths);
        if (left is DateTimeOffset ldt2 && right is TimeSpan ts03)
            return ldt2.Add(ts03);
        if (left is TimeSpan ts04 && right is DateTimeOffset rdt2)
            return rdt2.Add(ts04);
        // Time + dayTimeDuration
        if (left is TimeOnly lt && right is TimeSpan ts05)
        {
            var total = lt.ToTimeSpan() + ts05;
            var ticks = ((total.Ticks % TimeSpan.TicksPerDay) + TimeSpan.TicksPerDay) % TimeSpan.TicksPerDay;
            return new TimeOnly(ticks);
        }
        if (left is TimeSpan ts06 && right is TimeOnly rt)
        {
            var total = rt.ToTimeSpan() + ts06;
            var ticks = ((total.Ticks % TimeSpan.TicksPerDay) + TimeSpan.TicksPerDay) % TimeSpan.TicksPerDay;
            return new TimeOnly(ticks);
        }
        // Duration + duration
        if (left is Xdm.YearMonthDuration ya && right is Xdm.YearMonthDuration yb)
            return ya + yb;
        if (left is TimeSpan tsa && right is TimeSpan tsb)
            return tsa + tsb;

        if (IsNumeric(left) && IsNumeric(right))
        {
            var (l, r) = PromoteNumeric(left, right);
            return (l, r) switch
            {
                (long a, long b) => LongAddOrPromote(a, b),
                (BigInteger a, BigInteger b) => a + b,
                (float a, float b) => a + b,
                (double a, double b) => a + b,
                (decimal a, decimal b) => a + b,
                _ => Convert.ToDouble(l) + Convert.ToDouble(r)
            };
        }
        return ToDouble(left) + ToDouble(right);
    }

    private static object? Subtract(object? left, object? right)
    {
        try { return SubtractCore(left, right); }
        catch (ArgumentOutOfRangeException)
        { throw new XQueryRuntimeException("FODT0002", "Date/time overflow in subtraction"); }
    }

    private static object? SubtractCore(object? left, object? right)
    {
        // XPath 2.0: arithmetic with empty sequence returns empty sequence
        if (left is null || right is null)
            return null;

        // Date/time - duration arithmetic (new wrapper types). Subtraction = adding the negated
        // duration, routed through proleptic-Gregorian Core arithmetic (correct below year 1).
        if (left is Xdm.XsDate xld && right is Xdm.YearMonthDuration ymd)
            return xld.AddMonths(-(long)ymd.TotalMonths);
        if (left is Xdm.XsDate xld2 && right is TimeSpan ts)
            return AddDurationToDate(xld2, -ts);
        if (left is Xdm.XsDateTime xldt && right is Xdm.YearMonthDuration ymd2)
            return xldt.AddMonths(-(long)ymd2.TotalMonths);
        if (left is Xdm.XsDateTime xldt2 && right is TimeSpan ts2)
            return xldt2.Add(-ts2);
        if (left is Xdm.XsTime xlt && right is TimeSpan ts3)
        {
            var total = xlt.Time.ToTimeSpan() - ts3;
            var ticks = ((total.Ticks % TimeSpan.TicksPerDay) + TimeSpan.TicksPerDay) % TimeSpan.TicksPerDay;
            return new Xdm.XsTime(new TimeOnly(ticks), xlt.Timezone, (int)(ticks % TimeSpan.TicksPerSecond));
        }
        if (left is Xdm.XsTime xlt2 && right is Xdm.XsTime xrt)
        {
            // Per XPath F&O §10.6.7: normalize both times to UTC before subtracting.
            // If either has no timezone, use the implicit timezone.
            var implicitTz = DateTimeOffset.Now.Offset;
            var leftTz = xlt2.Timezone ?? implicitTz;
            var rightTz = xrt.Timezone ?? implicitTz;
            var leftUtcTicks = xlt2.Time.Ticks - leftTz.Ticks;
            var rightUtcTicks = xrt.Time.Ticks - rightTz.Ticks;
            return TimeSpan.FromTicks(leftUtcTicks - rightUtcTicks);
        }
        if (left is Xdm.XsDate xld3 && right is Xdm.XsDate xrd)
        {
            // Per XPath F&O §10.6.5: normalize both dates to UTC-equivalent before subtracting.
            var implicitTz = DateTimeOffset.Now.Offset;
            var leftTz = xld3.Timezone ?? implicitTz;
            var rightTz = xrd.Timezone ?? implicitTz;
            var leftDto = new DateTimeOffset(xld3.Date.ToDateTime(TimeOnly.MinValue), leftTz);
            var rightDto = new DateTimeOffset(xrd.Date.ToDateTime(TimeOnly.MinValue), rightTz);
            return leftDto.UtcDateTime - rightDto.UtcDateTime;
        }
        if (left is Xdm.XsDateTime xldt3 && right is Xdm.XsDateTime xrdt)
        {
            // Per XPath F&O §10.6.6: normalize both dateTimes to UTC before subtracting.
            // If either has no timezone, use the implicit timezone.
            var implicitTz = DateTimeOffset.Now.Offset;
            var leftVal = xldt3.HasTimezone ? xldt3.Value : new DateTimeOffset(xldt3.Value.DateTime, implicitTz);
            var rightVal = xrdt.HasTimezone ? xrdt.Value : new DateTimeOffset(xrdt.Value.DateTime, implicitTz);
            return leftVal - rightVal;
        }
        // Mixed XsDateTime / DateTimeOffset subtraction
        if (left is Xdm.XsDateTime xldt4 && right is DateTimeOffset rDto)
            return xldt4.Value - rDto;
        if (left is DateTimeOffset lDto && right is Xdm.XsDateTime xrdt2)
            return lDto - xrdt2.Value;
        if (left is DateTimeOffset lDto2 && right is DateTimeOffset rDto2)
            return lDto2 - rDto2;
        // Date/time - duration arithmetic (legacy raw types)
        if (left is DateOnly ld && right is Xdm.YearMonthDuration ymd0)
            return ld.AddMonths(-ymd0.TotalMonths);
        if (left is DateOnly ld2 && right is TimeSpan ts0)
            return ld2.AddDays(-(int)ts0.TotalDays);
        if (left is DateTimeOffset ldt && right is Xdm.YearMonthDuration ymd02)
            return ldt.AddMonths(-ymd02.TotalMonths);
        if (left is DateTimeOffset ldt2 && right is TimeSpan ts02)
            return ldt2.Subtract(ts02);
        if (left is TimeOnly lt && right is TimeSpan ts03)
        {
            var total = lt.ToTimeSpan() - ts03;
            var ticks = ((total.Ticks % TimeSpan.TicksPerDay) + TimeSpan.TicksPerDay) % TimeSpan.TicksPerDay;
            return new TimeOnly(ticks);
        }
        if (left is TimeOnly lt2 && right is TimeOnly rt)
            return lt2.ToTimeSpan() - rt.ToTimeSpan();
        if (left is DateOnly ld3 && right is DateOnly rd)
            return ld3.ToDateTime(TimeOnly.MinValue) - rd.ToDateTime(TimeOnly.MinValue);
        if (left is DateTimeOffset ldt3 && right is DateTimeOffset rdt)
            return ldt3 - rdt;
        // Duration - duration
        if (left is Xdm.YearMonthDuration ya && right is Xdm.YearMonthDuration yb)
            return ya - yb;
        if (left is TimeSpan tsa && right is TimeSpan tsb)
            return tsa - tsb;

        if (IsNumeric(left) && IsNumeric(right))
        {
            var (l, r) = PromoteNumeric(left, right);
            return (l, r) switch
            {
                (long a, long b) => LongSubtractOrPromote(a, b),
                (BigInteger a, BigInteger b) => a - b,
                (float a, float b) => a - b,
                (double a, double b) => a - b,
                (decimal a, decimal b) => a - b,
                _ => Convert.ToDouble(l) - Convert.ToDouble(r)
            };
        }
        return ToDouble(left) - ToDouble(right);
    }

    private static object? Multiply(object? left, object? right)
    {
        // XPath 2.0: arithmetic with empty sequence returns empty sequence
        if (left is null || right is null)
            return null;

        // Duration * number and number * duration
        if (left is Xdm.YearMonthDuration ymd && IsNumeric(right))
        {
            var d = ToDouble(right);
            if (double.IsNaN(d)) throw new XQueryRuntimeException("FOCA0005", "Cannot multiply duration by NaN");
            if (double.IsInfinity(d)) throw new XQueryRuntimeException("FODT0002", "Duration overflow");
            return ymd * d;
        }
        if (IsNumeric(left) && right is Xdm.YearMonthDuration ymd2)
        {
            var d = ToDouble(left);
            if (double.IsNaN(d)) throw new XQueryRuntimeException("FOCA0005", "Cannot multiply duration by NaN");
            if (double.IsInfinity(d)) throw new XQueryRuntimeException("FODT0002", "Duration overflow");
            return ymd2 * d;
        }
        if (left is TimeSpan ts && IsNumeric(right))
        {
            var d = ToDouble(right);
            if (double.IsNaN(d)) throw new XQueryRuntimeException("FOCA0005", "Cannot multiply duration by NaN");
            if (double.IsInfinity(d)) throw new XQueryRuntimeException("FODT0002", "Duration overflow");
            return TimeSpan.FromTicks((long)(ts.Ticks * d));
        }
        if (IsNumeric(left) && right is TimeSpan ts2)
        {
            var d = ToDouble(left);
            if (double.IsNaN(d)) throw new XQueryRuntimeException("FOCA0005", "Cannot multiply duration by NaN");
            if (double.IsInfinity(d)) throw new XQueryRuntimeException("FODT0002", "Duration overflow");
            return TimeSpan.FromTicks((long)(ts2.Ticks * d));
        }

        if (IsNumeric(left) && IsNumeric(right))
        {
            var (l, r) = PromoteNumeric(left, right);
            return (l, r) switch
            {
                (long a, long b) => LongMultiplyOrPromote(a, b),
                (BigInteger a, BigInteger b) => a * b,
                (float a, float b) => a * b,
                (double a, double b) => a * b,
                (decimal a, decimal b) => DecimalMultiplyOrPromote(a, b),
                _ => Convert.ToDouble(l) * Convert.ToDouble(r)
            };
        }
        return ToDouble(left) * ToDouble(right);
    }

    // Overflow-safe integer arithmetic: promotes to BigInteger when result exceeds Int64 range.
    private static object LongAddOrPromote(long a, long b)
    {
        try
        { return checked(a + b); }
        catch (OverflowException)
        {
            var result = (BigInteger)a + b;
            if (result.GetByteCount() > 1_000_000)
                throw new XQueryRuntimeException("FOAR0002", "Numeric result exceeds maximum supported size");
            return result;
        }
    }

    private static object LongSubtractOrPromote(long a, long b)
    {
        try
        { return checked(a - b); }
        catch (OverflowException)
        {
            var result = (BigInteger)a - b;
            if (result.GetByteCount() > 1_000_000)
                throw new XQueryRuntimeException("FOAR0002", "Numeric result exceeds maximum supported size");
            return result;
        }
    }

    private static object LongMultiplyOrPromote(long a, long b)
    {
        try
        { return checked(a * b); }
        catch (OverflowException)
        {
            var result = (BigInteger)a * b;
            if (result.GetByteCount() > 1_000_000)
                throw new XQueryRuntimeException("FOAR0002", "Numeric result exceeds maximum supported size");
            return result;
        }
    }

    private static object DecimalMultiplyOrPromote(decimal a, decimal b)
    {
        try
        { return a * b; }
        catch (OverflowException) { return (double)a * (double)b; }
    }

    private static object? Divide(object? left, object? right)
    {
        // XPath 2.0: arithmetic with empty sequence returns empty sequence
        if (left is null || right is null)
            return null;
        if (left is string || right is string)
            throw new XQueryRuntimeException("XPTY0004",
                "Arithmetic operator 'div' is not defined for xs:string");

        // Duration / number
        if (left is Xdm.YearMonthDuration ymd && IsNumeric(right))
        {
            var d = ToDouble(right);
            if (double.IsNaN(d)) throw new XQueryRuntimeException("FOCA0005", "Cannot divide duration by NaN");
            if (d == 0) throw new XQueryRuntimeException("FODT0002", "Duration division by zero");
            return new Xdm.YearMonthDuration((int)Math.Floor(ymd.TotalMonths / d + 0.5));
        }
        if (left is TimeSpan ts && IsNumeric(right))
        {
            var d = ToDouble(right);
            if (double.IsNaN(d)) throw new XQueryRuntimeException("FOCA0005", "Cannot divide duration by NaN");
            if (d == 0) throw new XQueryRuntimeException("FODT0002", "Duration division by zero");
            return TimeSpan.FromTicks((long)(ts.Ticks / d));
        }
        // Duration / duration = decimal
        if (left is Xdm.YearMonthDuration ya && right is Xdm.YearMonthDuration yb)
        {
            if (yb.TotalMonths == 0)
                throw new XQueryRuntimeException("FOAR0001", "Division by zero");
            return (decimal)ya.TotalMonths / yb.TotalMonths;
        }
        if (left is TimeSpan tsa && right is TimeSpan tsb)
        {
            if (tsb.Ticks == 0)
                throw new XQueryRuntimeException("FOAR0001", "Division by zero");
            return (decimal)tsa.Ticks / tsb.Ticks;
        }

        if (IsNumeric(left) && IsNumeric(right))
        {
            // XQuery: integer div integer = decimal
            if (left is int or long or BigInteger && right is int or long or BigInteger)
            {
                var ld = ToBigInteger(left);
                var rd = ToBigInteger(right);
                if (rd.IsZero)
                    throw new XQueryRuntimeException("FOAR0001", "Division by zero");
                // Use decimal when values fit, otherwise use double
                try
                {
                    return (decimal)ld / (decimal)rd;
                }
                catch (OverflowException)
                {
                    return (double)ld / (double)rd;
                }
            }
            var (l, r) = PromoteNumeric(left, right);
            return (l, r) switch
            {
                (decimal a, decimal b) when b != 0 => a / b,
                (decimal _, decimal _) => throw new XQueryRuntimeException("FOAR0001", "Division by zero"),
                (float a, float b) => a / b,
                (double a, double b) => a / b,
                _ => Convert.ToDouble(l) / Convert.ToDouble(r)
            };
        }
        return ToDouble(left) / ToDouble(right);
    }

    private static object? IntegerDivide(object? left, object? right)
    {
        // XPath 2.0: arithmetic with empty sequence returns empty sequence
        if (left is null || right is null)
            return null;

        left = QueryExecutionContext.Atomize(left);
        right = QueryExecutionContext.Atomize(right);
        // Float/double special cases
        if ((left is float lf && float.IsNaN(lf)) || (left is double ld2 && double.IsNaN(ld2)))
            throw new XQueryRuntimeException("FOAR0002", "Invalid argument for integer division: NaN");
        if ((left is float lf2 && float.IsInfinity(lf2)) || (left is double ld3 && double.IsInfinity(ld3)))
            throw new XQueryRuntimeException("FOAR0002", "Invalid argument for integer division: Infinity");
        if ((right is float rf && float.IsNaN(rf)) || (right is double rd2 && double.IsNaN(rd2)))
            throw new XQueryRuntimeException("FOAR0002", "Invalid argument for integer division: NaN");
        // x idiv INF = 0 when x is finite
        if ((right is float rf2 && float.IsInfinity(rf2)) || (right is double rd3 && double.IsInfinity(rd3)))
            return 0L;
        // BigInteger idiv
        if (left is BigInteger || right is BigInteger)
        {
            var lb = ToBigInteger(left);
            var rb = ToBigInteger(right);
            if (rb.IsZero)
                throw new XQueryRuntimeException("FOAR0001", "Division by zero");
            var result = BigInteger.Divide(lb, rb);
            // Narrow to long if possible
            if (result >= long.MinValue && result <= long.MaxValue)
                return (long)result;
            return result;
        }
        // Per XPath spec: A idiv B = truncate(A div B)
        // Must perform real division first, then truncate — not convert to int first
        if (left is float or double || right is float or double)
        {
            var dl = Convert.ToDouble(left is string sl ? ToDouble(sl) : left);
            var dr = Convert.ToDouble(right is string sr ? ToDouble(sr) : right);
            if (dr == 0)
                throw new XQueryRuntimeException("FOAR0001", "Division by zero");
            var quotient = Math.Truncate(dl / dr);
            if (double.IsInfinity(quotient) || double.IsNaN(quotient)
                || quotient > (double)long.MaxValue || quotient < (double)long.MinValue)
                throw new XQueryRuntimeException("FOAR0002",
                    "Integer overflow in integer division");
            return (long)quotient;
        }
        if (left is decimal or int or long || right is decimal)
        {
            try
            {
                var dl = Convert.ToDecimal(left is string sl2 ? ToDouble(sl2) : left);
                var dr = Convert.ToDecimal(right is string sr2 ? ToDouble(sr2) : right);
                if (dr == 0)
                    throw new XQueryRuntimeException("FOAR0001", "Division by zero");
                return (long)Math.Truncate(dl / dr);
            }
            catch (OverflowException)
            {
                throw new XQueryRuntimeException("FOAR0002",
                    "Integer overflow in integer division");
            }
        }
        var l = Convert.ToInt64(left is string sl3 ? ToDouble(sl3) : left);
        var r = Convert.ToInt64(right is string sr3 ? ToDouble(sr3) : right);
        if (r == 0)
            throw new XQueryRuntimeException("FOAR0001", "Division by zero");
        return l / r;
    }

    private static object? Modulo(object? left, object? right)
    {
        // XPath 2.0: arithmetic with empty sequence returns empty sequence
        if (left is null || right is null)
            return null;

        // Use PromoteNumeric for correct type promotion (float/double/decimal/untypedAtomic)
        var (pl, pr) = PromoteNumeric(left, right);

        return (pl, pr) switch
        {
            (float lf, float rf) => lf % rf, // IEEE 754: x mod 0 = NaN
            (double ld, double rd) => ld % rd, // IEEE 754: x mod 0 = NaN
            (decimal lm, decimal rm) when rm != 0 => lm % rm,
            (decimal _, decimal _) => throw new XQueryRuntimeException("FOAR0001", "Division by zero"),
            (BigInteger lb, BigInteger rb) when !rb.IsZero =>
                BigInteger.Remainder(lb, rb) is var r && r >= long.MinValue && r <= long.MaxValue ? (long)r : r,
            (BigInteger _, BigInteger _) => throw new XQueryRuntimeException("FOAR0001", "Division by zero"),
            (long ll, long rl) when rl != 0 => ll % rl,
            (long _, long _) => throw new XQueryRuntimeException("FOAR0001", "Division by zero"),
            _ => Convert.ToDouble(pl) % Convert.ToDouble(pr)
        };
    }

    private static bool IsNumeric(object? v) =>
        v is int or long or double or float or decimal or BigInteger;

    private static bool IsDurationType(object? v) =>
        v is Xdm.XsDuration or Xdm.YearMonthDuration or TimeSpan;

    /// <summary>
    /// Cross-type duration equality for eq/ne.
    /// xs:duration, xs:yearMonthDuration, and xs:dayTimeDuration are all comparable via eq/ne.
    /// </summary>
    private static bool DurationValueEqual(object left, object right)
    {
        var (lMonths, lTicks) = GetDurationComponents(left);
        var (rMonths, rTicks) = GetDurationComponents(right);
        return lMonths == rMonths && lTicks == rTicks;
    }

    private static (int months, long ticks) GetDurationComponents(object dur) => dur switch
    {
        Xdm.XsDuration d => (d.TotalMonths, d.DayTime.Ticks),
        Xdm.YearMonthDuration ymd => (ymd.TotalMonths, 0),
        TimeSpan ts => (0, ts.Ticks),
        _ => (0, 0)
    };

    private static BigInteger ToBigInteger(object? v) => v switch
    {
        BigInteger bi => bi,
        long l => l,
        int i => i,
        decimal m => (BigInteger)m,
        double d => (BigInteger)d,
        float f => (BigInteger)f,
        _ => (BigInteger)Convert.ToInt64(v)
    };

    private static bool IsComparisonOperator(BinaryOperator op) => op is
        BinaryOperator.Equal or BinaryOperator.NotEqual or
        BinaryOperator.GeneralEqual or BinaryOperator.GeneralNotEqual or
        BinaryOperator.LessThan or BinaryOperator.LessOrEqual or
        BinaryOperator.GreaterThan or BinaryOperator.GreaterOrEqual or
        BinaryOperator.GeneralLessThan or BinaryOperator.GeneralLessOrEqual or
        BinaryOperator.GeneralGreaterThan or BinaryOperator.GeneralGreaterOrEqual;

    private static double ToDouble(object? v)
    {
        v = QueryExecutionContext.Atomize(v);
        if (v is string s)
            return double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : double.NaN;
        if (v is BigInteger bi)
            return (double)bi;
        return Convert.ToDouble(v);
    }

    private static float ToFloat(object? v)
    {
        v = QueryExecutionContext.Atomize(v);
        if (v is float f) return f;
        if (v is string s)
            return float.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var fp) ? fp : float.NaN;
        if (v is BigInteger bi)
            return (float)bi;
        return Convert.ToSingle(v);
    }

    /// <summary>
    /// Converts to double, throwing FORG0001 if a string value cannot be cast.
    /// Used in XPath 2.0+ general comparisons where untypedAtomic→double cast failure is an error.
    /// </summary>
    private static double ToDoubleOrThrow(object? v)
    {
        v = QueryExecutionContext.Atomize(v);
        if (v is string s)
        {
            s = s.Trim();
            if (s == "INF")
                return double.PositiveInfinity;
            if (s == "-INF")
                return double.NegativeInfinity;
            if (s == "NaN")
                return double.NaN;
            if (double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d))
                return d;
            throw new Functions.XQueryException("FORG0001", $"Cannot cast '{s}' to xs:double");
        }
        if (v is BigInteger bi)
            return (double)bi;
        return Convert.ToDouble(v);
    }

    private static bool ValueEquals(object? left, object? right, StringComparison stringComparison = StringComparison.Ordinal)
    {
        // Atomize XDM nodes before comparison
        left = QueryExecutionContext.Atomize(left);
        right = QueryExecutionContext.Atomize(right);

        if (left is null && right is null)
            return true;
        if (left is null || right is null)
            return false;

        // XPath general comparison: if one operand is xs:untypedAtomic (string) and
        // the other is numeric, cast the string to xs:double
        var leftIsString = left is string;
        var rightIsString = right is string;
        var leftIsNumeric = IsNumeric(left);
        var rightIsNumeric = IsNumeric(right);

        if ((leftIsString && rightIsNumeric) || (rightIsString && leftIsNumeric))
        {
            var ld = ToDoubleOrThrow(left);
            var rd = ToDoubleOrThrow(right);
            // NaN != NaN per XQuery/IEEE 754
            if (double.IsNaN(ld) || double.IsNaN(rd))
                return false;
            return ld == rd;
        }

        // Both numeric — promote and compare
        if (leftIsNumeric && rightIsNumeric)
        {
            // If either is double, compare as double.
            if (left is double || right is double)
            {
                var ld = ToDouble(left);
                var rd = ToDouble(right);
                // NaN != NaN per XQuery/IEEE 754
                if (double.IsNaN(ld) || double.IsNaN(rd))
                    return false;
                return ld == rd;
            }
            // Otherwise, if either is xs:float (but neither is double), numeric promotion
            // promotes the other operand to xs:float and compares as float (QT3 K-SeqExprCast-81).
            if (left is float || right is float)
            {
                var lf = ToFloat(left);
                var rf = ToFloat(right);
                if (float.IsNaN(lf) || float.IsNaN(rf))
                    return false;
                return lf == rf;
            }
            // BigInteger comparison
            if (left is BigInteger || right is BigInteger)
                return ToBigInteger(left) == ToBigInteger(right);
            // Both are integer or decimal — compare as decimal for precision
            return Convert.ToDecimal(left) == Convert.ToDecimal(right);
        }

        // Boolean comparison
        if (left is bool lb && right is bool rb)
            return lb == rb;

        // QName comparison — namespace URI + local name per XPath spec
        // QName can only be compared with QName; other types are XPTY0004
        if (left is QName || right is QName)
        {
            if (left is QName lq && right is QName rq)
            {
                if (lq.LocalName != rq.LocalName)
                    return false;
                var lUri = lq.ResolvedNamespace;
                var rUri = rq.ResolvedNamespace;
                if (lUri != null && rUri != null)
                    return lUri == rUri;
                return lq.Namespace == rq.Namespace;
            }
            throw new Functions.XQueryException("XPTY0004",
                "Cannot compare xs:QName with a non-QName value");
        }

        // Date/time comparison — normalize to UTC before comparing
        if (left is Xdm.XsDateTime ldt && right is Xdm.XsDateTime rdt)
            return ldt.CompareTo(rdt) == 0;
        if (left is Xdm.XsDate ld2 && right is Xdm.XsDate rd2)
            return ld2.CompareTo(rd2) == 0;
        if (left is Xdm.XsTime lt && right is Xdm.XsTime rt)
            return lt.CompareTo(rt) == 0;

        // Duration comparison — cross-type equality for eq/ne
        if (IsDurationType(left) && IsDurationType(right))
            return DurationValueEqual(left, right);

        // Binary comparison — compare underlying byte arrays
        if (left is Xdm.XdmValue lv && right is Xdm.XdmValue rv
            && lv.Type == rv.Type
            && lv.RawValue is byte[] lBytes && rv.RawValue is byte[] rBytes)
            return lBytes.AsSpan().SequenceEqual(rBytes);

        // Gregorian date component types: convert to reference xs:dateTime and compare.
        // Per F&O 4.0 op:gDay-equal etc., the operand's timezone (explicit or implicit)
        // applies to the reference dateTime, so values differing only in timezone but
        // representing the same UTC instant are equal.
        if (left is Xdm.XsGDay lgd && right is Xdm.XsGDay rgd)
            return GregorianToUtcTicks(lgd.Value, GregorianKind.GDay)
                == GregorianToUtcTicks(rgd.Value, GregorianKind.GDay);
        if (left is Xdm.XsGMonth lgm && right is Xdm.XsGMonth rgm)
            return GregorianToUtcTicks(lgm.Value, GregorianKind.GMonth)
                == GregorianToUtcTicks(rgm.Value, GregorianKind.GMonth);
        if (left is Xdm.XsGYear lgy && right is Xdm.XsGYear rgy)
            return GregorianToUtcTicks(lgy.Value, GregorianKind.GYear)
                == GregorianToUtcTicks(rgy.Value, GregorianKind.GYear);
        if (left is Xdm.XsGYearMonth lgym && right is Xdm.XsGYearMonth rgym)
            return GregorianToUtcTicks(lgym.Value, GregorianKind.GYearMonth)
                == GregorianToUtcTicks(rgym.Value, GregorianKind.GYearMonth);
        if (left is Xdm.XsGMonthDay lgmd && right is Xdm.XsGMonthDay rgmd)
            return GregorianToUtcTicks(lgmd.Value, GregorianKind.GMonthDay)
                == GregorianToUtcTicks(rgmd.Value, GregorianKind.GMonthDay);

        // String comparison using XPath canonical string representations
        return string.Equals(
            Functions.ConcatFunction.XQueryStringValue(left),
            Functions.ConcatFunction.XQueryStringValue(right),
            stringComparison);
    }

    private enum GregorianKind { GYear, GMonth, GDay, GYearMonth, GMonthDay }

    /// <summary>
    /// Converts a Gregorian date component lexical value to UTC ticks using the reference
    /// xs:dateTime (1972-12-31T00:00:00 for gYear/gMonth/gDay/gYearMonth, where the year
    /// is copied from gYear/gYearMonth and month from gMonth/gYearMonth/gMonthDay and day
    /// from gDay/gMonthDay; missing components default to the reference 1972-12-31).
    /// The operand's timezone (explicit or implicit) is applied to shift to UTC.
    /// </summary>
    private static long GregorianToUtcTicks(string lex, GregorianKind kind)
    {
        // Strip and parse timezone suffix (Z | ±HH:MM). Empty => no timezone (use implicit).
        TimeSpan? tz = null;
        string body = lex;
        if (body.EndsWith('Z'))
        {
            tz = TimeSpan.Zero;
            body = body[..^1];
        }
        else
        {
            // Look for +HH:MM or -HH:MM at the end
            int len = body.Length;
            if (len >= 6 && body[len - 3] == ':' && (body[len - 6] == '+' || body[len - 6] == '-'))
            {
                var sign = body[len - 6] == '+' ? 1 : -1;
                if (int.TryParse(body.AsSpan(len - 5, 2), out var hh)
                    && int.TryParse(body.AsSpan(len - 2, 2), out var mm))
                {
                    tz = TimeSpan.FromMinutes(sign * (hh * 60 + mm));
                    body = body[..(len - 6)];
                }
            }
        }

        int year = 1972, month = 12, day = 31;
        switch (kind)
        {
            case GregorianKind.GYear:
                // Lexical form: [-]YYYY[+-]HH:MM or [-]YYYY Z
                _ = int.TryParse(body, out year);
                break;
            case GregorianKind.GMonth:
                // Lexical form: --MM
                if (body.StartsWith("--", StringComparison.Ordinal) && body.Length >= 4)
                    _ = int.TryParse(body.AsSpan(2, 2), out month);
                break;
            case GregorianKind.GDay:
                // Lexical form: ---DD
                if (body.StartsWith("---", StringComparison.Ordinal) && body.Length >= 5)
                    _ = int.TryParse(body.AsSpan(3, 2), out day);
                break;
            case GregorianKind.GYearMonth:
                // Lexical form: [-]YYYY-MM
                {
                    var dash = body.LastIndexOf('-');
                    if (dash > 0 && dash < body.Length - 1)
                    {
                        _ = int.TryParse(body.AsSpan(0, dash), out year);
                        _ = int.TryParse(body.AsSpan(dash + 1), out month);
                    }
                }
                break;
            case GregorianKind.GMonthDay:
                // Lexical form: --MM-DD
                if (body.StartsWith("--", StringComparison.Ordinal) && body.Length >= 7)
                {
                    _ = int.TryParse(body.AsSpan(2, 2), out month);
                    _ = int.TryParse(body.AsSpan(5, 2), out day);
                }
                break;
        }

        // Clamp day to valid range for the month (e.g., Feb 29 in leap year)
        var daysInMonth = DateTime.DaysInMonth(Math.Abs(year), Math.Max(1, Math.Min(12, month)));
        day = Math.Max(1, Math.Min(day, daysInMonth));
        month = Math.Max(1, Math.Min(12, month));

        // Build local DateTime and apply timezone offset to get UTC ticks
        var local = new DateTime(Math.Max(1, Math.Abs(year)), month, day, 0, 0, 0, DateTimeKind.Unspecified);
        var offset = tz ?? TimeSpan.Zero; // implicit = UTC
        var utc = local - offset;
        return utc.Ticks;
    }

    private static int ValueCompare(object? left, object? right, StringComparison stringComparison = StringComparison.Ordinal)
    {
        // Atomize XDM nodes before comparison
        left = QueryExecutionContext.Atomize(left);
        right = QueryExecutionContext.Atomize(right);

        // Unwrap XsTypedString and XsTypedInteger to plain values
        if (left is Xdm.XsTypedString tsLeft) left = tsLeft.Value;
        if (right is Xdm.XsTypedString tsRight) right = tsRight.Value;
        if (left is Xdm.XsTypedInteger tiLeft) left = tiLeft.Value;
        if (right is Xdm.XsTypedInteger tiRight) right = tiRight.Value;

        if (left is null && right is null)
            return 0;
        if (left is null)
            return -1;
        if (right is null)
            return 1;

        // XPath general comparison: if one operand is xs:untypedAtomic (string) and
        // the other is numeric, cast the string to xs:double
        var leftIsString = left is string;
        var rightIsString = right is string;
        var leftIsNumeric = IsNumeric(left);
        var rightIsNumeric = IsNumeric(right);

        if ((leftIsString && rightIsNumeric) || (rightIsString && leftIsNumeric))
        {
            var ld = ToDoubleOrThrow(left);
            var rd = ToDoubleOrThrow(right);
            return ld.CompareTo(rd);
        }

        // Both numeric — promote and compare
        if (leftIsNumeric && rightIsNumeric)
        {
            if (left is double or float || right is double or float)
            {
                var ld = ToDouble(left);
                var rd = ToDouble(right);
                return ld.CompareTo(rd);
            }
            if (left is BigInteger || right is BigInteger)
                return ToBigInteger(left).CompareTo(ToBigInteger(right));
            return Convert.ToDecimal(left).CompareTo(Convert.ToDecimal(right));
        }

        // Boolean comparison
        if (left is bool lb && right is bool rb)
            return lb.CompareTo(rb);

        // Date/time comparison — normalize to UTC before comparing
        if (left is Xdm.XsDateTime ldt && right is Xdm.XsDateTime rdt)
            return ldt.CompareTo(rdt);
        if (left is Xdm.XsDate ld2 && right is Xdm.XsDate rd2)
            return ld2.CompareTo(rd2);
        if (left is Xdm.XsTime lt && right is Xdm.XsTime rt)
            return lt.CompareTo(rt);

        // Duration comparison — yearMonthDuration and dayTimeDuration support ordering
        // but only within the same subtype. Cross-type (YMD vs DTD) and xs:duration are not ordered.
        if (left is Xdm.YearMonthDuration lym && right is Xdm.YearMonthDuration rym)
            return lym.CompareTo(rym);
        if (left is TimeSpan lts && right is TimeSpan rts)
            return lts.CompareTo(rts);
        // xs:duration is only eq/ne, not ordered
        if (IsDurationType(left) && IsDurationType(right))
            throw new Functions.XQueryException("XPTY0004",
                "Duration values are not ordered — cannot use lt, gt, le, ge for xs:duration or cross-type duration comparison");

        // QName only supports eq/ne, not ordering
        if (left is QName || right is QName)
            throw new Functions.XQueryException("XPTY0004",
                "Values of type xs:QName are not ordered — cannot use lt, gt, le, ge");

        // Gregorian date component types only support eq/ne, not ordering
        if (left is Xdm.XsGYear or Xdm.XsGMonth or Xdm.XsGDay or Xdm.XsGMonthDay or Xdm.XsGYearMonth ||
            right is Xdm.XsGYear or Xdm.XsGMonth or Xdm.XsGDay or Xdm.XsGMonthDay or Xdm.XsGYearMonth)
            throw new Functions.XQueryException("XPTY0004",
                "Gregorian date component types are not ordered — cannot use lt, gt, le, ge");

        // Binary comparison — octet-by-octet (unsigned) ordering of the underlying bytes.
        // Cross-type (hexBinary vs base64Binary) and binary vs string are XPTY0004.
        if (left is Xdm.XdmValue lbv && lbv.RawValue is byte[])
        {
            if (right is Xdm.XdmValue rbv && rbv.RawValue is byte[] rBytes && lbv.Type == rbv.Type)
                return ((byte[])lbv.RawValue!).AsSpan().SequenceCompareTo(rBytes);
            throw new Functions.XQueryException("XPTY0004",
                $"Cannot compare {lbv.Type} with {right.GetType().Name}");
        }
        if (right is Xdm.XdmValue rbv2 && rbv2.RawValue is byte[])
            throw new Functions.XQueryException("XPTY0004",
                $"Cannot compare {left.GetType().Name} with {rbv2.Type}");

        // String comparison using XPath canonical string representations
        // Use CollationHelper.CompareStrings for correct Unicode codepoint ordering
        // (.NET's string.Compare with Ordinal uses UTF-16 code unit ordering which
        // differs from codepoint ordering for supplementary characters)
        return Functions.CollationHelper.CompareStrings(
            Functions.ConcatFunction.XQueryStringValue(left),
            Functions.ConcatFunction.XQueryStringValue(right),
            stringComparison);
    }
}
