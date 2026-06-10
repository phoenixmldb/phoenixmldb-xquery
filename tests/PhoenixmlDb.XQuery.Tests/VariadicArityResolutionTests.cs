using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// Locks variadic function arity resolution. Functions with optional <em>trailing</em>
/// arguments (array:slice, array:build, fn:slice, fn:highest, fn:lowest, map:build,
/// ft:thesaurus-lookup) declare IsVariadic + a finite MaxArity equal to their full
/// parameter count. The resolver previously used the full parameter count as the
/// lower bound too, so these functions only resolved at their maximum arity — a call
/// like array:slice($a, 2, 4) (arity 3) failed with "Unknown function: slice#3".
///
/// MinArity now expresses the true lower bound, so each resolves across its whole
/// arity range. The fix must NOT loosen genuinely-required arities: fn:concat still
/// requires two arguments even though its parameters are declared item()*.
/// </summary>
public class VariadicArityResolutionTests
{
    private readonly XQueryFacade _facade = new();

    private async Task<string> Eval(string xq) => await _facade.EvaluateAsync(xq);

    // --- array:slice (arity 1-4) ---

    [Fact]
    public async Task ArraySlice_arity1_copies_whole_array()
        => (await Eval("string-join(array:flatten(array:slice([1,2,3,4,5]))!string(.), ',')")).Should().Be("1,2,3,4,5");

    [Fact]
    public async Task ArraySlice_arity2_from_start()
        => (await Eval("string-join(array:flatten(array:slice([1,2,3,4,5], 3))!string(.), ',')")).Should().Be("3,4,5");

    [Fact]
    public async Task ArraySlice_arity3_start_end()
        => (await Eval("string-join(array:flatten(array:slice([1,2,3,4,5], 2, 4))!string(.), ',')")).Should().Be("2,3,4");

    [Fact]
    public async Task ArraySlice_arity3_negative_indices()
        => (await Eval("string-join(array:flatten(array:slice([1,2,3,4,5], -2, -1))!string(.), ',')")).Should().Be("4,5");

    [Fact]
    public async Task ArraySlice_arity4_with_step()
        => (await Eval("string-join(array:flatten(array:slice([1,2,3,4,5,6,7,8], 1, 8, 2))!string(.), ',')")).Should().Be("1,3,5,7");

    // --- fn:slice (arity 1-4) ---

    [Fact]
    public async Task FnSlice_arity1_returns_whole_sequence()
        => (await Eval("string-join(fn:slice((1,2,3,4,5))!string(.), ',')")).Should().Be("1,2,3,4,5");

    [Fact]
    public async Task FnSlice_arity3_start_end()
        => (await Eval("string-join(fn:slice((10,20,30,40,50), 2, 4)!string(.), ',')")).Should().Be("20,30,40");

    // --- array:build / map:build (identity defaults) ---

    [Fact]
    public async Task ArrayBuild_arity1_identity()
        => (await Eval("string-join(array:flatten(array:build(1 to 3))!string(.), ',')")).Should().Be("1,2,3");

    [Fact]
    public async Task MapBuild_arity1_keys_each_item_by_itself()
        => (await Eval("string-join(map:keys(map:build(1 to 3))!string(.), ',')")).Should().Be("1,2,3");

    [Fact]
    public async Task MapBuild_arity2_custom_key_function()
        => (await Eval("string-join(map:keys(map:build(('aa','b','ccc'), function($x){string-length($x)}))!string(.), ',')"))
            .Should().Be("2,1,3");

    // --- fn:highest / fn:lowest (arity 1) ---

    [Fact]
    public async Task FnHighest_arity1()
        => (await Eval("string-join(fn:highest((3,1,4,1,5))!string(.), ',')")).Should().Be("5");

    [Fact]
    public async Task FnLowest_arity1_returns_all_tied_lowest()
        // XPath 4.0: fn:lowest returns every item equal to the minimum, so the two 1s.
        => (await Eval("string-join(fn:lowest((3,1,4,1,5))!string(.), ',')")).Should().Be("1,1");

    // --- Regression guards: genuinely-required arities stay required ---

    [Fact]
    public async Task FnConcat_arity2_still_resolves()
        => (await Eval("concat('a','b')")).Should().Be("ab");

    [Fact]
    public async Task FnConcat_arity0_still_rejected()
    {
        var act = async () => await Eval("concat()");
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task FnTrace_arity1_still_resolves()
        => (await Eval("fn:trace(42)")).Should().Contain("42");
}
