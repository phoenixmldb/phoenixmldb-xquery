using FluentAssertions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Functions;
using Xunit;
using XQueryExecutionContext = PhoenixmlDb.XQuery.Ast.ExecutionContext;

namespace PhoenixmlDb.XQuery.Tests.Functions;

/// <summary>
/// Tests for numeric functions.
/// </summary>
public class NumericFunctionTests
{
    #region abs Tests

    [Fact]
    public async Task Abs_PositiveNumber_ReturnsSame()
    {
        var func = new AbsFunction();
        var result = await func.InvokeAsync([42.0], CreateContext());

        result.Should().Be(42.0);
    }

    [Fact]
    public async Task Abs_NegativeNumber_ReturnsPositive()
    {
        var func = new AbsFunction();
        var result = await func.InvokeAsync([-42.0], CreateContext());

        result.Should().Be(42.0);
    }

    [Fact]
    public async Task Abs_Zero_ReturnsZero()
    {
        var func = new AbsFunction();
        var result = await func.InvokeAsync([0.0], CreateContext());

        result.Should().Be(0.0);
    }

    [Fact]
    public async Task Abs_NegativeZero_ReturnsZero()
    {
        var func = new AbsFunction();
        var result = await func.InvokeAsync([-0.0], CreateContext());

        result.Should().Be(0.0);
    }

    [Fact]
    public async Task Abs_Decimal_ReturnsAbsoluteValue()
    {
        var func = new AbsFunction();
        var result = await func.InvokeAsync([-3.14], CreateContext());

        result.Should().Be(3.14);
    }

    [Fact]
    public void Abs_Name_IsFnAbs()
    {
        var func = new AbsFunction();

        func.Name.LocalName.Should().Be("abs");
        func.Name.Namespace.Should().Be(FunctionNamespaces.Fn);
    }

    [Fact]
    public void Abs_ReturnType_IsDouble()
    {
        var func = new AbsFunction();

        func.ReturnType.Should().Be(XdmSequenceType.Double);
    }

    [Fact]
    public void Abs_Arity_IsOne()
    {
        var func = new AbsFunction();

        func.Arity.Should().Be(1);
    }

    #endregion

    #region ceiling Tests

    [Fact]
    public async Task Ceiling_PositiveDecimal_ReturnsNextInteger()
    {
        var func = new CeilingFunction();
        var result = await func.InvokeAsync([3.14], CreateContext());

        result.Should().Be(4.0);
    }

    [Fact]
    public async Task Ceiling_NegativeDecimal_ReturnsNextInteger()
    {
        var func = new CeilingFunction();
        var result = await func.InvokeAsync([-3.14], CreateContext());

        result.Should().Be(-3.0);
    }

    [Fact]
    public async Task Ceiling_WholeNumber_ReturnsSame()
    {
        var func = new CeilingFunction();
        var result = await func.InvokeAsync([5.0], CreateContext());

        result.Should().Be(5.0);
    }

    [Fact]
    public async Task Ceiling_Zero_ReturnsZero()
    {
        var func = new CeilingFunction();
        var result = await func.InvokeAsync([0.0], CreateContext());

        result.Should().Be(0.0);
    }

    [Fact]
    public async Task Ceiling_SmallPositive_ReturnsOne()
    {
        var func = new CeilingFunction();
        var result = await func.InvokeAsync([0.1], CreateContext());

        result.Should().Be(1.0);
    }

    [Fact]
    public void Ceiling_Name_IsFnCeiling()
    {
        var func = new CeilingFunction();

        func.Name.LocalName.Should().Be("ceiling");
    }

    #endregion

    #region floor Tests

    [Fact]
    public async Task Floor_PositiveDecimal_ReturnsPreviousInteger()
    {
        var func = new FloorFunction();
        var result = await func.InvokeAsync([3.14], CreateContext());

        result.Should().Be(3.0);
    }

    [Fact]
    public async Task Floor_NegativeDecimal_ReturnsPreviousInteger()
    {
        var func = new FloorFunction();
        var result = await func.InvokeAsync([-3.14], CreateContext());

        result.Should().Be(-4.0);
    }

    [Fact]
    public async Task Floor_WholeNumber_ReturnsSame()
    {
        var func = new FloorFunction();
        var result = await func.InvokeAsync([5.0], CreateContext());

        result.Should().Be(5.0);
    }

