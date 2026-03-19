using FluentAssertions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Functions;
using Xunit;
using XQueryExecutionContext = PhoenixmlDb.XQuery.Ast.ExecutionContext;

namespace PhoenixmlDb.XQuery.Tests.Functions;

/// <summary>
/// Tests for string functions.
/// </summary>
public class StringFunctionTests
{
    #region string-length Tests

    [Fact]
    public async Task StringLength_EmptyString_ReturnsZero()
    {
        var func = new StringLengthFunction();
        var result = await func.InvokeAsync([""], CreateContext());

        result.Should().Be(0);
    }

    [Fact]
    public async Task StringLength_NonEmptyString_ReturnsCorrectLength()
    {
        var func = new StringLengthFunction();
        var result = await func.InvokeAsync(["hello"], CreateContext());

        result.Should().Be(5);
    }

    [Fact]
    public async Task StringLength_NullArgument_ReturnsZero()
    {
        var func = new StringLengthFunction();
        var result = await func.InvokeAsync([null], CreateContext());

        result.Should().Be(0);
    }

    [Fact]
    public async Task StringLength_UnicodeString_ReturnsCorrectLength()
    {
        var func = new StringLengthFunction();
        var result = await func.InvokeAsync(["\u4e16\u754c"], CreateContext()); // Two Chinese characters

        result.Should().Be(2);
    }

    [Fact]
    public void StringLength_Name_IsFnStringLength()
    {
        var func = new StringLengthFunction();

        func.Name.LocalName.Should().Be("string-length");
        func.Name.Namespace.Should().Be(FunctionNamespaces.Fn);
    }

    [Fact]
    public void StringLength_ReturnType_IsInteger()
    {
        var func = new StringLengthFunction();

        func.ReturnType.Should().Be(XdmSequenceType.Integer);
    }

    [Fact]
    public void StringLength_Arity_IsOne()
    {
        var func = new StringLengthFunction();

        func.Arity.Should().Be(1);
    }

    #endregion

    #region substring Tests

    [Fact]
    public async Task Substring_FromStart_ReturnsSubstring()
    {
        var func = new SubstringFunction();
        var result = await func.InvokeAsync(["hello", 1.0], CreateContext());

        result.Should().Be("hello");
    }

    [Fact]
    public async Task Substring_FromMiddle_ReturnsSubstring()
    {
        var func = new SubstringFunction();
        var result = await func.InvokeAsync(["hello", 3.0], CreateContext());

        result.Should().Be("llo");
    }

    [Fact]
    public async Task Substring_BeyondEnd_ReturnsEmpty()
    {
        var func = new SubstringFunction();
        var result = await func.InvokeAsync(["hello", 10.0], CreateContext());

        result.Should().Be("");
    }

    [Fact]
    public async Task Substring_NegativeStart_ReturnsFromBeginning()
    {
        var func = new SubstringFunction();
        var result = await func.InvokeAsync(["hello", -1.0], CreateContext());

        result.Should().Be("hello");
    }

    [Fact]
    public async Task Substring_WithLength_ReturnsCorrectPortion()
    {
        var func = new Substring3Function();
        var result = await func.InvokeAsync(["hello", 2.0, 3.0], CreateContext());

        result.Should().Be("ell");
    }

    [Fact]
    public async Task Substring_WithLengthExceedingEnd_ReturnsTillEnd()
    {
        var func = new Substring3Function();
        var result = await func.InvokeAsync(["hello", 3.0, 10.0], CreateContext());

        result.Should().Be("llo");
    }

    [Fact]
    public async Task Substring_NullSource_ReturnsEmpty()
    {
        var func = new SubstringFunction();
        var result = await func.InvokeAsync([null, 1.0], CreateContext());

        result.Should().Be("");
    }

    [Fact]
    public void Substring_Name_IsFnSubstring()
    {
        var func = new SubstringFunction();

        func.Name.LocalName.Should().Be("substring");
    }

