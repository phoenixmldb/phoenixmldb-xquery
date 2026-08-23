using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using PhoenixmlDb.XQuery;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Cli.Tests;

/// <summary>
/// Covers the CLI's own <c>ResultSerializer</c>, which is a SECOND implementation of adaptive
/// serialization alongside the engine's <c>XQueryResultSerializer</c>. The engine's copy is
/// correct and well covered by AdaptiveSerializationTests; this one shipped with the two
/// runtime representations inverted and no tests at all, so
///
///     partition((1,2,3,4,5,6,7), function($p, $n) { count($p) eq 2 })
///
/// printed 12/34/56/7 from the `xquery` tool where Saxon prints [1,2] and so on — reported by
/// Martin Honnen, 2026-08-22. Because the tool is what users run, a correct engine did not
/// save them.
///
/// The distinction these tests pin: an ARRAY is <c>List&lt;object?&gt;</c> and takes brackets;
/// a SEQUENCE is <c>object?[]</c> and takes parentheses when nested. The CLI had them the
/// wrong way round in both directions at once.
/// </summary>
public sealed class ResultSerializerAdaptiveTests
{
    private static string Adaptive(object? value)
    {
        var store = new XdmDocumentStore();
        using var writer = new StringWriter();
        var serializer = new ResultSerializer(store, writer, OutputMethod.Adaptive);
        serializer.Serialize(value);
        return writer.ToString();
    }

    // An array is List<object?>. This is the case that was missing entirely: it fell through
    // to the IEnumerable branch and serialized as bare members.
    [Fact]
    public void Array_renders_in_brackets()
        => Adaptive(new List<object?> { 1, 2 }).Should().Be("[1,2]");

    [Fact]
    public void Empty_array_renders_as_empty_brackets()
        => Adaptive(new List<object?>()).Should().Be("[]");

    [Fact]
    public void Nested_array_keeps_its_own_brackets()
        => Adaptive(new List<object?> { 1, new List<object?> { 2, 3 } }).Should().Be("[1,[2,3]]");

    // Inside an array a string is quoted, even though a top-level string is bare.
    [Fact]
    public void Strings_inside_an_array_are_quoted()
        => Adaptive(new List<object?> { "x", "y" }).Should().Be("[\"x\",\"y\"]");

    // A sequence is object?[] and takes PARENTHESES when it appears as an array member. The
    // old code gave it brackets, i.e. it rendered a sequence as though it were an array.
    [Fact]
    public void Sequence_member_renders_parenthesized()
        => Adaptive(new List<object?> { new object?[] { 1, 2 } }).Should().Be("[(1,2)]");

    // A length-1 sequence is indistinguishable from its single item, so it must NOT gain
    // parentheses.
    [Fact]
    public void Singleton_sequence_member_unwraps()
        => Adaptive(new List<object?> { new object?[] { 7 } }).Should().Be("[7]");

    [Fact]
    public void Map_inside_an_array_keeps_map_syntax()
    {
        var map = new Dictionary<object, object?> { ["k"] = new List<object?> { 1, 2 } };
        Adaptive(new List<object?> { map }).Should().Be("[map{\"k\":[1,2]}]");
    }

    // Regression guard for the half that was already right: a top-level SEQUENCE is still
    // separated by newlines and must not acquire brackets from this change.
    [Fact]
    public void TopLevel_sequence_is_not_bracketed()
        => Adaptive(new object?[] { 1, 2, 3 }).Should().NotStartWith("[");
}
