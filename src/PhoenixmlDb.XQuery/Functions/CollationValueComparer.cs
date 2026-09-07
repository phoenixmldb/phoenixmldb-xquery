using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// Value comparer that uses a collation for string comparisons.
/// </summary>
internal sealed class CollationValueComparer : IEqualityComparer<object?>
{
    private readonly StringComparison _comparison;

    public CollationValueComparer(StringComparison comparison) => _comparison = comparison;

    public new bool Equals(object? x, object? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null) return x is null && y is null;

        if (x is Xdm.XsTypedString tsx) x = tsx.Value;
        if (y is Xdm.XsTypedString tsy) y = tsy.Value;

        if (x is Xdm.XsTypedInteger tix) x = tix.Value;
        if (y is Xdm.XsTypedInteger tiy) y = tiy.Value;

        if (x is string sx && y is string sy)
            return string.Equals(sx, sy, _comparison);

        if (XQueryValueComparer.IsNumericValue(x) && XQueryValueComparer.IsNumericValue(y))
            return XQueryValueComparer.NumericEquals(x, y);

        return object.Equals(x, y);
    }

    public int GetHashCode(object? obj)
    {
        if (obj is null) return 0;
        if (obj is Xdm.XsTypedString ts2) obj = ts2.Value;
        if (obj is Xdm.XsTypedInteger ti2) obj = ti2.Value;
        if (obj is string s && _comparison is StringComparison.OrdinalIgnoreCase or StringComparison.InvariantCultureIgnoreCase)
            return StringComparer.OrdinalIgnoreCase.GetHashCode(s);
        if (XQueryValueComparer.IsNumericValue(obj))
        {
            if (obj is double d)
            {
                if (double.IsNaN(d)) return 0;
                return d.GetHashCode();
            }
            var fv = Convert.ToSingle(obj, System.Globalization.CultureInfo.InvariantCulture);
            if (float.IsNaN(fv)) return 0;
            return fv.GetHashCode();
        }
        return obj.GetHashCode();
    }
}