    [Fact]
    public void Substring2_Arity_IsTwo()
    {
        var func = new SubstringFunction();

        func.Arity.Should().Be(2);
    }

    [Fact]
    public void Substring3_Arity_IsThree()
    {
        var func = new Substring3Function();

        func.Arity.Should().Be(3);
    }

    #endregion

    #region concat Tests

    [Fact]
    public async Task Concat_TwoStrings_ReturnsConcatenated()
    {
        var func = new ConcatFunction();
        var result = await func.InvokeAsync(["hello", " world"], CreateContext());

        result.Should().Be("hello world");
    }

    [Fact]
    public async Task Concat_WithNull_TreatsAsEmpty()
    {
        var func = new ConcatFunction();
        var result = await func.InvokeAsync(["hello", null], CreateContext());

        result.Should().Be("hello");
    }

    [Fact]
    public async Task Concat_BothEmpty_ReturnsEmpty()
    {
        var func = new ConcatFunction();
        var result = await func.InvokeAsync(["", ""], CreateContext());

        result.Should().Be("");
    }

    [Fact]
    public void Concat_Name_IsFnConcat()
    {
        var func = new ConcatFunction();

        func.Name.LocalName.Should().Be("concat");
    }

    [Fact]
    public void Concat_ReturnType_IsString()
    {
        var func = new ConcatFunction();

        func.ReturnType.Should().Be(XdmSequenceType.String);
    }

    #endregion

    #region string-join Tests

    [Fact]
    public async Task StringJoin_WithSeparator_ReturnsJoined()
    {
        var func = new StringJoinFunction();
        var items = new object[] { "a", "b", "c" };
        var result = await func.InvokeAsync([items, ", "], CreateContext());

        result.Should().Be("a, b, c");
    }

    [Fact]
    public async Task StringJoin_EmptySequence_ReturnsEmpty()
    {
        var func = new StringJoinFunction();
        var items = Array.Empty<object>();
        var result = await func.InvokeAsync([items, ", "], CreateContext());

        result.Should().Be("");
    }

    [Fact]
    public async Task StringJoin_SingleItem_NoSeparator()
    {
        var func = new StringJoinFunction();
        var items = new object[] { "only" };
        var result = await func.InvokeAsync([items, ", "], CreateContext());

        result.Should().Be("only");
    }

    [Fact]
    public async Task StringJoin_EmptySeparator_Concatenates()
    {
        var func = new StringJoinFunction();
        var items = new object[] { "a", "b", "c" };
        var result = await func.InvokeAsync([items, ""], CreateContext());

        result.Should().Be("abc");
    }

    [Fact]
    public void StringJoin_Name_IsFnStringJoin()
    {
        var func = new StringJoinFunction();

        func.Name.LocalName.Should().Be("string-join");
    }

    #endregion

    #region contains Tests

    [Fact]
    public async Task Contains_SubstringPresent_ReturnsTrue()
    {
        var func = new ContainsFunction();
        var result = await func.InvokeAsync(["hello world", "world"], CreateContext());

        result.Should().Be(true);
    }

    [Fact]
    public async Task Contains_SubstringNotPresent_ReturnsFalse()
    {
        var func = new ContainsFunction();
        var result = await func.InvokeAsync(["hello world", "planet"], CreateContext());

        result.Should().Be(false);
    }

    [Fact]
    public async Task Contains_EmptySubstring_ReturnsTrue()
    {
        var func = new ContainsFunction();
        var result = await func.InvokeAsync(["hello", ""], CreateContext());

        result.Should().Be(true);
    }

    [Fact]
    public async Task Contains_EmptyString_ReturnsFalse()
    {
        var func = new ContainsFunction();
        var result = await func.InvokeAsync(["", "hello"], CreateContext());

        result.Should().Be(false);
    }

    [Fact]
    public async Task Contains_NullArguments_HandledGracefully()
    {
        var func = new ContainsFunction();
        var result = await func.InvokeAsync([null, null], CreateContext());

        result.Should().Be(true); // Empty contains empty
    }

