using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Optimizer;

/// <summary>
/// Recognizes <c>/element[@attr op literal]</c> shapes covered by a value index
/// and emits an <see cref="IndexLookupOperator"/>. Supports equality
/// (<c>=</c>, <c>eq</c>) and range comparisons (<c>&lt;</c>, <c>&lt;=</c>,
/// <c>&gt;</c>, <c>&gt;=</c>, <c>lt</c>, <c>le</c>, <c>gt</c>, <c>ge</c>)
/// with the literal on either side. Falls through (returns null) for anything
/// it can't classify, allowing the default path planner to handle it.
/// </summary>
public sealed class IndexAwareQueryPlanOptimizer : IQueryPlanOptimizer
{
    private readonly IIndexCatalog _catalog;

    public IndexAwareQueryPlanOptimizer(IIndexCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    /// <inheritdoc />
    public PhysicalOperator? OptimizePath(PathExpression path, ContainerId container)
    {
        if (!path.IsAbsolute) return null;
        if (path.InitialExpression != null) return null;
        if (path.Steps.Count < 1) return null;

        var elementPath = new string[path.Steps.Count];
        for (int i = 0; i < path.Steps.Count; i++)
        {
            var s = path.Steps[i];
            if (s.Axis != Axis.Child) return null;
            if (s.NodeTest is not NameTest n || n.IsLocalNameWildcard) return null;
            elementPath[i] = n.LocalName;
            bool isLast = i == path.Steps.Count - 1;
            if (!isLast && s.Predicates.Count != 0) return null;
            if (isLast && s.Predicates.Count != 1) return null;
        }

        if (!TryMatchAttributePredicate(path.Steps[^1].Predicates[0], out var attrName, out var predicate))
            return null;

        var coverage = _catalog.LookupValueIndex(container, elementPath, attrName!);
        if (coverage == null) return null;

        return new IndexLookupOperator { IndexName = coverage.IndexName, Predicate = predicate! };
    }

    /// <summary>
    /// Tries to match a predicate expression of the form <c>@attr op literal</c>
    /// or <c>literal op @attr</c> (equality or range comparison).
    /// </summary>
    private static bool TryMatchAttributePredicate(
        XQueryExpression expr,
        out string? attrName,
        out IndexPredicate? predicate)
    {
        attrName = null;
        predicate = null;

        if (expr is not BinaryExpression bin) return false;

        // Determine which side holds the attribute path and which holds the literal.
        string? attr;
        object litValue;
        BinaryOperator op;

        var rightLitValue = ExtractLiteralValue(bin.Right);
        var leftLitValue  = ExtractLiteralValue(bin.Left);

        if (TryExtractAttributeName(bin.Left, out attr) && rightLitValue is not null)
        {
            // @attr op literal — natural order
            op = bin.Operator;
            litValue = rightLitValue;
        }
        else if (TryExtractAttributeName(bin.Right, out attr) && leftLitValue is not null)
        {
            // literal op @attr — flip the operator so semantics are attr-centric
            op = Flip(bin.Operator);
            litValue = leftLitValue;
        }
        else
        {
            return false;
        }

        predicate = op switch
        {
            BinaryOperator.GeneralEqual or BinaryOperator.Equal
                => new IndexEquality(litValue),

            BinaryOperator.GeneralLessThan or BinaryOperator.LessThan
                => new IndexRange(null, false, litValue, false),

            BinaryOperator.GeneralLessOrEqual or BinaryOperator.LessOrEqual
                => new IndexRange(null, false, litValue, true),

            BinaryOperator.GeneralGreaterThan or BinaryOperator.GreaterThan
                => new IndexRange(litValue, false, null, false),

            BinaryOperator.GeneralGreaterOrEqual or BinaryOperator.GreaterOrEqual
                => new IndexRange(litValue, true, null, false),

            _ => null
        };

        if (predicate == null) return false;

        attrName = attr;
        return true;
    }

    /// <summary>
    /// Returns the literal's value as an object (string, long/BigInteger, decimal, double, bool)
    /// for index lookup, or null if not a recognized literal type.
    /// </summary>
    private static object? ExtractLiteralValue(XQueryExpression expr) => expr switch
    {
        StringLiteral  s  => s.Value,
        IntegerLiteral i  => i.Value,   // long or BigInteger
        DecimalLiteral d  => d.Value,
        DoubleLiteral  db => db.Value,
        BooleanLiteral b  => b.Value,
        _                 => null,
    };

    /// <summary>
    /// Returns true and sets <paramref name="attrName"/> if <paramref name="expr"/>
    /// is a bare relative attribute path (<c>@name</c> — single attribute-axis step,
    /// no sub-predicates, non-wildcard).
    /// </summary>
    private static bool TryExtractAttributeName(XQueryExpression expr, out string? attrName)
    {
        attrName = null;
        if (expr is not PathExpression attrPath) return false;
        if (attrPath.IsAbsolute) return false;
        if (attrPath.InitialExpression != null) return false;
        if (attrPath.Steps.Count != 1) return false;
        var step = attrPath.Steps[0];
        if (step.Axis != Axis.Attribute) return false;
        if (step.NodeTest is not NameTest nt) return false;
        if (nt.IsLocalNameWildcard) return false;
        if (step.Predicates.Count != 0) return false;
        attrName = nt.LocalName;
        return true;
    }

    /// <summary>
    /// Flips a comparison operator so that <c>literal op @attr</c> becomes the
    /// semantically equivalent <c>@attr flipped(op) literal</c>.
    /// </summary>
    private static BinaryOperator Flip(BinaryOperator op) => op switch
    {
        BinaryOperator.GeneralLessThan        => BinaryOperator.GeneralGreaterThan,
        BinaryOperator.GeneralLessOrEqual     => BinaryOperator.GeneralGreaterOrEqual,
        BinaryOperator.GeneralGreaterThan     => BinaryOperator.GeneralLessThan,
        BinaryOperator.GeneralGreaterOrEqual  => BinaryOperator.GeneralLessOrEqual,
        BinaryOperator.LessThan               => BinaryOperator.GreaterThan,
        BinaryOperator.LessOrEqual            => BinaryOperator.GreaterOrEqual,
        BinaryOperator.GreaterThan            => BinaryOperator.LessThan,
        BinaryOperator.GreaterOrEqual         => BinaryOperator.LessOrEqual,
        _                                     => op  // equality is symmetric
    };
}
