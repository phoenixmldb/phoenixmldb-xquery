using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// Locks XPath 4.0 ordered-map semantics: XDM maps iterate in entry/insertion
/// order. XPath 3.1 left this unspecified ("don't write code that relies on it");
/// XPath 4.0 makes insertion order a contract. PhoenixmlDb commits to the 4.0
/// behavior, and this suite is the regression guard that keeps it a guarantee
/// rather than an incidental property of the backing data structure.
///
/// Each test asserts exact key order via map:keys + string-join so a reordering
/// regression fails loudly.
/// </summary>
public class OrderedMapSemanticsTests
{
    private readonly XQueryFacade _facade = new();

    private async Task<string> KeyOrder(string mapExpr)
        => await _facade.EvaluateAsync($"string-join(map:keys({mapExpr}), ',')");

    // --- Construction order ---

    [Fact]
    public async Task Literal_map_preserves_entry_order_not_sorted()
    {
        // Keys deliberately non-alphabetical so a sorted/hashed impl would reorder.
        var order = await KeyOrder("map { 'zebra': 1, 'apple': 2, 'mango': 3 }");
        order.Should().Be("zebra,apple,mango");
    }

    [Fact]
    public async Task Literal_map_without_keyword_preserves_order()
    {
        var order = await KeyOrder("{ 'gamma': 1, 'alpha': 2, 'beta': 3 }");
        order.Should().Be("gamma,alpha,beta");
    }

    [Fact]
    public async Task Numeric_keys_preserve_insertion_order_not_numeric_order()
    {
        var order = await KeyOrder("map { 30: 'a', 10: 'b', 20: 'c' }");
        order.Should().Be("30,10,20");
    }

    // --- map:put ---

    [Fact]
    public async Task Repeated_put_of_new_keys_appends_in_call_order()
    {
        var order = await KeyOrder("map:put(map:put(map:put(map{}, 'c', 3), 'a', 1), 'b', 2)");
        order.Should().Be("c,a,b");
    }

    [Fact]
    public async Task Put_existing_key_preserves_original_position()
    {
        // XPath 4.0: replacing a value for an existing key does NOT move the key.
        var order = await KeyOrder("map:put(map { 'a':1, 'b':2, 'c':3 }, 'a', 99)");
        order.Should().Be("a,b,c");
    }

    [Fact]
    public async Task Put_existing_middle_key_preserves_position()
    {
        var order = await KeyOrder("map:put(map { 'a':1, 'b':2, 'c':3 }, 'b', 99)");
        order.Should().Be("a,b,c");
    }

    // --- map:remove ---

    [Fact]
    public async Task Remove_preserves_order_of_survivors()
    {
        var order = await KeyOrder("map:remove(map { 'a':1, 'b':2, 'c':3, 'd':4 }, ('a','c'))");
        order.Should().Be("b,d");
    }

    [Fact]
    public async Task Remove_then_put_new_key_appends_at_end()
    {
        var order = await KeyOrder(
            "map:put(map:remove(map { 'a':1,'b':2,'c':3,'d':4 }, 'b'), 'e', 5)");
        order.Should().Be("a,c,d,e");
    }

    [Fact]
    public async Task Multi_remove_then_multi_put_keeps_deterministic_order()
    {
        // Adversarial: two non-adjacent removals build a free-list in a hash-backed
        // impl; two subsequent inserts must still land at the end in call order.
        var order = await KeyOrder(
            "map:put(map:put(map:remove(map{'a':1,'b':2,'c':3,'d':4,'e':5}, ('b','d')), 'x', 9), 'y', 8)");
        order.Should().Be("a,c,e,x,y");
    }

    [Fact]
    public async Task Remove_then_readd_same_key_moves_it_to_end()
    {
        // XPath 4.0: a removed-then-reinserted key is a NEW insertion → goes to end.
        var order = await KeyOrder(
            "map:put(map:remove(map { 'a':1, 'b':2, 'c':3 }, 'a'), 'a', 1)");
        order.Should().Be("b,c,a");
    }

    // --- map:merge ---

    [Fact]
    public async Task Merge_preserves_order_across_maps()
    {
        var order = await KeyOrder("map:merge((map{'a':1}, map{'b':2}, map{'c':3}))");
        order.Should().Be("a,b,c");
    }