    [Fact]
    public void Contains_Name_IsFnContains()
    {
        var func = new ContainsFunction();

        func.Name.LocalName.Should().Be("contains");
    }

    [Fact]
    public void Contains_ReturnType_IsBoolean()
    {
        var func = new ContainsFunction();

        func.ReturnType.Should().Be(XdmSequenceType.Boolean);
    }

    #endregion

    #region starts-with Tests

    [Fact]
    public async Task StartsWith_ValidPrefix_ReturnsTrue()
    {
        var func = new StartsWithFunction();
        var result = await func.InvokeAsync(["hello world", "hello"], CreateContext());

        result.Should().Be(true);
    }

    [Fact]
    public async Task StartsWith_InvalidPrefix_ReturnsFalse()
    {
        var func = new StartsWithFunction();
        var result = await func.InvokeAsync(["hello world", "world"], CreateContext());

        result.Should().Be(false);
    }

    [Fact]
    public async Task StartsWith_EmptyPrefix_ReturnsTrue()
    {
        var func = new StartsWithFunction();
        var result = await func.InvokeAsync(["hello", ""], CreateContext());

        result.Should().Be(true);
    }

    [Fact]
    public async Task StartsWith_PrefixLongerThanString_ReturnsFalse()
    {
        var func = new StartsWithFunction();
        var result = await func.InvokeAsync(["hi", "hello"], CreateContext());

        result.Should().Be(false);
    }

    [Fact]
    public void StartsWith_Name_IsFnStartsWith()
    {
        var func = new StartsWithFunction();

        func.Name.LocalName.Should().Be("starts-with");
    }

    #endregion

    #region ends-with Tests

    [Fact]
    public async Task EndsWith_ValidSuffix_ReturnsTrue()
    {
        var func = new EndsWithFunction();
        var result = await func.InvokeAsync(["hello world", "world"], CreateContext());

        result.Should().Be(true);
    }

    [Fact]
    public async Task EndsWith_InvalidSuffix_ReturnsFalse()
    {
        var func = new EndsWithFunction();
        var result = await func.InvokeAsync(["hello world", "hello"], CreateContext());

        result.Should().Be(false);
    }

    [Fact]
    public async Task EndsWith_EmptySuffix_ReturnsTrue()
    {
        var func = new EndsWithFunction();
        var result = await func.InvokeAsync(["hello", ""], CreateContext());

        result.Should().Be(true);
    }

    [Fact]
    public async Task EndsWith_SuffixLongerThanString_ReturnsFalse()
    {
        var func = new EndsWithFunction();
        var result = await func.InvokeAsync(["hi", "hello"], CreateContext());

        result.Should().Be(false);
    }

    [Fact]
    public void EndsWith_Name_IsFnEndsWith()
    {
        var func = new EndsWithFunction();

        func.Name.LocalName.Should().Be("ends-with");
    }

    #endregion

    #region normalize-space Tests

    [Fact]
    public async Task NormalizeSpace_LeadingTrailingSpaces_Trimmed()
    {
        var func = new NormalizeSpaceFunction();
        var result = await func.InvokeAsync(["  hello  "], CreateContext());

        result.Should().Be("hello");
    }

    [Fact]
    public async Task NormalizeSpace_MultipleInternalSpaces_SingleSpace()
    {
        var func = new NormalizeSpaceFunction();
        var result = await func.InvokeAsync(["hello    world"], CreateContext());

        result.Should().Be("hello world");
    }

    [Fact]
    public async Task NormalizeSpace_Tabs_ConvertedToSpace()
    {
        var func = new NormalizeSpaceFunction();
        var result = await func.InvokeAsync(["hello\tworld"], CreateContext());

        result.Should().Be("hello world");
    }

    [Fact]
    public async Task NormalizeSpace_Newlines_ConvertedToSpace()
    {
        var func = new NormalizeSpaceFunction();
        var result = await func.InvokeAsync(["hello\nworld"], CreateContext());

        result.Should().Be("hello world");
    }

