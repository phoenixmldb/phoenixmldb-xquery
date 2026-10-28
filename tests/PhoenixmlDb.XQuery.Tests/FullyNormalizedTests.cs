using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// normalize-unicode with FULLY-NORMALIZED gives NFC, with a space before a leading composing
/// character (Character Model: Normalization). The form was refused with FOCH0003
/// (QT3 cbcl-fn-normalize-unicode-001, -006).
/// </summary>
public class FullyNormalizedTests
{
    private static async Task<string> Eval(string query)
    {
        var engine = new QueryEngine();
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors));
        var items = new List<object?>();
        await foreach (var i in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
            items.Add(i);
        return string.Join(",", items.Select(i => i?.ToString() ?? ""));
    }

    [Theory]
    [InlineData("normalize-unicode('blah', 'FULLY-NORMALIZED')", "blah")]
    // U+09BE has combining class 0 but is the second part of U+09CB's decomposition.
    [InlineData("string-to-codepoints(normalize-unicode(codepoints-to-string(2494), 'FULLY-NORMALIZED'))", "32,2494")]
    // U+0301 has a non-zero combining class.
    [InlineData("string-to-codepoints(normalize-unicode(codepoints-to-string((769, 97)), 'fully-normalized'))", "32,769,97")]
    // NFC first: e + U+0301 composes to U+00E9, which is not composing.
    [InlineData("string-to-codepoints(normalize-unicode(codepoints-to-string((101, 769)), 'FULLY-NORMALIZED'))", "233")]
    [InlineData("normalize-unicode('', 'FULLY-NORMALIZED')", "")]
    public async Task Fully_normalized_is_NFC_without_a_leading_composing_character(string query, string expected)
        => (await Eval(query)).Should().Be(expected);
}
