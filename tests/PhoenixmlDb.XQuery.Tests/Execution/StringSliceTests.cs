using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.Functions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// A <see cref="StringSlice"/> cut again and again gives the strings fn:substring copies out of
/// the same string, and counts codepoints as fn:string-length does (xquery#287). Surrogate pairs,
/// a lone surrogate, starts past the end and zero lengths included.
/// </summary>
public class StringSliceTests
{
    public static TheoryData<string> Texts =>
    [
        "",
        "abc(def)[g]",
        "x\U0001F600(y\U0001D11Ez",
        "\U0001F600\U0001F600\U0001F600",
        "a\uD800b",
    ];

    [Theory]
    [MemberData(nameof(Texts))]
    public void Every_cut_of_a_slice_equals_the_copy_of_the_string(string text)
    {
        var codepoints = StringLengthFunction.CountCodepoints(text);
        for (var start = 1; start <= codepoints + 2; start++)
        {
            for (var length = 0; length <= codepoints + 1; length++)
            {
                var slice = StringSlice.Of(text).Substring(start, length);
                slice.ToString().Should().Be(SubstringHelper.SubstringByCodepoints(text, start, length),
                    $"substring at {start} for {length}");
                slice.CodepointLength.Should().Be(StringLengthFunction.CountCodepoints(slice.ToString()));
            }
        }
    }

    [Theory]
    [MemberData(nameof(Texts))]
    public void A_walk_one_codepoint_at_a_time_equals_the_copies(string text)
    {
        var slice = StringSlice.Of(text);
        var copy = text;
        while (copy.Length > 0)
        {
            slice.ToString().Should().Be(copy);
            slice.CodepointLength.Should().Be(StringLengthFunction.CountCodepoints(copy));
            slice.Substring(1, 1).ToString().Should().Be(SubstringHelper.SubstringByCodepoints(copy, 1, 1));
            slice = slice.Substring(2, int.MaxValue);
            copy = SubstringHelper.SubstringByCodepoints(copy, 2, int.MaxValue);
        }
        slice.ToString().Should().BeEmpty();
        slice.CodepointLength.Should().Be(0);
    }
}