    [Fact]
    public async Task NormalizeSpace_EmptyString_ReturnsEmpty()
    {
        var func = new NormalizeSpaceFunction();
        var result = await func.InvokeAsync([""], CreateContext());

        result.Should().Be("");
    }

    [Fact]
    public async Task NormalizeSpace_OnlySpaces_ReturnsEmpty()
    {
        var func = new NormalizeSpaceFunction();
        var result = await func.InvokeAsync(["     "], CreateContext());

        result.Should().Be("");
    }

    [Fact]
    public void NormalizeSpace_Name_IsFnNormalizeSpace()
    {
        var func = new NormalizeSpaceFunction();

        func.Name.LocalName.Should().Be("normalize-space");
    }

    [Fact]
    public void NormalizeSpace0_Arity_IsZero()
    {
        var func = new NormalizeSpace0Function();

        func.Arity.Should().Be(0);
    }

    #endregion

    #region upper-case Tests

    [Fact]
    public async Task UpperCase_LowercaseString_ReturnsUppercase()
    {
        var func = new UpperCaseFunction();
        var result = await func.InvokeAsync(["hello"], CreateContext());

        result.Should().Be("HELLO");
    }

    [Fact]
    public async Task UpperCase_MixedCase_ReturnsUppercase()
    {
        var func = new UpperCaseFunction();
        var result = await func.InvokeAsync(["HeLLo WoRLd"], CreateContext());

        result.Should().Be("HELLO WORLD");
    }

    [Fact]
    public async Task UpperCase_EmptyString_ReturnsEmpty()
    {
        var func = new UpperCaseFunction();
        var result = await func.InvokeAsync([""], CreateContext());

        result.Should().Be("");
    }

    [Fact]
    public async Task UpperCase_AlreadyUppercase_ReturnsSame()
    {
        var func = new UpperCaseFunction();
        var result = await func.InvokeAsync(["HELLO"], CreateContext());

        result.Should().Be("HELLO");
    }

    [Fact]
    public async Task UpperCase_WithNumbers_PreservesNumbers()
    {
        var func = new UpperCaseFunction();
        var result = await func.InvokeAsync(["abc123"], CreateContext());

        result.Should().Be("ABC123");
    }

    [Fact]
    public void UpperCase_Name_IsFnUpperCase()
    {
        var func = new UpperCaseFunction();

        func.Name.LocalName.Should().Be("upper-case");
    }

    #endregion

    #region lower-case Tests

    [Fact]
    public async Task LowerCase_UppercaseString_ReturnsLowercase()
    {
        var func = new LowerCaseFunction();
        var result = await func.InvokeAsync(["HELLO"], CreateContext());

        result.Should().Be("hello");
    }

    [Fact]
    public async Task LowerCase_MixedCase_ReturnsLowercase()
    {
        var func = new LowerCaseFunction();
        var result = await func.InvokeAsync(["HeLLo WoRLd"], CreateContext());

        result.Should().Be("hello world");
    }

    [Fact]
    public async Task LowerCase_EmptyString_ReturnsEmpty()
    {
        var func = new LowerCaseFunction();
        var result = await func.InvokeAsync([""], CreateContext());

        result.Should().Be("");
    }

    [Fact]
    public async Task LowerCase_AlreadyLowercase_ReturnsSame()
    {
        var func = new LowerCaseFunction();
        var result = await func.InvokeAsync(["hello"], CreateContext());

        result.Should().Be("hello");
    }

    [Fact]
    public async Task LowerCase_WithNumbers_PreservesNumbers()
    {
        var func = new LowerCaseFunction();
        var result = await func.InvokeAsync(["ABC123"], CreateContext());

        result.Should().Be("abc123");
    }

    [Fact]
    public void LowerCase_Name_IsFnLowerCase()
    {
        var func = new LowerCaseFunction();

        func.Name.LocalName.Should().Be("lower-case");
    }

    #endregion

    #region translate Tests

