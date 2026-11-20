using System.Reflection;
using AwesomeAssertions;
using PhoenixmlDb.XQuery.Ast;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Ast;

/// <summary>
/// Every expression type that holds child expressions has its own visit in
/// <see cref="XQueryExpressionRewriter"/> (BUGS.md #130).
/// </summary>
/// <remarks>
/// The rewriter's default returns an expression unchanged, so a type without an override is a
/// type that NO rewrite pass (namespace resolution, constant folding, predicate simplification)
/// enters. A transform expression was such a type, and constructors inside its clauses lost their
/// namespaces. This test finds the types by reflection, so a new expression type with children
/// fails here instead of being skipped.
/// </remarks>
public class RewriterCoverageTests
{
    private static bool IsExpressionCarrier(Type t, int depth = 0)
    {
        if (typeof(XQueryExpression).IsAssignableFrom(t)) return true;
        if (t == typeof(string) || t.IsPrimitive || t.IsEnum) return false;
        var element = t.IsArray ? t.GetElementType()
            : t.IsGenericType && typeof(System.Collections.IEnumerable).IsAssignableFrom(t) ? t.GetGenericArguments()[^1]
            : null;
        if (element != null) return IsExpressionCarrier(element, depth);
        if (depth >= 2 || t.Namespace != typeof(XQueryExpression).Namespace) return false;
        return t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Any(p => IsExpressionCarrier(p.PropertyType, depth + 1));
    }

    internal static IEnumerable<string> TypesWithChildrenAndNoRewriterVisit()
    {
        var rewriter = typeof(XQueryExpressionRewriter);
        var types = typeof(XQueryExpression).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(XQueryExpression).IsAssignableFrom(t));
        foreach (var t in types)
        {
            var children = t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.DeclaringType != typeof(XQueryExpression) && IsExpressionCarrier(p.PropertyType))
                .Select(p => p.Name).ToList();
            if (children.Count == 0) continue;
            var visit = rewriter.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name.StartsWith("Visit", StringComparison.Ordinal)
                    && m.GetParameters() is [var only] && only.ParameterType == t);
            if (visit == null || visit.DeclaringType != rewriter)
                yield return $"{t.Name} ({string.Join(", ", children)})";
        }
    }

    [Fact]
    public void Every_expression_type_with_children_is_visited_by_the_rewriter()
    {
        TypesWithChildrenAndNoRewriterVisit().Should().BeEmpty();
    }
}