    [Fact]
    public async Task Floor_Zero_ReturnsZero()
    {
        var func = new FloorFunction();
        var result = await func.InvokeAsync([0.0], CreateContext());

        result.Should().Be(0.0);
    }

    [Fact]
    public async Task Floor_SmallNegative_ReturnsMinusOne()
    {
        var func = new FloorFunction();
        var result = await func.InvokeAsync([-0.1], CreateContext());

        result.Should().Be(-1.0);
    }

    [Fact]
    public void Floor_Name_IsFnFloor()
    {
        var func = new FloorFunction();

        func.Name.LocalName.Should().Be("floor");
    }

    #endregion

    #region round Tests

    [Fact]
    public async Task Round_PositiveHalf_RoundsUp()
    {
        var func = new RoundFunction();
        var result = await func.InvokeAsync([2.5], CreateContext());

        result.Should().Be(3.0);
    }

    [Fact]
    public async Task Round_NegativeHalf_RoundsTowardsPositiveInfinity()
    {
        // XPath spec: "rounds towards positive infinity" for halfway values
        // round(-2.5) → -2.0 (closer to +∞), not -3.0 (away from zero)
        var func = new RoundFunction();
        var result = await func.InvokeAsync([-2.5], CreateContext());

        result.Should().Be(-2.0);
    }

    [Fact]
    public async Task Round_PositiveLessThanHalf_RoundsDown()
    {
        var func = new RoundFunction();
        var result = await func.InvokeAsync([2.4], CreateContext());

        result.Should().Be(2.0);
    }

    [Fact]
    public async Task Round_PositiveMoreThanHalf_RoundsUp()
    {
        var func = new RoundFunction();
        var result = await func.InvokeAsync([2.6], CreateContext());

        result.Should().Be(3.0);
    }

    [Fact]
    public async Task Round_WholeNumber_ReturnsSame()
    {
        var func = new RoundFunction();
        var result = await func.InvokeAsync([5.0], CreateContext());

        result.Should().Be(5.0);
    }

    [Fact]
    public async Task Round_Zero_ReturnsZero()
    {
        var func = new RoundFunction();
        var result = await func.InvokeAsync([0.0], CreateContext());

        result.Should().Be(0.0);
    }

    [Fact]
    public void Round_Name_IsFnRound()
    {
        var func = new RoundFunction();

        func.Name.LocalName.Should().Be("round");
    }

    #endregion

    #region sum Tests

    [Fact]
    public async Task Sum_SingleNumber_ReturnsSame()
    {
        var func = new SumFunction();
        var items = new object[] { 5.0 };
        var result = await func.InvokeAsync([items], CreateContext());

        result.Should().Be(5.0);
    }

    [Fact]
    public async Task Sum_MultipleNumbers_ReturnsSum()
    {
        var func = new SumFunction();
        var items = new object[] { 1.0, 2.0, 3.0, 4.0, 5.0 };
        var result = await func.InvokeAsync([items], CreateContext());

        result.Should().Be(15.0);
    }

    [Fact]
    public async Task Sum_EmptySequence_ReturnsZero()
    {
        var func = new SumFunction();
        var items = Array.Empty<object>();
        var result = await func.InvokeAsync([items], CreateContext());

        result.Should().Be(0.0);
    }

    [Fact]
    public async Task Sum_MixedPositiveAndNegative_ReturnsCorrectSum()
    {
        var func = new SumFunction();
        var items = new object[] { 10.0, -5.0, 3.0 };
        var result = await func.InvokeAsync([items], CreateContext());

        result.Should().Be(8.0);
    }

    [Fact]
    public async Task Sum_StringNumbers_ParsesAndSums()
    {
        var func = new SumFunction();
        var items = new object[] { "1", "2", "3" };
        var result = await func.InvokeAsync([items], CreateContext());

        result.Should().Be(6.0);
    }

    [Fact]
    public void Sum_Name_IsFnSum()
    {
        var func = new SumFunction();

        func.Name.LocalName.Should().Be("sum");
    }

    [Fact]
    public void Sum_Arity_IsOne()
    {
        var func = new SumFunction();

        func.Arity.Should().Be(1);
    }

    #endregion

    #region avg Tests

    [Fact]
    public async Task Avg_SingleNumber_ReturnsSame()
    {
        var func = new AvgFunction();
        var items = new object[] { 5.0 };
        var result = await func.InvokeAsync([items], CreateContext());

        result.Should().Be(5.0);
    }