    [Fact]
    public async Task Merge_with_duplicates_use_first_keeps_first_position()
    {
        // Default duplicate handling: the key keeps the position of its first occurrence.
        var order = await KeyOrder(
            "map:merge((map{'a':1,'b':2}, map{'b':99,'c':3}), map{'duplicates':'use-first'})");
        order.Should().Be("a,b,c");
    }

    [Fact]
    public async Task Merge_single_argument_form_defaults_to_use_first_value()
    {
        // The one-argument map:merge has no options map, so it must apply the default
        // duplicate policy "use-first": for a key present in more than one input map,
        // the value from the EARLIEST map wins. Regression guard for QT3 app-Walmsley
        // d1e66015/26/70/81, which previously failed because the one-arg form used a
        // last-wins assignment.
        var value = await _facade.EvaluateAsync(
            "map:merge((map{1:'first', 2:'second'}, map{1:'ONE', 'abc':'def'}))(1)");
        value.Should().Be("first");
    }

    [Fact]
    public async Task Merge_single_argument_form_use_first_across_three_maps()
    {
        var first = await _facade.EvaluateAsync(
            "map:merge((map{1:'a'}, map{1:'b'}, map{1:'c'}))(1)");
        first.Should().Be("a");
    }

    // --- Build-via-fold (the grouping use case Martin described) ---

    [Fact]
    public async Task Fold_left_build_preserves_insertion_order()
    {
        var order = await KeyOrder(
            "fold-left(('delta','alpha','charlie','bravo'), map{}, " +
            "function($acc,$k){ map:put($acc,$k,string-length($k)) })");
        order.Should().Be("delta,alpha,charlie,bravo");
    }

    [Fact]
    public async Task Grouping_into_map_yields_groups_in_first_seen_order()
    {
        // Mirrors converting XML-grouping code to maps: groups should emerge in the
        // order their first member appears, exactly as the XML-target version did.
        const string xml = """
            <items>
              <i cat="fruit">apple</i>
              <i cat="veg">carrot</i>
              <i cat="fruit">banana</i>
              <i cat="grain">rice</i>
              <i cat="veg">pea</i>
            </items>
            """;
        var order = await _facade.EvaluateAsync(
            "string-join(map:keys(map:merge(" +
            "for $i in /items/i return map:entry(string($i/@cat), $i), " +
            "map{'duplicates':'use-first'})), ',')",
            xml);
        order.Should().Be("fruit,veg,grain");
    }

    // --- Nested + serialization ---

    [Fact]
    public async Task Nested_map_preserves_inner_order()
    {
        var order = await KeyOrder("map:get(map { 'outer': map { 'z':1, 'a':2, 'm':3 } }, 'outer')");
        order.Should().Be("z,a,m");
    }

    [Fact]
    public async Task Json_serialization_preserves_key_order()
    {
        var result = await _facade.EvaluateAsync(
            """
            declare option output:method "json";
            map { 'zebra': 1, 'apple': 2, 'mango': 3 }
            """);
        // zebra must appear before apple before mango in the serialized text.
        var zi = result.IndexOf("zebra", System.StringComparison.Ordinal);
        var ai = result.IndexOf("apple", System.StringComparison.Ordinal);
        var mi = result.IndexOf("mango", System.StringComparison.Ordinal);
        zi.Should().BeGreaterThanOrEqualTo(0);
        ai.Should().BeGreaterThan(zi);
        mi.Should().BeGreaterThan(ai);
    }

    [Fact]
    public async Task Large_map_preserves_order_beyond_hash_bucket_count()
    {
        // 50 keys k01..k50 inserted in order; a hash-backed map without explicit
        // ordering would scatter these. Assert they come back in insertion order.
        var expr =
            "string-join(map:keys(map:merge(" +
            "for $n in 1 to 50 return map:entry(concat('k', format-number($n, '00')), $n)" +
            ")), ',')";
        var result = await _facade.EvaluateAsync(expr);
        var expected = string.Join(",", Enumerable.Range(1, 50).Select(n => $"k{n:00}"));
        result.Should().Be(expected);
    }
}