    [Fact]
    public async Task Translate_ReplaceCharacters_ReturnsTranslated()
    {
        var func = new TranslateFunction();
        var result = await func.InvokeAsync(["abcd", "abc", "xyz"], CreateContext());

        result.Should().Be("xyzd");
    }

    [Fact]
    public async Task Translate_DeleteCharacters_ReturnsWithoutDeleted()
    {
        var func = new TranslateFunction();
        var result = await func.InvokeAsync(["abcdef", "bdf", ""], CreateContext());

        result.Should().Be("ace");
    }

    [Fact]
    public async Task Translate_NoMatch_ReturnsSame()
    {
        var func = new TranslateFunction();
        var result = await func.InvokeAsync(["hello", "xyz", "123"], CreateContext());

        result.Should().Be("hello");
    }

    [Fact]
    public async Task Translate_EmptyString_ReturnsEmpty()
    {
        var func = new TranslateFunction();
        var result = await func.InvokeAsync(["", "abc", "xyz"], CreateContext());

        result.Should().Be("");
    }

    [Fact]
    public async Task Translate_ShorterReplacement_DeletesExtra()
    {
        var func = new TranslateFunction();
        var result = await func.InvokeAsync(["abcabc", "abc", "x"], CreateContext());

        result.Should().Be("xx");
    }

    [Fact]
    public void Translate_Name_IsFnTranslate()
    {
        var func = new TranslateFunction();

        func.Name.LocalName.Should().Be("translate");
    }

    [Fact]
    public void Translate_Arity_IsThree()
    {
        var func = new TranslateFunction();

        func.Arity.Should().Be(3);
    }

    #endregion

    #region string Tests

    [Fact]
    public async Task String_IntegerArgument_ReturnsStringRepresentation()
    {
        var func = new StringFunction();
        var result = await func.InvokeAsync([42], CreateContext());

        result.Should().Be("42");
    }

    [Fact]
    public async Task String_StringArgument_ReturnsSame()
    {
        var func = new StringFunction();
        var result = await func.InvokeAsync(["hello"], CreateContext());

        result.Should().Be("hello");
    }

    [Fact]
    public async Task String_NullArgument_ReturnsEmpty()
    {
        var func = new StringFunction();
        var result = await func.InvokeAsync([null], CreateContext());

        result.Should().Be("");
    }

    [Fact]
    public void String_Name_IsFnString()
    {
        var func = new StringFunction();

        func.Name.LocalName.Should().Be("string");
    }

    [Fact]
    public void String0_Arity_IsZero()
    {
        var func = new String0Function();

        func.Arity.Should().Be(0);
    }

    #endregion

    #region tokenize Tests

    [Fact]
    public async Task Tokenize_SimplePattern_ReturnsTokens()
    {
        var func = new TokenizeFunction();
        var result = await func.InvokeAsync(["hello world test", " "], CreateContext()) as string[];

        result.Should().BeEquivalentTo(["hello", "world", "test"]);
    }