    [Fact]
    public async Task Avg_MultipleNumbers_ReturnsAverage()
    {
        var func = new AvgFunction();
        var items = new object[] { 1.0, 2.0, 3.0, 4.0, 5.0 };
        var result = await func.InvokeAsync([items], CreateContext());

        result.Should().Be(3.0);
    }

    [Fact]
    public async Task Avg_EmptySequence_ReturnsNull()
    {
        var func = new AvgFunction();
        var items = Array.Empty<object>();
        var result = await func.InvokeAsync([items], CreateContext());

        result.Should().BeNull();
    }

    [Fact]
    public async Task Avg_TwoNumbers_ReturnsCorrectAverage()
    {
        var func = new AvgFunction();
        var items = new object[] { 10.0, 20.0 };
        var result = await func.InvokeAsync([items], CreateContext());

        result.Should().Be(15.0);
    }

    [Fact]
    public async Task Avg_NonWholeResult_ReturnsDecimal()
    {
        var func = new AvgFunction();
        var items = new object[] { 1.0, 2.0 };
        var result = await func.InvokeAsync([items], CreateContext());

        result.Should().Be(1.5);
    }

    [Fact]
    public void Avg_Name_IsFnAvg()
    {
        var func = new AvgFunction();

        func.Name.LocalName.Should().Be("avg");
    }

    [Fact]
    public void Avg_ReturnType_IsOptionalDouble()
    {
        var func = new AvgFunction();

        func.ReturnType.Occurrence.Should().Be(Occurrence.ZeroOrOne);
    }

    #endregion

    #region min Tests

    [Fact]
    public async Task Min_SingleNumber_ReturnsSame()
    {
        var func = new MinFunction();
        var items = new object[] { 5.0 };
        var result = await func.InvokeAsync([items], CreateContext());

        result.Should().Be(5.0);
    }

    [Fact]
    public async Task Min_MultipleNumbers_ReturnsMinimum()
    {
        var func = new MinFunction();
        var items = new object[] { 3.0, 1.0, 4.0, 1.0, 5.0 };
        var result = await func.InvokeAsync([items], CreateContext());

        result.Should().Be(1.0);
    }

    [Fact]
    public async Task Min_EmptySequence_ReturnsNull()
    {
        var func = new MinFunction();
        var items = Array.Empty<object>();
        var result = await func.InvokeAsync([items], CreateContext());

        result.Should().BeNull();
    }

    [Fact]
    public async Task Min_NegativeNumbers_ReturnsSmallest()
    {
        var func = new MinFunction();
        var items = new object[] { -3.0, -1.0, -5.0 };
        var result = await func.InvokeAsync([items], CreateContext());

        result.Should().Be(-5.0);
    }

    [Fact]
    public async Task Min_MixedPositiveAndNegative_ReturnsSmallest()
    {
        var func = new MinFunction();
        var items = new object[] { 10.0, -5.0, 0.0, 3.0 };
        var result = await func.InvokeAsync([items], CreateContext());

        result.Should().Be(-5.0);
    }

    [Fact]
    public void Min_Name_IsFnMin()
    {
        var func = new MinFunction();

        func.Name.LocalName.Should().Be("min");
    }

    #endregion

    #region max Tests

    [Fact]
    public async Task Max_SingleNumber_ReturnsSame()
    {
        var func = new MaxFunction();
        var items = new object[] { 5.0 };
        var result = await func.InvokeAsync([items], CreateContext());

        result.Should().Be(5.0);
    }

    [Fact]
    public async Task Max_MultipleNumbers_ReturnsMaximum()
    {
        var func = new MaxFunction();
        var items = new object[] { 3.0, 1.0, 4.0, 1.0, 5.0 };
        var result = await func.InvokeAsync([items], CreateContext());

        result.Should().Be(5.0);
    }

    [Fact]
    public async Task Max_EmptySequence_ReturnsNull()
    {
        var func = new MaxFunction();
        var items = Array.Empty<object>();
        var result = await func.InvokeAsync([items], CreateContext());

        result.Should().BeNull();
    }

    [Fact]
    public async Task Max_NegativeNumbers_ReturnsLargest()
    {
        var func = new MaxFunction();
        var items = new object[] { -3.0, -1.0, -5.0 };
        var result = await func.InvokeAsync([items], CreateContext());

        result.Should().Be(-1.0);
    }

