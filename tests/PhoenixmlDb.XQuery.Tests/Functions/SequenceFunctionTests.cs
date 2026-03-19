using FluentAssertions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Functions;
using Xunit;
using XQueryExecutionContext = PhoenixmlDb.XQuery.Ast.ExecutionContext;

namespace PhoenixmlDb.XQuery.Tests.Functions;

/// <summary>
/// Tests for sequence functions.
/// </summary>
public class SequenceFunctionTests
{
    #region empty Tests

    [Fact]
    public async Task Empty_NullArgument_ReturnsTrue()
    {
        var func = new EmptyFunction();
        var result = await func.InvokeAsync([null], CreateContext());

        result.Should().Be(true);
    }

    [Fact]
    public async Task Empty_EmptySequence_ReturnsTrue()
    {
        var func = new EmptyFunction();
        var items = Array.Empty<object>();
        var result = await func.InvokeAsync([items], CreateContext());

        result.Should().Be(true);
    }

    [Fact]
    public async Task Empty_NonEmptySequence_ReturnsFalse()
    {
        var func = new EmptyFunction();
        var items = new object[] { 1, 2, 3 };
        var result = await func.InvokeAsync([items], CreateContext());

        result.Should().Be(false);
    }

    [Fact]
    public async Task Empty_SingleItem_ReturnsFalse()
    {
        var func = new EmptyFunction();
        var result = await func.InvokeAsync(["hello"], CreateContext());

        result.Should().Be(false);
    }

    [Fact]
    public void Empty_Name_IsFnEmpty()
    {
        var func = new EmptyFunction();

        func.Name.LocalName.Should().Be("empty");
        func.Name.Namespace.Should().Be(FunctionNamespaces.Fn);
    }

    [Fact]
    public void Empty_ReturnType_IsBoolean()
    {
        var func = new EmptyFunction();

        func.ReturnType.Should().Be(XdmSequenceType.Boolean);
    }

    [Fact]
    public void Empty_Arity_IsOne()
    {
        var func = new EmptyFunction();

        func.Arity.Should().Be(1);
    }

    #endregion

    #region exists Tests

    [Fact]
    public async Task Exists_NullArgument_ReturnsFalse()
    {
        var func = new ExistsFunction();
        var result = await func.InvokeAsync([null], CreateContext());

        result.Should().Be(false);
    }

    [Fact]
    public async Task Exists_EmptySequence_ReturnsFalse()
    {
        var func = new ExistsFunction();
        var items = Array.Empty<object>();
        var result = await func.InvokeAsync([items], CreateContext());

        result.Should().Be(false);
    }

    [Fact]
    public async Task Exists_NonEmptySequence_ReturnsTrue()
    {
        var func = new ExistsFunction();
        var items = new object[] { 1, 2, 3 };
        var result = await func.InvokeAsync([items], CreateContext());

        result.Should().Be(true);
    }

    [Fact]
    public async Task Exists_SingleItem_ReturnsTrue()
    {
        var func = new ExistsFunction();
        var result = await func.InvokeAsync(["hello"], CreateContext());

        result.Should().Be(true);
    }

    [Fact]
    public void Exists_Name_IsFnExists()
    {
        var func = new ExistsFunction();

        func.Name.LocalName.Should().Be("exists");
    }

    [Fact]
    public void Exists_ReturnType_IsBoolean()
    {
        var func = new ExistsFunction();

        func.ReturnType.Should().Be(XdmSequenceType.Boolean);
    }

    #endregion

    #region head Tests

    [Fact]
    public async Task Head_NonEmptySequence_ReturnsFirstItem()
    {
        var func = new HeadFunction();
        var items = new object[] { 1, 2, 3 };
        var result = await func.InvokeAsync([items], CreateContext());

        result.Should().Be(1);
    }

    [Fact]
    public async Task Head_SingleItem_ReturnsThatItem()
    {
        var func = new HeadFunction();
        var items = new object[] { 42 };
        var result = await func.InvokeAsync([items], CreateContext());

        result.Should().Be(42);
    }

