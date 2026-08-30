using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// A FLWOR for clause used to build each tuple by allocating a fresh dictionary and copying every
/// binding into it, once per tuple per nesting level. Running the whole QT3 corpus sat at 112% CPU
/// with RSS climbing about 6MB/s past a gigabyte, and the stack was
/// <c>Dictionary.AddRange &lt;- ForClauseOperator.ExecuteBindingsAsync</c>.
/// </summary>
/// <remarks>
/// The tuples are now merged in place, which is safe because every dictionary yielded from that
/// recursion is freshly allocated by the base case and consumed exactly once. The subtlety worth
/// pinning is precedence: the old code let the inner tuple overwrite the outer one, so an inner
/// binding shadowed an outer binding of the same name. In-place merge uses TryAdd to preserve
/// that, and getting it backwards would silently unshadow variables rather than fail loudly.
/// </remarks>
public class FlworBindingMergeTests
{
    private readonly XQueryFacade _facade = new();
    private async Task<string> Eval(string xq) => await _facade.EvaluateAsync(xq);

    /// <summary>An inner binding of the same name shadows the outer one.</summary>
    [Fact]
    public async Task InnerBindingShadowsOuterOfTheSameName()
        => (await Eval("string-join(for $x in (1, 2) for $x in (10, 20) return $x, ',')"))
            .Should().Be("10,20,10,20");

    /// <summary>Distinct names from both levels survive the merge.</summary>
    [Fact]
    public async Task BothLevelsContributeTheirOwnBindings()
        => (await Eval("string-join(for $a in (1, 2) for $b in ('x', 'y') return concat($a, $b), ',')"))
            .Should().Be("1x,1y,2x,2y");

    /// <summary>Three levels, to catch a merge that only works at depth two.</summary>
    [Fact]
    public async Task ThreeLevelsOfNestingMergeCorrectly()
        => (await Eval("string-join(for $a in (1,2) for $b in (3,4) for $c in (5,6) "
                     + "return string($a*100+$b*10+$c), ',')"))
            .Should().Be("135,136,145,146,235,236,245,246");

    /// <summary>The positional variable travels with its binding.</summary>
    [Fact]
    public async Task PositionalVariablesSurviveTheMerge()
        => (await Eval("string-join(for $x at $i in ('a','b') for $y at $j in ('p','q') "
                     + "return concat($x, $i, $y, $j), ',')"))
            .Should().Be("a1p1,a1q2,b2p1,b2q2");

    /// <summary>allowing empty takes its own merge path.</summary>
    [Fact]
    public async Task AllowingEmptyStillBindsTheOuterVariable()
        => (await Eval("string-join(for $a in (1, 2) for $b allowing empty in () return string($a), ',')"))
            .Should().Be("1,2");
}
