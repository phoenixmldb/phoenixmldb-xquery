using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// The bindings of one for clause iterate in the order written: the first is the outermost loop.
/// The optimizer reordered them by estimated cost, which changed the order of the results and,
/// for two bindings of the same name, which one was in scope.
/// </summary>
public sealed class ForBindingOrderTests
{
    private static async Task<string> Eval(string query) => (await new XQueryFacade().EvaluateAsync(query)).Trim();

    [Theory]
    [InlineData("string-join(for $a in (1 to 3), $b in ('x', 'y') return $a || $b, ' ')", "1x 1y 2x 2y 3x 3y")]
    [InlineData("string-join(for $a in (1, 2), $b in ('x', 'y', 'z') return $a || $b, ' ')", "1x 1y 1z 2x 2y 2z")]
    [InlineData("string-join(for $a in (1 to 4), $b in 'x', $c in (7, 8) return $a || $b || $c, ' ')", "1x7 1x8 2x7 2x8 3x7 3x8 4x7 4x8")]
    public async Task Results_come_in_binding_order(string query, string expected)
        => (await Eval(query)).Should().Be(expected);

    [Theory]
    [InlineData("string-join(for $i in (5, 6, 7), $i in 6 return string($i), ' ')", "6 6 6")]
    [InlineData("string-join(for $i in 1, $i in (5, 6, 7), $i in 6 return string($i), ' ')", "6 6 6")]
    public async Task The_later_of_two_same_named_bindings_is_in_scope(string query, string expected)
        => (await Eval(query)).Should().Be(expected);

    [Fact]
    public async Task Ordering_unordered_still_yields_every_tuple()
    {
        var r = await Eval("declare ordering unordered; string-join(sort(for $a in (1 to 3), $b in ('x', 'y') return $a || $b), ' ')");
        r.Should().Be("1x 1y 2x 2y 3x 3y");
    }
}