    [Fact]
    public async Task Head_EmptySequence_ReturnsNull()
    {
        var func = new HeadFunction();
        var items = Array.Empty<object>();
        var result = await func.InvokeAsync([items], CreateContext());

        result.Should().BeNull();
    }

    [Fact]
    public async Task Head_NullArgument_ReturnsNull()
    {
        var func = new HeadFunction();
        var result = await func.InvokeAsync([null], CreateContext());

        result.Should().BeNull();
    }

    [Fact]
    public async Task Head_NonSequenceItem_ReturnsThatItem()
    {
        var func = new HeadFunction();
        var result = await func.InvokeAsync(["hello"], CreateContext());

        result.Should().Be("hello");
    }

    [Fact]
    public void Head_Name_IsFnHead()
    {
        var func = new HeadFunction();

        func.Name.LocalName.Should().Be("head");
    }

    [Fact]
    public void Head_ReturnType_IsOptionalItem()
    {
        var func = new HeadFunction();

        func.ReturnType.Should().Be(XdmSequenceType.OptionalItem);
    }

    #endregion

    #region tail Tests

    [Fact]
    public async Task Tail_NonEmptySequence_ReturnsAllButFirst()
    {
        var func = new TailFunction();
        var items = new object[] { 1, 2, 3, 4, 5 };
        var result = await func.InvokeAsync([items], CreateContext()) as IEnumerable<object?>;

        result.Should().BeEquivalentTo([2, 3, 4, 5]);
    }

    [Fact]
    public async Task Tail_TwoItems_ReturnsSecondItem()
    {
        var func = new TailFunction();
        var items = new object[] { 1, 2 };
        var result = await func.InvokeAsync([items], CreateContext()) as IEnumerable<object?>;

        result.Should().BeEquivalentTo([2]);
    }