    [Fact]
    public async Task Max_MixedPositiveAndNegative_ReturnsLargest()
    {
        var func = new MaxFunction();
        var items = new object[] { -10.0, 5.0, 0.0, 3.0 };
        var result = await func.InvokeAsync([items], CreateContext());

        result.Should().Be(5.0);
    }

    [Fact]
    public void Max_Name_IsFnMax()
    {
        var func = new MaxFunction();

        func.Name.LocalName.Should().Be("max");
    }

    #endregion

    #region count Tests

    [Fact]
    public async Task Count_EmptySequence_ReturnsZero()
    {
        var func = new CountFunction();
        var items = Array.Empty<object>();
        var result = await func.InvokeAsync([items], CreateContext());

        result.Should().Be(0);
    }

    [Fact]
    public async Task Count_SingleItem_ReturnsOne()
    {
        var func = new CountFunction();
        var items = new object[] { 42 };
        var result = await func.InvokeAsync([items], CreateContext());

        result.Should().Be(1);
    }

    [Fact]
    public async Task Count_MultipleItems_ReturnsCount()
    {
        var func = new CountFunction();
        var items = new object[] { 1, 2, 3, 4, 5 };
        var result = await func.InvokeAsync([items], CreateContext());

        result.Should().Be(5);
    }

    [Fact]
    public async Task Count_NullArgument_ReturnsZero()
    {
        var func = new CountFunction();
        var result = await func.InvokeAsync([null], CreateContext());

        result.Should().Be(0);
    }

    [Fact]
    public async Task Count_SingleNonSequenceItem_ReturnsOne()
    {
        var func = new CountFunction();
        var result = await func.InvokeAsync(["hello"], CreateContext());

        result.Should().Be(1);
    }

    [Fact]
    public void Count_Name_IsFnCount()
    {
        var func = new CountFunction();

        func.Name.LocalName.Should().Be("count");
    }

    [Fact]
    public void Count_ReturnType_IsInteger()
    {
        var func = new CountFunction();

        func.ReturnType.Should().Be(XdmSequenceType.Integer);
    }

    #endregion

    #region number Tests

    [Fact]
    public async Task Number_ValidNumber_ReturnsDouble()
    {
        var func = new NumberFunction();
        var result = await func.InvokeAsync(["42"], CreateContext());

        result.Should().Be(42.0);
    }

    [Fact]
    public async Task Number_DecimalString_ReturnsDouble()
    {
        var func = new NumberFunction();
        var result = await func.InvokeAsync(["3.14"], CreateContext());

        result.Should().Be(3.14);
    }

    [Fact]
    public async Task Number_InvalidString_ReturnsNaN()
    {
        var func = new NumberFunction();
        var result = await func.InvokeAsync(["hello"], CreateContext());

        result.Should().Be(double.NaN);
    }

    [Fact]
    public async Task Number_NullArgument_ReturnsNaN()
    {
        var func = new NumberFunction();
        var result = await func.InvokeAsync([null], CreateContext());

        result.Should().Be(double.NaN);
    }

    [Fact]
    public async Task Number_EmptyString_ReturnsNaN()
    {
        var func = new NumberFunction();
        var result = await func.InvokeAsync([""], CreateContext());

        result.Should().Be(double.NaN);
    }

    [Fact]
    public async Task Number_NegativeNumber_ReturnsNegativeDouble()
    {
        var func = new NumberFunction();
        var result = await func.InvokeAsync(["-42.5"], CreateContext());

        result.Should().Be(-42.5);
    }

    [Fact]
    public void Number_Name_IsFnNumber()
    {
        var func = new NumberFunction();

        func.Name.LocalName.Should().Be("number");
    }

    [Fact]
    public void Number0_Arity_IsZero()
    {
        var func = new Number0Function();

        func.Arity.Should().Be(0);
    }

    #endregion

    #region FunctionLibrary Registration Tests

    [Fact]
    public void FunctionLibrary_ContainsAbs()
    {
        var lib = FunctionLibrary.Standard;
        var func = lib.Resolve(new QName(FunctionNamespaces.Fn, "abs"), 1);

        func.Should().NotBeNull();
        func.Should().BeOfType<AbsFunction>();
    }

