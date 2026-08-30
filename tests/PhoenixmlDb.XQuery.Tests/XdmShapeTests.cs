using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// Pins the sequence-versus-array convention that five bugs got wrong. Each test below is a
/// bug that shipped, expressed as the smallest question that would have caught it.
/// </summary>
public class XdmShapeTests
{
    // object?[] is a SEQUENCE, List<object?> is an ARRAY.

    [Fact]
    public void An_array_is_one_item_not_its_members()
    {
        // The QT3 runner flattened here and lost 56 array-sort tests; xsl:array's spread
        // would have flattened a nested member the same way.
        var array = new List<object?> { 1, 2, 3 };
        XdmShape.SequenceItems(array).Should().HaveCount(1);
        XdmShape.SequenceItems(array)[0].Should().BeSameAs(array);
    }

    [Fact]
    public void A_sequence_yields_its_items()
        => XdmShape.SequenceItems(new object?[] { 1, 2, 3 }).Should().HaveCount(3);

    [Fact]
    public void A_bare_value_is_one_item()
        => XdmShape.SequenceItems(42).Should().ContainSingle().Which.Should().Be(42);

    [Fact]
    public void Null_is_the_empty_sequence()
        => XdmShape.SequenceItems(null).Should().BeEmpty();

    [Fact]
    public void ArrayMembers_looks_inside_an_array_and_only_an_array()
    {
        XdmShape.ArrayMembers(new List<object?> { 1, 2 }).Should().HaveCount(2);
        XdmShape.ArrayMembers(new object?[] { 1, 2 }).Should().BeNull();   // a sequence is not an array
        XdmShape.ArrayMembers(42).Should().BeNull();
    }

    /// <summary>
    /// The xsl:array bug: the finished array was handed on with .ToArray(), producing a
    /// SEQUENCE of its members. That printed correctly for a 5-member array and collapsed a
    /// one-member array to its member.
    /// </summary>
    [Fact]
    public void AsArray_never_unwraps_a_single_member()
    {
        var one = XdmShape.AsArray(new object?[] { 42 });
        XdmShape.IsArray(one).Should().BeTrue();
        XdmShape.SequenceItems(one).Should().HaveCount(1);
        XdmShape.ArrayMembers(one).Should().ContainSingle().Which.Should().Be(42);
    }

    [Fact]
    public void AsArray_of_nothing_is_an_empty_array_not_an_empty_sequence()
    {
        var empty = XdmShape.AsArray(Array.Empty<object?>());
        XdmShape.IsArray(empty).Should().BeTrue();
        XdmShape.ArrayMembers(empty).Should().BeEmpty();
    }

    /// <summary>A one-item sequence IS its item in XDM, so AsSequence unwraps — unlike AsArray.</summary>
    [Fact]
    public void AsSequence_unwraps_a_single_item()
    {
        XdmShape.AsSequence(new object?[] { 42 }).Should().Be(42);
        XdmShape.IsSequence(XdmShape.AsSequence(new object?[] { 1, 2 })).Should().BeTrue();
        XdmShape.SequenceItems(XdmShape.AsSequence(Array.Empty<object?>())).Should().BeEmpty();
    }

    /// <summary>
    /// The asymmetry is the whole point, and is what .ToArray() erased: one member packaged as
    /// an array stays a container; one item packaged as a sequence becomes the item.
    /// </summary>
    [Fact]
    public void AsArray_and_AsSequence_differ_on_exactly_one_element()
    {
        object?[] single = [7];
        XdmShape.AsSequence(single).Should().Be(7);
        XdmShape.AsArray(single).Should().BeOfType<List<object?>>();
    }

    [Fact]
    public void The_two_shapes_are_never_both_true()
    {
        foreach (object? v in new object?[] { new List<object?>(), Array.Empty<object?>(), 42, "x", null })
        {
            (XdmShape.IsArray(v) && XdmShape.IsSequence(v)).Should().BeFalse();
        }
    }

    // ---- Render: what a human is shown, and the CLR names that used to leak ----

    /// <summary>
    /// The bug this was written for. fn:trace interpolated its argument, so ToString() on the
    /// container answered with the CLR type: a sequence appeared as "System.Object[]" and an
    /// array as "System.Collections.Generic.List`1[System.Object]". Twelve of those turned up in
    /// a single conformance chunk log.
    /// </summary>
    [Fact]
    public void Render_NeverLeaksAClrTypeName()
    {
        object?[] sequence = [1, 2, 3];
        List<object?> array = [1, "two"];

        XdmShape.Render(sequence).Should().NotContain("System.");
        XdmShape.Render(array).Should().NotContain("System.");
        XdmShape.Render(new Dictionary<object, object?> { ["a"] = 1 }).Should().NotContain("System.");
    }

    /// <summary>A sequence has no brackets of its own; its items are juxtaposed.</summary>
    [Fact]
    public void Render_SequenceJuxtaposesItsItems()
    {
        object?[] sequence = [1, 2, 3];
        XdmShape.Render(sequence).Should().Be("1 2 3");
    }

    /// <summary>An array is a single item and reads as one, which is what tells it from a sequence.</summary>
    [Fact]
    public void Render_ArrayIsBracketed_SoItReadsDifferentlyFromASequence()
    {
        List<object?> array = [1, "two"];
        object?[] sequence = [1, "two"];

        XdmShape.Render(array).Should().Be("[1, two]");
        XdmShape.Render(array).Should().NotBe(XdmShape.Render(sequence));
    }

    [Fact]
    public void Render_EmptySequenceIsWrittenAsSuch() =>
        XdmShape.Render(null).Should().Be("()");

    [Fact]
    public void Render_BooleansUseXdmSpellingNotClr() =>
        XdmShape.Render(true).Should().Be("true");

    /// <summary>
    /// A diagnostic must not become the failure. Rendering is bounded so a deeply nested or
    /// cyclic value elides visibly instead of overflowing the stack, which matters more than
    /// usual here: the conformance suite currently hangs somewhere inside the fn:trace test set.
    /// </summary>
    [Fact]
    public void Render_BoundsDepthRatherThanRecursingForever()
    {
        object? nested = 1;
        for (var i = 0; i < 50; i++)
            nested = new List<object?> { nested };

        var rendered = XdmShape.Render(nested);
        rendered.Should().Contain("...");
        rendered.Length.Should().BeLessThan(200);
    }
}
