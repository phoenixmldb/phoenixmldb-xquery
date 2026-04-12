using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Optimizer;

/// <summary>
/// Folds constant expressions at compile time.
/// E.g., 1 + 2 becomes 3.
/// </summary>
public sealed class ConstantFolder : XQueryExpressionRewriter
{
    /// <summary>
    /// When true, arithmetic on integers produces doubles (XPath 1.0 backwards-compatible mode).
    /// </summary>
    public bool BackwardsCompatible { get; init; }

    public override XQueryExpression VisitBinaryExpression(BinaryExpression expr)
    {
        var left = Rewrite(expr.Left);
        var right = Rewrite(expr.Right);

        // Try to fold arithmetic operations on literals (only for long values, not BigInteger)
        if (left is IntegerLiteral li && right is IntegerLiteral ri
            && li.Value is long lv && ri.Value is long rv)
        {
            // In backwards-compatible mode, arithmetic always produces doubles
            if (BackwardsCompatible && expr.Operator is BinaryOperator.Add or BinaryOperator.Subtract
                or BinaryOperator.Multiply or BinaryOperator.Divide or BinaryOperator.Modulo)
            {
                var doubleResult = expr.Operator switch
                {
                    BinaryOperator.Add => (double)lv + rv,
                    BinaryOperator.Subtract => (double)lv - rv,
                    BinaryOperator.Multiply => (double)lv * rv,
                    BinaryOperator.Divide when rv != 0 => (double)lv / rv,
                    BinaryOperator.Modulo when rv != 0 => (double)lv % rv,
                    _ => (double?)null
                };
                if (doubleResult.HasValue)
                    return new DoubleLiteral { Value = doubleResult.Value, Location = expr.Location };
            }

            // XQuery div returns decimal for integer operands, idiv returns integer
            if (expr.Operator == BinaryOperator.Divide && rv != 0)
            {
                // div: integer / integer = decimal (per XQuery spec)
                var decimalResult = (decimal)lv / rv;
                return new DecimalLiteral { Value = decimalResult, Location = expr.Location };
            }

            var result = expr.Operator switch
            {
                BinaryOperator.Add => lv + rv,
                BinaryOperator.Subtract => lv - rv,
                BinaryOperator.Multiply => lv * rv,
                BinaryOperator.IntegerDivide when rv != 0 => lv / rv,
                BinaryOperator.Modulo when rv != 0 => lv % rv,
                _ => (long?)null
            };

            if (result.HasValue)
            {
                return new IntegerLiteral { Value = result.Value, Location = expr.Location };
            }
        }

        if (left is DoubleLiteral ld && right is DoubleLiteral rd)
        {
            var result = expr.Operator switch
            {
                BinaryOperator.Add => ld.Value + rd.Value,
                BinaryOperator.Subtract => ld.Value - rd.Value,
                BinaryOperator.Multiply => ld.Value * rd.Value,
                BinaryOperator.Divide => ld.Value / rd.Value,
                _ => (double?)null
            };

            if (result.HasValue)
            {
                return new DoubleLiteral { Value = result.Value, Location = expr.Location };
            }
        }

        // Fold string concatenation
        if (expr.Operator == BinaryOperator.Concat &&
            left is StringLiteral ls && right is StringLiteral rs)
        {
            return new StringLiteral { Value = ls.Value + rs.Value, Location = expr.Location };
        }

        // Fold boolean operations
        if (left is BooleanLiteral lb && right is BooleanLiteral rb)
        {
            var result = expr.Operator switch
            {
                BinaryOperator.And => lb.Value && rb.Value,
                BinaryOperator.Or => lb.Value || rb.Value,
                _ => (bool?)null
            };

            if (result.HasValue)
            {
                return new BooleanLiteral { Value = result.Value, Location = expr.Location };
            }
        }

        // Short-circuit boolean operations
        if (expr.Operator == BinaryOperator.And && left is BooleanLiteral { Value: false })
        {
            return new BooleanLiteral { Value = false, Location = expr.Location };
        }

        if (expr.Operator == BinaryOperator.Or && left is BooleanLiteral { Value: true })
        {
            return new BooleanLiteral { Value = true, Location = expr.Location };
        }

        // Fold comparisons (only for long values, not BigInteger)
        if (left is IntegerLiteral cmpLi && right is IntegerLiteral cmpRi
            && cmpLi.Value is long cmpLv && cmpRi.Value is long cmpRv)
        {
            var result = expr.Operator switch
            {
                BinaryOperator.Equal or BinaryOperator.GeneralEqual => cmpLv == cmpRv,
                BinaryOperator.NotEqual or BinaryOperator.GeneralNotEqual => cmpLv != cmpRv,
                BinaryOperator.LessThan or BinaryOperator.GeneralLessThan => cmpLv < cmpRv,
                BinaryOperator.LessOrEqual or BinaryOperator.GeneralLessOrEqual => cmpLv <= cmpRv,
                BinaryOperator.GreaterThan or BinaryOperator.GeneralGreaterThan => cmpLv > cmpRv,
                BinaryOperator.GreaterOrEqual or BinaryOperator.GeneralGreaterOrEqual => cmpLv >= cmpRv,
                _ => (bool?)null
            };

            if (result.HasValue)
            {
                return new BooleanLiteral { Value = result.Value, Location = expr.Location };
            }
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

        // Fold unary minus on literals
        if (expr.Operator == UnaryOperator.Minus)
        {
            if (operand is IntegerLiteral li)
            {
                // In backward-compatible mode (XPath 1.0), all numeric literals are doubles
                if (BackwardsCompatible && (li.Value is long or int))
                    return new DoubleLiteral { Value = -Convert.ToDouble(li.Value), Location = expr.Location };
                object negated = li.Value is long lv ? (object)(-lv)
                    : li.Value is int iv ? (object)(-(long)iv)
                    : -(System.Numerics.BigInteger)li.Value;
                return new IntegerLiteral { Value = negated, Location = expr.Location };
            }
            if (operand is DoubleLiteral ld)
            {
                return new DoubleLiteral { Value = -ld.Value, Location = expr.Location };
            }
            if (operand is DecimalLiteral ldec)
            {
                return new DecimalLiteral { Value = -ldec.Value, Location = expr.Location };
            }
        }

        // Fold unary plus (no-op)
        if (expr.Operator == UnaryOperator.Plus)
        {
            if (operand is IntegerLiteral or DoubleLiteral or DecimalLiteral)
            {
                return operand;
            }
        }

        // Fold not
        if (expr.Operator == UnaryOperator.Not && operand is BooleanLiteral lb)
        {
            return new BooleanLiteral { Value = !lb.Value, Location = expr.Location };
        }

        // NOTE: not(not(x)) -> x is NOT safe because not() always returns xs:boolean.
        // not(not(42)) must return true, not 42. Skipping this optimization.

        if (operand == expr.Operand)
            return expr;

        return new UnaryExpression
        {
            Operator = expr.Operator,
            Operand = operand,
            Location = expr.Location
        };
    }

    public override XQueryExpression VisitIfExpression(IfExpression expr)
    {
        var condition = Rewrite(expr.Condition);

        // If condition is constant, eliminate the branch
        if (condition is BooleanLiteral lb)
        {
            return lb.Value
                ? Rewrite(expr.Then)
                : expr.Else != null ? Rewrite(expr.Else) : EmptySequence.Instance;
        }

        var then = Rewrite(expr.Then);
        var @else = expr.Else != null ? Rewrite(expr.Else) : null;

        if (condition == expr.Condition && then == expr.Then && @else == expr.Else)
            return expr;

        return new IfExpression
        {
            Condition = condition,
            Then = then,
            Else = @else,
            Location = expr.Location
        };
    }
}
