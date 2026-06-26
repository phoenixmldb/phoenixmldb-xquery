using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// Regression tests for adaptive-method array serialization. Per W3C Serialization 4.0
/// §6 (Adaptive method), an array renders as <c>[m1,m2,…]</c> with each member serialized
/// per adaptive rules, and a member that is itself a sequence renders parenthesized
/// (<c>(1,2,3)</c>). Previously top-level arrays in adaptive mode wrongly hit the JSON
/// array path, producing the wrong format and hard-erroring on sequence members and
/// INF/NaN. The boolean <c>true()</c> form and typed INF lexical are owned by later tasks.
/// </summary>
public class AdaptiveSerializationTests
{
    private readonly XQueryFacade _facade = new();

    private const string AdaptivePrefix = "declare option output:method 'adaptive';\n";

    [Fact]
    public async Task Array_of_sequence_members_renders_parenthesized()
    {
        var result = await _facade.EvaluateAsync(
            AdaptivePrefix + "[(1,2,3),(4,5,6)]", "<x/>");

        result.Should().Be("[(1,2,3),(4,5,6)]");
    }

    [Fact]
    public async Task Array_of_singletons_renders_bare_members()
    {
        var result = await _facade.EvaluateAsync(
            AdaptivePrefix + "[1,2,3]", "<x/>");

        result.Should().Be("[1,2,3]");
    }

    [Fact]
    public async Task Empty_array_renders_empty_brackets()
    {
        var result = await _facade.EvaluateAsync(
            AdaptivePrefix + "[]", "<x/>");

        result.Should().Be("[]");
    }

    [Fact]
    public async Task Array_of_empty_sequence_members_renders_empty_parens()
    {
        var result = await _facade.EvaluateAsync(
            AdaptivePrefix + "[(),(),()]", "<x/>");

        result.Should().Be("[(),(),()]");
    }

    [Fact]
    public async Task Array_with_INF_member_does_not_throw_and_keeps_structure()
    {
        var act = async () => await _facade.EvaluateAsync(
            AdaptivePrefix + "[xs:double('INF')]", "<x/>");

        var result = await act.Should().NotThrowAsync();
        result.Subject.Should().Contain("[", because: "the array brackets must be present");
        result.Subject.Should().Contain("]", because: "the array brackets must be present");
    }

    [Fact]
    public void TopLevel_string_with_AdaptiveQuoteStrings_renders_quoted()
    {
        // W3C adaptive serialization quotes atomic strings even at the top level.
        // This opt-in behaviour is what the conformance harness enables.
        var store = new XdmDocumentStore();
        var options = new SerializationOptions
        {
            Method = OutputMethod.Adaptive,
            AdaptiveQuoteStrings = true,
        };

        var result = XQueryResultSerializer.Serialize("simple string", store, options);

        result.Should().Be("\"simple string\"");
    }

    [Fact]
    public async Task Facade_default_keeps_top_level_string_bare()
    {
        // GUARD: the string-in/string-out facade defaults to adaptive but must NOT
        // quote top-level strings (flag off) — users rely on bare output.
        var result = await _facade.EvaluateAsync("'simple string'", "<x/>");

        result.Should().Be("simple string");
    }

    [Fact]
    public async Task Facade_text_method_keeps_string_bare()
    {
        var result = await _facade.EvaluateAsync(
            "declare option output:method 'text'; 'simple string'", "<x/>");

        result.Should().Be("simple string");
    }

    [Fact]
    public async Task Array_of_booleans_renders_function_call_form()
    {
        // W3C Serialization 4.0 §6: a boolean INSIDE a structured item (array member)
        // renders as true()/false(), not bare.
        var result = await _facade.EvaluateAsync(
            AdaptivePrefix + "[true(), false()]", "<x/>");

        result.Should().Be("[true(),false()]");
    }

    [Fact]
    public async Task Map_value_boolean_renders_function_call_form()
    {
        // A map VALUE is a structured-item context → true().
        var result = await _facade.EvaluateAsync(
            AdaptivePrefix + "map{'b': true()}", "<x/>");

        result.Should().Be("map{\"b\":true()}");
    }

    [Fact]
    public async Task TopLevel_boolean_true_stays_bare()
    {
        // GUARD: top-level booleans MUST stay bare. This contrast is the whole point.
        var result = await _facade.EvaluateAsync(
            AdaptivePrefix + "true()", "<x/>");

        result.Should().Be("true");
    }

    [Fact]
    public async Task TopLevel_boolean_false_stays_bare()
    {
        // GUARD: top-level booleans MUST stay bare.
        var result = await _facade.EvaluateAsync(
            AdaptivePrefix + "false()", "<x/>");

        result.Should().Be("false");
    }

    // ---- Function items (Task 4): adaptive method renders prefix:local#arity for a
    // named function reference and (anonymous-function)#arity for an inline function. ----

    [Fact]
    public async Task Named_function_reference_renders_prefix_local_arity()
    {
        var result = await _facade.EvaluateAsync(
            AdaptivePrefix + "fn:exists#1", "<x/>");

        result.Should().Be("fn:exists#1");
    }

    [Fact]
    public async Task Inline_function_renders_anonymous_form()
    {
        var result = await _facade.EvaluateAsync(
            AdaptivePrefix + "function($x){$x}", "<x/>");

        result.Should().Be("(anonymous-function)#1");
    }

    [Fact]
    public async Task Array_of_function_items_renders_each_member()
    {
        var result = await _facade.EvaluateAsync(
            AdaptivePrefix + "[fn:exists#1, function($x){$x}]", "<x/>");

        result.Should().Be("[fn:exists#1,(anonymous-function)#1]");
    }
}
