using System.Globalization;
using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Execution.FullTextMatching;

/// <summary>
/// The values of the XQuery expressions embedded in a selection, evaluated once for the whole
/// <c>contains text</c> in its dynamic context (Full Text 3.0 §4.3, step 1).
/// </summary>
internal sealed class FtEvaluatedExpressions
{
    private readonly Dictionary<XQueryExpression, List<object?>> _values = new(ReferenceEqualityComparer.Instance);

    public static async ValueTask<FtEvaluatedExpressions> EvaluateAsync(
        IReadOnlyDictionary<XQueryExpression, PhysicalOperator> operators, QueryExecutionContext context)
    {
        var result = new FtEvaluatedExpressions();
        foreach (var (expression, op) in operators)
        {
            var items = new List<object?>();
            await foreach (var item in op.ExecuteGuarded(context).ConfigureAwait(false))
                items.Add(context.AtomizeWithNodes(item));
            result._values[expression] = Flatten(items);
        }
        return result;
    }

    private static List<object?> Flatten(List<object?> items)
    {
        var flat = new List<object?>(items.Count);
        foreach (var item in items)
        {
            if (item is object?[] array) flat.AddRange(array);
            else if (item is List<object?> list) flat.AddRange(list);
            else if (item is not null) flat.Add(item);
        }
        return flat;
    }

    /// <summary>
    /// The search strings of <c>{expr}</c>: xs:string* under the function conversion rules, so
    /// an untyped or xs:anyURI value is a string and any other type is XPTY0004 (§3.2).
    /// </summary>
    public List<string> Strings(XQueryExpression expression)
        => _values[expression].Select(v => v switch
        {
            string s => s,
            Xdm.XsUntypedAtomic u => u.ToString() ?? "",
            Xdm.XsAnyUri uri => uri.Value,
            Xdm.XsTypedString t => t.ToString() ?? "",
            _ => throw new XQueryRuntimeException("XPTY0004",
                $"The search words of a full-text selection must be strings, not {v?.GetType().Name}"),
        }).ToList();

    /// <summary>A window size or range bound (xs:integer).</summary>
    public long Integer(XQueryExpression expression)
    {
        var values = _values[expression];
        if (values.Count != 1)
            throw new XQueryRuntimeException("XPTY0004", "A full-text window size or range bound must be a single integer");
        return values[0] switch
        {
            long l => l,
            int i => i,
            Xdm.XsTypedInteger t => t.Value,
            Xdm.XsUntypedAtomic u when long.TryParse(u.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            System.Numerics.BigInteger b when b >= long.MinValue && b <= long.MaxValue => (long)b,
            decimal d when d == decimal.Truncate(d) => (long)d,
            _ => throw new XQueryRuntimeException("XPTY0004", "A full-text window size or range bound must be an integer"),
        };
    }

    /// <summary>A weight (xs:double): FTDY0016 beyond 1000 (§3.1).</summary>
    public double Weight(XQueryExpression expression)
    {
        var values = _values[expression];
        if (values.Count != 1)
            throw new XQueryRuntimeException("XPTY0004", "A full-text weight must be a single number");
        // Converted as an argument of expected type xs:double: a string is XPTY0004, an untyped
        // value is cast (FORG0001 when it is not a number).
        var weight = values[0] switch
        {
            string or Xdm.XsAnyUri or Xdm.XsTypedString => throw new XQueryRuntimeException("XPTY0004",
                "A full-text weight must be a number"),
            Xdm.XsUntypedAtomic u => double.TryParse(u.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : throw new XQueryRuntimeException("FORG0001", $"The weight \"{u}\" is not a number"),
            IConvertible c when values[0] is not bool => c.ToDouble(CultureInfo.InvariantCulture),
            _ => throw new XQueryRuntimeException("XPTY0004", "A full-text weight must be a number"),
        };
        if (Math.Abs(weight) > 1000.0)
            throw new XQueryRuntimeException("FTDY0016", $"The weight {weight} is outside -1000 to 1000");
        return weight;
    }

    public (FtRangeKind Kind, long First, long Second) Range(FtRange range)
        => (range.Kind, Integer(range.First), range.Second is null ? 0 : Integer(range.Second));
}