    [Fact]
    public async Task Tokenize_EmptyString_ReturnsEmptyArray()
    {
        var func = new TokenizeFunction();
        var result = await func.InvokeAsync(["", " "], CreateContext()) as string[];

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Tokenize_NoMatch_ReturnsSingleToken()
    {
        var func = new TokenizeFunction();
        var result = await func.InvokeAsync(["hello", ","], CreateContext()) as string[];

        result.Should().BeEquivalentTo(["hello"]);
    }

    [Fact]
    public void Tokenize_Name_IsFnTokenize()
    {
        var func = new TokenizeFunction();

        func.Name.LocalName.Should().Be("tokenize");
    }

    [Fact]
    public void Tokenize_Arity_IsTwo()
    {
        var func = new TokenizeFunction();

        func.Arity.Should().Be(2);
    }

    #endregion

    #region FunctionLibrary Registration Tests

    [Fact]
    public void FunctionLibrary_ContainsStringLength()
    {
        var lib = FunctionLibrary.Standard;
        var func = lib.Resolve(new QName(FunctionNamespaces.Fn, "string-length"), 1);

        func.Should().NotBeNull();
        func.Should().BeOfType<StringLengthFunction>();
    }

    [Fact]
    public void FunctionLibrary_ContainsSubstring()
    {
        var lib = FunctionLibrary.Standard;

        lib.Resolve(new QName(FunctionNamespaces.Fn, "substring"), 2).Should().NotBeNull();
        lib.Resolve(new QName(FunctionNamespaces.Fn, "substring"), 3).Should().NotBeNull();
    }

    [Fact]
    public void FunctionLibrary_ContainsConcat()
    {
        var lib = FunctionLibrary.Standard;
        var func = lib.Resolve(new QName(FunctionNamespaces.Fn, "concat"), 2);

        func.Should().NotBeNull();
    }

    [Fact]
    public void FunctionLibrary_ContainsStringJoin()
    {
        var lib = FunctionLibrary.Standard;
        var func = lib.Resolve(new QName(FunctionNamespaces.Fn, "string-join"), 2);

        func.Should().NotBeNull();
    }

    [Fact]
    public void FunctionLibrary_ContainsContains()
    {
        var lib = FunctionLibrary.Standard;
        var func = lib.Resolve(new QName(FunctionNamespaces.Fn, "contains"), 2);

        func.Should().NotBeNull();
    }

    [Fact]
    public void FunctionLibrary_ContainsStartsWith()
    {
        var lib = FunctionLibrary.Standard;
        var func = lib.Resolve(new QName(FunctionNamespaces.Fn, "starts-with"), 2);

        func.Should().NotBeNull();
    }

    [Fact]
    public void FunctionLibrary_ContainsEndsWith()
    {
        var lib = FunctionLibrary.Standard;
        var func = lib.Resolve(new QName(FunctionNamespaces.Fn, "ends-with"), 2);

        func.Should().NotBeNull();
    }

    [Fact]
    public void FunctionLibrary_ContainsNormalizeSpace()
    {
        var lib = FunctionLibrary.Standard;

        lib.Resolve(new QName(FunctionNamespaces.Fn, "normalize-space"), 0).Should().NotBeNull();
        lib.Resolve(new QName(FunctionNamespaces.Fn, "normalize-space"), 1).Should().NotBeNull();
    }

    [Fact]
    public void FunctionLibrary_ContainsUpperCase()
    {
        var lib = FunctionLibrary.Standard;
        var func = lib.Resolve(new QName(FunctionNamespaces.Fn, "upper-case"), 1);

        func.Should().NotBeNull();
    }

    [Fact]
    public void FunctionLibrary_ContainsLowerCase()
    {
        var lib = FunctionLibrary.Standard;
        var func = lib.Resolve(new QName(FunctionNamespaces.Fn, "lower-case"), 1);

        func.Should().NotBeNull();
    }

    [Fact]
    public void FunctionLibrary_ContainsTranslate()
    {
        var lib = FunctionLibrary.Standard;
        var func = lib.Resolve(new QName(FunctionNamespaces.Fn, "translate"), 3);

        func.Should().NotBeNull();
    }

    [Fact]
    public void FunctionLibrary_ContainsString()
    {
        var lib = FunctionLibrary.Standard;

        lib.Resolve(new QName(FunctionNamespaces.Fn, "string"), 0).Should().NotBeNull();
        lib.Resolve(new QName(FunctionNamespaces.Fn, "string"), 1).Should().NotBeNull();
    }

    [Fact]
    public void FunctionLibrary_ContainsTokenize()
    {
        var lib = FunctionLibrary.Standard;
        var func = lib.Resolve(new QName(FunctionNamespaces.Fn, "tokenize"), 2);

        func.Should().NotBeNull();
    }

    #endregion

    #region Helper Methods

    private static XQueryExecutionContext CreateContext()
    {
        return new TestExecutionContext();
    }

    private class TestExecutionContext : XQueryExecutionContext
    {
        public object? ContextItem => null;
    }

    #endregion
}
