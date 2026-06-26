using FluentAssertions;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Functions;
using Xunit;
using XQueryExecutionContext = PhoenixmlDb.XQuery.Ast.ExecutionContext;

namespace PhoenixmlDb.XQuery.Tests.Functions;

/// <summary>
/// Regression tests for the app-spec-examples (QT3) fixes: XDM arrays are single items
/// for the fn:head/tail/reverse/index-of/sum sequence functions, deep-equal value-promotes
/// derived integer types, and UCA collations (incl. alternate=blanked) drive the
/// substring-search string functions.
/// </summary>
public class SpecExampleFixesTests
{
    private const string UcaBlankedPrimary =
        "http://www.w3.org/2013/collation/UCA?lang=en;alternate=blanked;strength=primary";

    private static XQueryExecutionContext Ctx() => new TestContext();
    private sealed class TestContext : XQueryExecutionContext
    {
        public object? ContextItem => null;
    }

    // ---- arrays are single items for fn: sequence functions ----

    [Fact]
    public async Task Head_Array_ReturnsArrayItself()
    {
        var arr = new List<object?> { 1L, 2L, 3L };
        var result = await new HeadFunction().InvokeAsync([arr], Ctx());
        result.Should().BeSameAs(arr);
    }

    [Fact]
    public async Task Tail_Array_ReturnsEmptySequence()
    {
        var arr = new List<object?> { 1L, 2L, 3L };
        var result = await new TailFunction().InvokeAsync([arr], Ctx());
        ((System.Collections.IEnumerable)result!).Cast<object?>().Should().BeEmpty();
    }

    [Fact]
    public async Task Reverse_Array_ReturnsSingletonOfArray()
    {
        var arr = new List<object?> { 1L, 2L, 3L };
        var result = await new ReverseFunction().InvokeAsync([arr], Ctx()) as object?[];
        result.Should().NotBeNull();
        result.Should().HaveCount(1);
        result![0].Should().BeSameAs(arr);
    }

    [Fact]
    public async Task IndexOf_FlattensNestedArrays()
    {
        // index-of([1,[5,6],[6,7]], 6) → (3, 4)
        var arr = new List<object?>
        {
            1L,
            new List<object?> { 5L, 6L },
            new List<object?> { 6L, 7L },
        };
        var result = await new IndexOfFunction().InvokeAsync([arr, 6L], Ctx()) as object?[];
        result.Should().NotBeNull();
        result!.Select(x => (long)x!).Should().Equal(3L, 4L);
    }

    [Fact]
    public async Task Sum_FlattensNestedArrays()
    {
        // sum([[1,2],[3,4]]) → 10
        var arr = new List<object?>
        {
            new List<object?> { 1L, 2L },
            new List<object?> { 3L, 4L },
        };
        var result = await new SumFunction().InvokeAsync([arr], Ctx());
        System.Convert.ToInt64(result).Should().Be(10);
    }

    // ---- deep-equal value-promotes derived integer types ----

    [Fact]
    public async Task DeepEqual_TypedIntVsInteger_IsTrue()
    {
        // deep-equal((xs:int(23), xs:int(29)), (23, 29)) → true
        var typed = new object?[] { new XsTypedInteger(23, "int"), new XsTypedInteger(29, "int") };
        var plain = new object?[] { 23L, 29L };
        var result = await new DeepEqualFunction().InvokeAsync([typed, plain], Ctx());
        result.Should().Be(true);
    }

    // ---- UCA collation-aware substring search (alternate=blanked) ----

    [Theory]
    [InlineData("abcdefghi", "-d-e-f-", true)]   // punctuation ignored under blanked
    [InlineData("abcdefghi", "xyz", false)]
    public void UcaContains(string s, string search, bool expected) =>
        CollationHelper.UcaContains(s, search, UcaBlankedPrimary).Should().Be(expected);

    [Theory]
    [InlineData("abcdefghi", "-a-b-c-", true)]
    [InlineData("-abcdefghi", "-abc", true)]      // leading symbol on source
    [InlineData("", "--***-*---", true)]          // symbol-only search collapses to empty
    [InlineData("abcdefghi", "-d-e-", false)]
    public void UcaStartsWith(string s, string prefix, bool expected) =>
        CollationHelper.UcaStartsWith(s, prefix, UcaBlankedPrimary).Should().Be(expected);

    [Theory]
    [InlineData("abcdefghi", "ghi-", true)]
    [InlineData("", "--***-*---", true)]
    [InlineData("abcdefghi", "-d-e-", false)]
    public void UcaEndsWith(string s, string suffix, bool expected) =>
        CollationHelper.UcaEndsWith(s, suffix, UcaBlankedPrimary).Should().Be(expected);

    [Fact]
    public void UcaSubstringBefore_IgnoresPunctuation() =>
        CollationHelper.UcaSubstringBefore("abcdefghi", "--d-e-", UcaBlankedPrimary).Should().Be("abc");

    [Fact]
    public void UcaSubstringAfter_IgnoresPunctuation() =>
        CollationHelper.UcaSubstringAfter("abcdefghi", "--d-e-", UcaBlankedPrimary).Should().Be("fghi");

    // ---- UCA default-collation compare (German ss/ß equivalence) ----

    [Fact]
    public void CompareUca_GermanPrimary_SsEqualsSharpS() =>
        CollationHelper.CompareUca("Strasse", "Straße",
            "http://www.w3.org/2013/collation/UCA?lang=de;strength=primary").Should().Be(0);

    [Fact]
    public void CompareUca_GermanTertiary_StrassenGreaterThanStrasse() =>
        System.Math.Sign(CollationHelper.CompareUca("Strassen", "Straße",
            "http://www.w3.org/2013/collation/UCA?lang=de")).Should().Be(1);
}
