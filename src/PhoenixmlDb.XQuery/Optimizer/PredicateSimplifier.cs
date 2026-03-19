using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Optimizer;

/// <summary>
/// Simplifies predicates and boolean expressions.
/// </summary>
public sealed class PredicateSimplifier : XQueryExpressionRewriter
{
    public override XQueryExpression VisitBinaryExpression(BinaryExpression expr)
    {
        var left = Rewrite(expr.Left);
        var right = Rewrite(expr.Right);

        // Simplify AND with true/false
        if (expr.Operator == BinaryOperator.And)
        {
            if (left is BooleanLiteral { Value: true })
                return right;
            if (right is BooleanLiteral { Value: true })
                return left;
            if (left is BooleanLiteral { Value: false } || right is BooleanLiteral { Value: false })
                return new BooleanLiteral { Value = false, Location = expr.Location };
        }

        // Simplify OR with true/false
        if (expr.Operator == BinaryOperator.Or)
        {
            if (left is BooleanLiteral { Value: false })
                return right;
            if (right is BooleanLiteral { Value: false })
                return left;
            if (left is BooleanLiteral { Value: true } || right is BooleanLiteral { Value: true })
                return new BooleanLiteral { Value = true, Location = expr.Location };
        }

        // Simplify comparisons with self (x = x -> true for atomic values)
        if ((expr.Operator == BinaryOperator.Equal || expr.Operator == BinaryOperator.GeneralEqual) &&
            AreEquivalent(left, right))
        {
            // Note: Only safe for atomic non-NaN values, so we check for literals
            if (left is IntegerLiteral or StringLiteral or BooleanLiteral)
            {
                return new BooleanLiteral { Value = true, Location = expr.Location };
            }
        }

        // Simplify x != x -> false (for deterministic expressions)
        if ((expr.Operator == BinaryOperator.NotEqual || expr.Operator == BinaryOperator.GeneralNotEqual) &&
            AreEquivalent(left, right))
        {
            if (left is IntegerLiteral or StringLiteral or BooleanLiteral)
            {
                return new BooleanLiteral { Value = false, Location = expr.Location };
            }
        }

        // Simplify arithmetic identities
        if (expr.Operator == BinaryOperator.Add)
        {
            // x + 0 -> x
            if (right is IntegerLiteral { LongValue: 0 })
                return left;
            if (left is IntegerLiteral { LongValue: 0 })
                return right;
        }

        if (expr.Operator == BinaryOperator.Subtract)
        {
            // x - 0 -> x
            if (right is IntegerLiteral { LongValue: 0 })
                return left;
        }

        if (expr.Operator == BinaryOperator.Multiply)
        {
            // x * 1 -> x
            if (right is IntegerLiteral { LongValue: 1 })
                return left;
            if (left is IntegerLiteral { LongValue: 1 })
                return right;
            // x * 0 -> 0
            if (right is IntegerLiteral { LongValue: 0 } || left is IntegerLiteral { LongValue: 0 })
                return new IntegerLiteral { Value = 0L, Location = expr.Location };
        }

        if (expr.Operator == BinaryOperator.Divide)
        {
            // x div 1 -> x
            if (right is IntegerLiteral { LongValue: 1 })
                return left;
        }

        if (left == expr.Left && right == expr.Right)
            return expr;

        return new BinaryExpression
        {
            Left = left,
            Operator = expr.Operator,
            Right = right,
            Location = expr.Location
        };
    }

    public override XQueryExpression VisitUnaryExpression(UnaryExpression expr)
    {
        var operand = Rewrite(expr.Operand);

        // Double negation elimination: not(not(x)) -> x
        if (expr.Operator == UnaryOperator.Not &&
            operand is UnaryExpression { Operator: UnaryOperator.Not } inner)
        {
            return inner.Operand;
        }

        // NOTE: not(x eq y) -> x ne y is NOT safe in XPath 3-valued logic.
        // Value comparisons return empty sequence when either operand is absent:
        //   not("a" eq ()) = not(()) = not(false) = true
        //   "a" ne ()      = ()      → EBV = false
        // This optimization would change the result, so we do NOT apply it.

        if (operand == expr.Operand)
            return expr;

        return new UnaryExpression
        {
            Operator = expr.Operator,
            Operand = operand,
            Location = expr.Location
        };
    }

    public override XQueryExpression VisitFlworExpression(FlworExpression expr)
    {
        var clauses = new List<FlworClause>();
        var changed = false;

        foreach (var clause in expr.Clauses)
        {
            // Simplify where clauses
            if (clause is WhereClause wc)
            {
                var condition = Rewrite(wc.Condition);

                // Remove where true
                if (condition is BooleanLiteral { Value: true })
                {
                    changed = true;
                    continue;
                }

                // where false means empty result - but we keep it for now
                // as it may affect error semantics

                if (condition != wc.Condition)
                {
                    clauses.Add(new WhereClause { Condition = condition });
                    changed = true;
                    continue;
                }
            }

            clauses.Add(clause);
        }

        var returnExpr = Rewrite(expr.ReturnExpression);
        if (returnExpr != expr.ReturnExpression)
            changed = true;

        if (!changed)
            return expr;

        return new FlworExpression
        {
            Clauses = clauses,
            ReturnExpression = returnExpr,
            Location = expr.Location
        };
    }

    private static bool AreEquivalent(XQueryExpression left, XQueryExpression right)
    {
        return (left, right) switch
        {
            (IntegerLiteral a, IntegerLiteral b) => Equals(a.Value, b.Value),
            (StringLiteral a, StringLiteral b) => a.Value == b.Value,
            (BooleanLiteral a, BooleanLiteral b) => a.Value == b.Value,
            (DoubleLiteral a, DoubleLiteral b) => a.Value == b.Value && !double.IsNaN(a.Value),
            (VariableReference a, VariableReference b) =>
                a.Name.LocalName == b.Name.LocalName && a.Name.Namespace == b.Name.Namespace,
            _ => false
        };
    }
}