    [Fact]
    public async Task Tail_SingleItem_ReturnsEmpty()
    {
        var func = new TailFunction();
        var items = new object[] { 42 };
        var result = await func.InvokeAsync([items], CreateContext()) as IEnumerable<object?>;

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Tail_EmptySequence_ReturnsEmpty()
    {
        var func = new TailFunction();
        var items = Array.Empty<object>();
        var result = await func.InvokeAsync([items], CreateContext()) as IEnumerable<object?>;

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Tail_NullArgument_ReturnsEmpty()
    {
        var func = new TailFunction();
        var result = await func.InvokeAsync([null], CreateContext()) as object[];

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Tail_NonSequenceItem_ReturnsEmpty()
    {
        var func = new TailFunction();
        var result = await func.InvokeAsync(["hello"], CreateContext()) as object[];

        result.Should().BeEmpty();
    }

    [Fact]
    public void Tail_Name_IsFnTail()
    {
        var func = new TailFunction();

        func.Name.LocalName.Should().Be("tail");
    }

    [Fact]
    public void Tail_ReturnType_IsZeroOrMoreItems()
    {
        var func = new TailFunction();

        func.ReturnType.Should().Be(XdmSequenceType.ZeroOrMoreItems);
    }

    #endregion

    #region reverse Tests

    [Fact]
    public async Task Reverse_NonEmptySequence_ReturnsReversed()
    {
        var func = new ReverseFunction();
        var items = new object[] { 1, 2, 3, 4, 5 };
        var result = await func.InvokeAsync([items], CreateContext()) as object[];

        result.Should().BeEquivalentTo([5, 4, 3, 2, 1], o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task Reverse_TwoItems_ReturnsSwapped()
    {
        var func = new ReverseFunction();
        var items = new object[] { "a", "b" };
        var result = await func.InvokeAsync([items], CreateContext()) as object[];

        result.Should().BeEquivalentTo(["b", "a"], o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task Reverse_SingleItem_ReturnsSame()
    {
        var func = new ReverseFunction();
        var items = new object[] { 42 };
        var result = await func.InvokeAsync([items], CreateContext()) as object[];

        result.Should().BeEquivalentTo([42]);
    }

    [Fact]
    public async Task Reverse_EmptySequence_ReturnsEmpty()
    {
        var func = new ReverseFunction();
        var items = Array.Empty<object>();
        var result = await func.InvokeAsync([items], CreateContext()) as object[];

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Reverse_NullArgument_ReturnsEmpty()
    {
        var func = new ReverseFunction();
        var result = await func.InvokeAsync([null], CreateContext()) as object[];

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Reverse_NonSequenceItem_ReturnsSequenceWithItem()
    {
        var func = new ReverseFunction();
        var result = await func.InvokeAsync(["hello"], CreateContext()) as object[];

        result.Should().BeEquivalentTo(["hello"]);
    }

    [Fact]
    public void Reverse_Name_IsFnReverse()
    {
        var func = new ReverseFunction();

        func.Name.LocalName.Should().Be("reverse");
    }

    #endregion

    #region distinct-values Tests

    [Fact]
    public async Task DistinctValues_AllUnique_ReturnsSame()
    {
        var func = new DistinctValuesFunction();
        var items = new object[] { 1, 2, 3 };
        var result = await func.InvokeAsync([items], CreateContext()) as IEnumerable<object?>;

        result.Should().BeEquivalentTo([1, 2, 3]);
    }

    [Fact]
    public async Task DistinctValues_WithDuplicates_RemovesDuplicates()
    {
        var func = new DistinctValuesFunction();
        var items = new object[] { 1, 2, 2, 3, 3, 3 };
        var result = await func.InvokeAsync([items], CreateContext()) as IEnumerable<object?>;

        result.Should().BeEquivalentTo([1, 2, 3]);
    }

    [Fact]
    public async Task DistinctValues_AllSame_ReturnsSingle()
    {
        var func = new DistinctValuesFunction();
        var items = new object[] { 5, 5, 5, 5, 5 };
        var result = await func.InvokeAsync([items], CreateContext()) as IEnumerable<object?>;

        result.Should().BeEquivalentTo([5]);
    }

    [Fact]
    public async Task DistinctValues_EmptySequence_ReturnsEmpty()
    {
        var func = new DistinctValuesFunction();
        var items = Array.Empty<object>();
        var result = await func.InvokeAsync([items], CreateContext()) as IEnumerable<object?>;

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task DistinctValues_NullArgument_ReturnsEmpty()
    {
        var func = new DistinctValuesFunction();
        var result = await func.InvokeAsync([null], CreateContext()) as object[];

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task DistinctValues_StringValues_RemovesDuplicates()
    {
        var func = new DistinctValuesFunction();
        var items = new object[] { "a", "b", "a", "c", "b" };
        var result = await func.InvokeAsync([items], CreateContext()) as IEnumerable<object?>;

        result.Should().BeEquivalentTo(["a", "b", "c"]);
    }

    [Fact]
    public void DistinctValues_Name_IsFnDistinctValues()
    {
        var func = new DistinctValuesFunction();

        func.Name.LocalName.Should().Be("distinct-values");
    }

    #endregion

    #region subsequence Tests

    [Fact]
    public async Task Subsequence_FromStart_ReturnsAll()
    {
        var func = new SubsequenceFunction();
        var items = new object[] { 1, 2, 3, 4, 5 };
        var result = await func.InvokeAsync([items, 1.0], CreateContext()) as object[];

        result.Should().BeEquivalentTo([1, 2, 3, 4, 5], o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task Subsequence_FromMiddle_ReturnsRest()
    {
        var func = new SubsequenceFunction();
        var items = new object[] { 1, 2, 3, 4, 5 };
        var result = await func.InvokeAsync([items, 3.0], CreateContext()) as object[];

        result.Should().BeEquivalentTo([3, 4, 5], o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task Subsequence_BeyondEnd_ReturnsEmpty()
    {
        var func = new SubsequenceFunction();
        var items = new object[] { 1, 2, 3 };
        var result = await func.InvokeAsync([items, 10.0], CreateContext()) as object[];

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Subsequence_NegativeStart_ReturnsFromBeginning()
    {
        var func = new SubsequenceFunction();
        var items = new object[] { 1, 2, 3, 4, 5 };
        var result = await func.InvokeAsync([items, -1.0], CreateContext()) as object[];

        result.Should().BeEquivalentTo([1, 2, 3, 4, 5], o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task Subsequence_EmptySequence_ReturnsEmpty()
    {
        var func = new SubsequenceFunction();
        var items = Array.Empty<object>();
        var result = await func.InvokeAsync([items, 1.0], CreateContext()) as object[];

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Subsequence_NullArgument_ReturnsEmpty()
    {
        var func = new SubsequenceFunction();
        var result = await func.InvokeAsync([null, 1.0], CreateContext()) as object[];

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Subsequence3_WithLength_ReturnsCorrectPortion()
    {
        var func = new Subsequence3Function();
        var items = new object[] { 1, 2, 3, 4, 5 };
        var result = await func.InvokeAsync([items, 2.0, 3.0], CreateContext()) as object[];

        result.Should().BeEquivalentTo([2, 3, 4], o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task Subsequence3_LengthExceedingEnd_ReturnsTillEnd()
    {
        var func = new Subsequence3Function();
        var items = new object[] { 1, 2, 3, 4, 5 };
        var result = await func.InvokeAsync([items, 4.0, 10.0], CreateContext()) as object[];

        result.Should().BeEquivalentTo([4, 5], o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task Subsequence3_ZeroLength_ReturnsEmpty()
    {
        var func = new Subsequence3Function();
        var items = new object[] { 1, 2, 3, 4, 5 };
        var result = await func.InvokeAsync([items, 2.0, 0.0], CreateContext()) as object[];

        result.Should().BeEmpty();
    }

    [Fact]
    public void Subsequence2_Arity_IsTwo()
    {
        var func = new SubsequenceFunction();

        func.Arity.Should().Be(2);
    }

    [Fact]
    public void Subsequence3_Arity_IsThree()
    {
        var func = new Subsequence3Function();

        func.Arity.Should().Be(3);
    }

    [Fact]
    public void Subsequence_Name_IsFnSubsequence()
    {
        var func = new SubsequenceFunction();

        func.Name.LocalName.Should().Be("subsequence");
    }

    #endregion

    #region insert-before Tests

    [Fact]
    public async Task InsertBefore_AtStart_PrependsItems()
    {
        var func = new InsertBeforeFunction();
        var target = new object[] { 2, 3, 4 };
        var inserts = new object[] { 0, 1 };
        var result = await func.InvokeAsync([target, 1, inserts], CreateContext()) as object[];

        result.Should().BeEquivalentTo([0, 1, 2, 3, 4], o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task InsertBefore_AtMiddle_InsertsCorrectly()
    {
        var func = new InsertBeforeFunction();
        var target = new object[] { 1, 4, 5 };
        var inserts = new object[] { 2, 3 };
        var result = await func.InvokeAsync([target, 2, inserts], CreateContext()) as object[];

        result.Should().BeEquivalentTo([1, 2, 3, 4, 5], o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task InsertBefore_AtEnd_AppendsItems()
    {
        var func = new InsertBeforeFunction();
        var target = new object[] { 1, 2, 3 };
        var inserts = new object[] { 4, 5 };
        var result = await func.InvokeAsync([target, 10, inserts], CreateContext()) as object[];

        result.Should().BeEquivalentTo([1, 2, 3, 4, 5], o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task InsertBefore_SingleItem_InsertsItem()
    {
        var func = new InsertBeforeFunction();
        var target = new object[] { 1, 3 };
        var result = await func.InvokeAsync([target, 2, 2], CreateContext()) as object[];

        result.Should().BeEquivalentTo([1, 2, 3], o => o.WithStrictOrdering());
    }

    [Fact]
    public void InsertBefore_Name_IsFnInsertBefore()
    {
        var func = new InsertBeforeFunction();

        func.Name.LocalName.Should().Be("insert-before");
    }

    [Fact]
    public void InsertBefore_Arity_IsThree()
    {
        var func = new InsertBeforeFunction();

        func.Arity.Should().Be(3);
    }

    #endregion

    #region remove Tests

    [Fact]
    public async Task Remove_FromStart_RemovesFirstItem()
    {
        var func = new RemoveFunction();
        var target = new object[] { 1, 2, 3, 4, 5 };
        var result = await func.InvokeAsync([target, 1], CreateContext()) as object[];

        result.Should().BeEquivalentTo([2, 3, 4, 5], o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task Remove_FromMiddle_RemovesItem()
    {
        var func = new RemoveFunction();
        var target = new object[] { 1, 2, 3, 4, 5 };
        var result = await func.InvokeAsync([target, 3], CreateContext()) as object[];

        result.Should().BeEquivalentTo([1, 2, 4, 5], o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task Remove_FromEnd_RemovesLastItem()
    {
        var func = new RemoveFunction();
        var target = new object[] { 1, 2, 3, 4, 5 };
        var result = await func.InvokeAsync([target, 5], CreateContext()) as object[];

        result.Should().BeEquivalentTo([1, 2, 3, 4], o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task Remove_InvalidPosition_ReturnsUnchanged()
    {
        var func = new RemoveFunction();
        var target = new object[] { 1, 2, 3 };
        var result = await func.InvokeAsync([target, 10], CreateContext()) as object[];

        result.Should().BeEquivalentTo([1, 2, 3], o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task Remove_NegativePosition_ReturnsUnchanged()
    {
        var func = new RemoveFunction();
        var target = new object[] { 1, 2, 3 };
        var result = await func.InvokeAsync([target, -1], CreateContext()) as object[];

        result.Should().BeEquivalentTo([1, 2, 3], o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task Remove_NullTarget_ReturnsEmpty()
    {
        var func = new RemoveFunction();
        var result = await func.InvokeAsync([null, 1], CreateContext()) as object[];

        result.Should().BeEmpty();
    }

    [Fact]
    public void Remove_Name_IsFnRemove()
    {
        var func = new RemoveFunction();

        func.Name.LocalName.Should().Be("remove");
    }

    [Fact]
    public void Remove_Arity_IsTwo()
    {
        var func = new RemoveFunction();

        func.Arity.Should().Be(2);
    }

    #endregion

    #region index-of Tests

    [Fact]
    public async Task IndexOf_SingleMatch_ReturnsPosition()
    {
        var func = new IndexOfFunction();
        var items = new object[] { "a", "b", "c", "d" };
        var result = await func.InvokeAsync([items, "c"], CreateContext()) as object[];

        result.Should().BeEquivalentTo(new object[] { 3L }); // 1-based indexing
    }

    [Fact]
    public async Task IndexOf_MultipleMatches_ReturnsAllPositions()
    {
        var func = new IndexOfFunction();
        var items = new object[] { "a", "b", "a", "b", "a" };
        var result = await func.InvokeAsync([items, "a"], CreateContext()) as object[];

        result.Should().BeEquivalentTo(new object[] { 1L, 3L, 5L }); // 1-based indexing
    }

    [Fact]
    public async Task IndexOf_NoMatch_ReturnsEmpty()
    {
        var func = new IndexOfFunction();
        var items = new object[] { "a", "b", "c" };
        var result = await func.InvokeAsync([items, "z"], CreateContext()) as object[];

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task IndexOf_EmptySequence_ReturnsEmpty()
    {
        var func = new IndexOfFunction();
        var items = Array.Empty<object>();
        var result = await func.InvokeAsync([items, "a"], CreateContext()) as object[];

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task IndexOf_NullSequence_ReturnsEmpty()
    {
        var func = new IndexOfFunction();
        var result = await func.InvokeAsync([null, "a"], CreateContext()) as object[];

        result.Should().BeEmpty();
    }

    [Fact]
    public void IndexOf_Name_IsFnIndexOf()
    {
        var func = new IndexOfFunction();

        func.Name.LocalName.Should().Be("index-of");
    }

    [Fact]
    public void IndexOf_Arity_IsTwo()
    {
        var func = new IndexOfFunction();

        func.Arity.Should().Be(2);
    }

    #endregion

    #region FunctionLibrary Registration Tests

    [Fact]
    public void FunctionLibrary_ContainsEmpty()
    {
        var lib = FunctionLibrary.Standard;
        var func = lib.Resolve(new QName(FunctionNamespaces.Fn, "empty"), 1);

        func.Should().NotBeNull();
        func.Should().BeOfType<EmptyFunction>();
    }

    [Fact]
    public void FunctionLibrary_ContainsExists()
    {
        var lib = FunctionLibrary.Standard;
        var func = lib.Resolve(new QName(FunctionNamespaces.Fn, "exists"), 1);

        func.Should().NotBeNull();
        func.Should().BeOfType<ExistsFunction>();
    }

    [Fact]
    public void FunctionLibrary_ContainsHead()
    {
        var lib = FunctionLibrary.Standard;
        var func = lib.Resolve(new QName(FunctionNamespaces.Fn, "head"), 1);

        func.Should().NotBeNull();
        func.Should().BeOfType<HeadFunction>();
    }

    [Fact]
    public void FunctionLibrary_ContainsTail()
    {
        var lib = FunctionLibrary.Standard;
        var func = lib.Resolve(new QName(FunctionNamespaces.Fn, "tail"), 1);

        func.Should().NotBeNull();
        func.Should().BeOfType<TailFunction>();
    }

    [Fact]
    public void FunctionLibrary_ContainsReverse()
    {
        var lib = FunctionLibrary.Standard;
        var func = lib.Resolve(new QName(FunctionNamespaces.Fn, "reverse"), 1);

        func.Should().NotBeNull();
        func.Should().BeOfType<ReverseFunction>();
    }

    [Fact]
    public void FunctionLibrary_ContainsDistinctValues()
    {
        var lib = FunctionLibrary.Standard;
        var func = lib.Resolve(new QName(FunctionNamespaces.Fn, "distinct-values"), 1);

        func.Should().NotBeNull();
        func.Should().BeOfType<DistinctValuesFunction>();
    }

    [Fact]
    public void FunctionLibrary_ContainsSubsequence()
    {
        var lib = FunctionLibrary.Standard;

        lib.Resolve(new QName(FunctionNamespaces.Fn, "subsequence"), 2).Should().NotBeNull();
        lib.Resolve(new QName(FunctionNamespaces.Fn, "subsequence"), 3).Should().NotBeNull();
    }

    [Fact]
    public void FunctionLibrary_ContainsInsertBefore()
    {
        var lib = FunctionLibrary.Standard;
        var func = lib.Resolve(new QName(FunctionNamespaces.Fn, "insert-before"), 3);

        func.Should().NotBeNull();
        func.Should().BeOfType<InsertBeforeFunction>();
    }

    [Fact]
    public void FunctionLibrary_ContainsRemove()
    {
        var lib = FunctionLibrary.Standard;
        var func = lib.Resolve(new QName(FunctionNamespaces.Fn, "remove"), 2);

        func.Should().NotBeNull();
        func.Should().BeOfType<RemoveFunction>();
    }

    [Fact]
    public void FunctionLibrary_ContainsIndexOf()
    {
        var lib = FunctionLibrary.Standard;
        var func = lib.Resolve(new QName(FunctionNamespaces.Fn, "index-of"), 2);

        func.Should().NotBeNull();
        func.Should().BeOfType<IndexOfFunction>();
    }

    #endregion

    #region Empty and Exists Inverse Property Tests

    [Fact]
    public async Task EmptyAndExists_AreInverses_ForEmptySequence()
    {
        var emptyFunc = new EmptyFunction();
        var existsFunc = new ExistsFunction();
        var items = Array.Empty<object>();

        var emptyResult = await emptyFunc.InvokeAsync([items], CreateContext());
        var existsResult = await existsFunc.InvokeAsync([items], CreateContext());

        emptyResult.Should().Be(true);
        existsResult.Should().Be(false);
    }

    [Fact]
    public async Task EmptyAndExists_AreInverses_ForNonEmptySequence()
    {
        var emptyFunc = new EmptyFunction();
        var existsFunc = new ExistsFunction();
        var items = new object[] { 1, 2, 3 };

        var emptyResult = await emptyFunc.InvokeAsync([items], CreateContext());
        var existsResult = await existsFunc.InvokeAsync([items], CreateContext());

        emptyResult.Should().Be(false);
        existsResult.Should().Be(true);
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