    [Fact]
    public void FunctionLibrary_ContainsCeiling()
    {
        var lib = FunctionLibrary.Standard;
        var func = lib.Resolve(new QName(FunctionNamespaces.Fn, "ceiling"), 1);

        func.Should().NotBeNull();
        func.Should().BeOfType<CeilingFunction>();
    }

    [Fact]
    public void FunctionLibrary_ContainsFloor()
    {
        var lib = FunctionLibrary.Standard;
        var func = lib.Resolve(new QName(FunctionNamespaces.Fn, "floor"), 1);

        func.Should().NotBeNull();
        func.Should().BeOfType<FloorFunction>();
    }

    [Fact]
    public void FunctionLibrary_ContainsRound()
    {
        var lib = FunctionLibrary.Standard;
        var func = lib.Resolve(new QName(FunctionNamespaces.Fn, "round"), 1);

        func.Should().NotBeNull();
        func.Should().BeOfType<RoundFunction>();
    }

    [Fact]
    public void FunctionLibrary_ContainsSum()
    {
        var lib = FunctionLibrary.Standard;
        var func = lib.Resolve(new QName(FunctionNamespaces.Fn, "sum"), 1);

        func.Should().NotBeNull();
        func.Should().BeOfType<SumFunction>();
    }

    [Fact]
    public void FunctionLibrary_ContainsAvg()
    {
        var lib = FunctionLibrary.Standard;
        var func = lib.Resolve(new QName(FunctionNamespaces.Fn, "avg"), 1);

        func.Should().NotBeNull();
        func.Should().BeOfType<AvgFunction>();
    }

    [Fact]
    public void FunctionLibrary_ContainsMin()
    {
        var lib = FunctionLibrary.Standard;
        var func = lib.Resolve(new QName(FunctionNamespaces.Fn, "min"), 1);

        func.Should().NotBeNull();
        func.Should().BeOfType<MinFunction>();
    }

    [Fact]
    public void FunctionLibrary_ContainsMax()
    {
        var lib = FunctionLibrary.Standard;
        var func = lib.Resolve(new QName(FunctionNamespaces.Fn, "max"), 1);

        func.Should().NotBeNull();
        func.Should().BeOfType<MaxFunction>();
    }

    [Fact]
    public void FunctionLibrary_ContainsCount()
    {
        var lib = FunctionLibrary.Standard;
        var func = lib.Resolve(new QName(FunctionNamespaces.Fn, "count"), 1);

        func.Should().NotBeNull();
        func.Should().BeOfType<CountFunction>();
    }

    [Fact]
    public void FunctionLibrary_ContainsNumber()
    {
        var lib = FunctionLibrary.Standard;

        lib.Resolve(new QName(FunctionNamespaces.Fn, "number"), 0).Should().NotBeNull();
        lib.Resolve(new QName(FunctionNamespaces.Fn, "number"), 1).Should().NotBeNull();
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public async Task Sum_WithNullItems_IgnoresNulls()
    {
        var func = new SumFunction();
        var items = new object?[] { 1.0, null, 2.0, null, 3.0 };
        var result = await func.InvokeAsync([items], CreateContext());

        result.Should().Be(6.0);
    }

    [Fact]
    public async Task Min_AllSameValues_ReturnsThatValue()
    {
        var func = new MinFunction();
        var items = new object[] { 5.0, 5.0, 5.0 };
        var result = await func.InvokeAsync([items], CreateContext());

        result.Should().Be(5.0);
    }

    [Fact]
    public async Task Max_AllSameValues_ReturnsThatValue()
    {
        var func = new MaxFunction();
        var items = new object[] { 5.0, 5.0, 5.0 };
        var result = await func.InvokeAsync([items], CreateContext());

        result.Should().Be(5.0);
    }

    [Fact]
    public async Task Ceiling_LargeNumber_HandlesCorrectly()
    {
        var func = new CeilingFunction();
        var result = await func.InvokeAsync([1e10 + 0.5], CreateContext());

        result.Should().Be(Math.Ceiling(1e10 + 0.5));
    }

    [Fact]
    public async Task Floor_LargeNumber_HandlesCorrectly()
    {
        var func = new FloorFunction();
        var result = await func.InvokeAsync([1e10 + 0.5], CreateContext());

        result.Should().Be(Math.Floor(1e10 + 0.5));
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
