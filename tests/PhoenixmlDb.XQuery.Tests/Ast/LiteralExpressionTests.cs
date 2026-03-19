using FluentAssertions;
using PhoenixmlDb.XQuery.Ast;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Ast;

/// <summary>
/// Tests for literal expression AST nodes.
/// </summary>
public class LiteralExpressionTests
{
    #region IntegerLiteral Tests

    [Fact]
    public void IntegerLiteral_WithPositiveValue_StoresValueCorrectly()
    {
        var literal = new IntegerLiteral { Value = 42 };

        literal.Value.Should().Be(42);
    }

    [Fact]
    public void IntegerLiteral_WithNegativeValue_StoresValueCorrectly()
    {
        var literal = new IntegerLiteral { Value = -100 };

        literal.Value.Should().Be(-100);
    }

    [Fact]
    public void IntegerLiteral_WithZero_StoresValueCorrectly()
    {
        var literal = new IntegerLiteral { Value = 0 };

        literal.Value.Should().Be(0);
    }

    [Fact]
    public void IntegerLiteral_WithMaxValue_StoresValueCorrectly()
    {
        var literal = new IntegerLiteral { Value = long.MaxValue };

        literal.Value.Should().Be(long.MaxValue);
    }

    [Fact]
    public void IntegerLiteral_WithMinValue_StoresValueCorrectly()
    {
        var literal = new IntegerLiteral { Value = long.MinValue };

        literal.Value.Should().Be(long.MinValue);
    }

    [Fact]
    public void IntegerLiteral_ToString_ReturnsValueAsString()
    {
        var literal = new IntegerLiteral { Value = 42 };

        literal.ToString().Should().Be("42");
    }

    [Fact]
    public void IntegerLiteral_Accept_CallsVisitorMethod()
    {
        var literal = new IntegerLiteral { Value = 42 };
        var visitor = new TestVisitor();

        var result = literal.Accept(visitor);

        result.Should().Be("IntegerLiteral:42");
    }

    [Fact]
    public void IntegerLiteral_StaticType_InitiallyNull()
    {
        var literal = new IntegerLiteral { Value = 42 };

        literal.StaticType.Should().BeNull();
    }

    [Fact]
    public void IntegerLiteral_Location_CanBeSet()
    {
        var location = new SourceLocation(1, 5, 0, 2);
        var literal = new IntegerLiteral { Value = 42, Location = location };

        literal.Location.Should().Be(location);
    }

    #endregion

    #region DecimalLiteral Tests

    [Fact]
    public void DecimalLiteral_WithPositiveValue_StoresValueCorrectly()
    {
        var literal = new DecimalLiteral { Value = 3.14m };

        literal.Value.Should().Be(3.14m);
    }

    [Fact]
    public void DecimalLiteral_WithNegativeValue_StoresValueCorrectly()
    {
        var literal = new DecimalLiteral { Value = -0.5m };

        literal.Value.Should().Be(-0.5m);
    }

    [Fact]
    public void DecimalLiteral_WithZero_StoresValueCorrectly()
    {
        var literal = new DecimalLiteral { Value = 0m };

        literal.Value.Should().Be(0m);
    }

    [Fact]
    public void DecimalLiteral_WithPrecision_MaintainsPrecision()
    {
        var literal = new DecimalLiteral { Value = 1.23456789m };

        literal.Value.Should().Be(1.23456789m);
    }

    [Fact]
    public void DecimalLiteral_ToString_ReturnsValueAsString()
    {
        var literal = new DecimalLiteral { Value = 3.14m };

        literal.ToString().Should().Be("3.14");
    }

    [Fact]
    public void DecimalLiteral_Accept_CallsVisitorMethod()
    {
        var literal = new DecimalLiteral { Value = 3.14m };
        var visitor = new TestVisitor();

        var result = literal.Accept(visitor);

        result.Should().Be("DecimalLiteral:3.14");
    }

    #endregion

    #region DoubleLiteral Tests

    [Fact]
    public void DoubleLiteral_WithPositiveValue_StoresValueCorrectly()
    {
        var literal = new DoubleLiteral { Value = 1.5e10 };

        literal.Value.Should().Be(1.5e10);
    }

    [Fact]
    public void DoubleLiteral_WithNegativeValue_StoresValueCorrectly()
    {
        var literal = new DoubleLiteral { Value = -2.5e-3 };

        literal.Value.Should().Be(-2.5e-3);
    }

    [Fact]
    public void DoubleLiteral_WithZero_StoresValueCorrectly()
    {
        var literal = new DoubleLiteral { Value = 0.0 };

        literal.Value.Should().Be(0.0);
    }

    [Fact]
    public void DoubleLiteral_WithInfinity_StoresValueCorrectly()
    {
        var literal = new DoubleLiteral { Value = double.PositiveInfinity };

        literal.Value.Should().Be(double.PositiveInfinity);
    }

    [Fact]
    public void DoubleLiteral_WithNegativeInfinity_StoresValueCorrectly()
    {
        var literal = new DoubleLiteral { Value = double.NegativeInfinity };

        literal.Value.Should().Be(double.NegativeInfinity);
    }

    [Fact]
    public void DoubleLiteral_WithNaN_StoresValueCorrectly()
    {
        var literal = new DoubleLiteral { Value = double.NaN };

        literal.Value.Should().Be(double.NaN);
    }

    [Fact]
    public void DoubleLiteral_ToString_ReturnsScientificNotation()
    {
        var literal = new DoubleLiteral { Value = 1.5e10 };

        literal.ToString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void DoubleLiteral_Accept_CallsVisitorMethod()
    {
        var literal = new DoubleLiteral { Value = 1.5 };
        var visitor = new TestVisitor();

        var result = literal.Accept(visitor);

        result.Should().Be("DoubleLiteral:1.5");
    }

    #endregion

    #region StringLiteral Tests

    [Fact]
    public void StringLiteral_WithNonEmptyValue_StoresValueCorrectly()
    {
        var literal = new StringLiteral { Value = "hello" };

        literal.Value.Should().Be("hello");
    }

    [Fact]
    public void StringLiteral_WithEmptyString_StoresValueCorrectly()
    {
        var literal = new StringLiteral { Value = "" };

        literal.Value.Should().BeEmpty();
    }

    [Fact]
    public void StringLiteral_WithSpecialCharacters_StoresValueCorrectly()
    {
        var literal = new StringLiteral { Value = "hello\nworld\t!" };

        literal.Value.Should().Be("hello\nworld\t!");
    }

    [Fact]
    public void StringLiteral_WithUnicodeCharacters_StoresValueCorrectly()
    {
        var literal = new StringLiteral { Value = "Hello \u4e16\u754c" };

        literal.Value.Should().Be("Hello \u4e16\u754c");
    }

    [Fact]
    public void StringLiteral_ToString_ReturnsQuotedValue()
    {
        var literal = new StringLiteral { Value = "hello" };

        literal.ToString().Should().Be("\"hello\"");
    }

    [Fact]
    public void StringLiteral_Accept_CallsVisitorMethod()
    {
        var literal = new StringLiteral { Value = "test" };
        var visitor = new TestVisitor();

        var result = literal.Accept(visitor);

        result.Should().Be("StringLiteral:test");
    }

    #endregion

    #region BooleanLiteral Tests

    [Fact]
    public void BooleanLiteral_WithTrue_StoresValueCorrectly()
    {
        var literal = new BooleanLiteral { Value = true };

        literal.Value.Should().BeTrue();
    }

    [Fact]
    public void BooleanLiteral_WithFalse_StoresValueCorrectly()
    {
        var literal = new BooleanLiteral { Value = false };

        literal.Value.Should().BeFalse();
    }

    [Fact]
    public void BooleanLiteral_True_IsSingleton()
    {
        var literal1 = BooleanLiteral.True;
        var literal2 = BooleanLiteral.True;

        literal1.Should().BeSameAs(literal2);
    }

    [Fact]
    public void BooleanLiteral_False_IsSingleton()
    {
        var literal1 = BooleanLiteral.False;
        var literal2 = BooleanLiteral.False;

        literal1.Should().BeSameAs(literal2);
    }

    [Fact]
    public void BooleanLiteral_True_HasCorrectValue()
    {
        BooleanLiteral.True.Value.Should().BeTrue();
    }

    [Fact]
    public void BooleanLiteral_False_HasCorrectValue()
    {
        BooleanLiteral.False.Value.Should().BeFalse();
    }

    [Fact]
    public void BooleanLiteral_ToString_ForTrue_ReturnsTrueFunction()
    {
        var literal = new BooleanLiteral { Value = true };

        literal.ToString().Should().Be("true()");
    }

    [Fact]
    public void BooleanLiteral_ToString_ForFalse_ReturnsFalseFunction()
    {
        var literal = new BooleanLiteral { Value = false };

        literal.ToString().Should().Be("false()");
    }

    [Fact]
    public void BooleanLiteral_Accept_CallsVisitorMethod()
    {
        var literal = new BooleanLiteral { Value = true };
        var visitor = new TestVisitor();

        var result = literal.Accept(visitor);

        result.Should().Be("BooleanLiteral:True");
    }

    #endregion

    #region EmptySequence Tests

    [Fact]
    public void EmptySequence_Instance_IsSingleton()
    {
        var instance1 = EmptySequence.Instance;
        var instance2 = EmptySequence.Instance;

        instance1.Should().BeSameAs(instance2);
    }

    [Fact]
    public void EmptySequence_ToString_ReturnsEmptyParentheses()
    {
        EmptySequence.Instance.ToString().Should().Be("()");
    }

    [Fact]
    public void EmptySequence_Accept_CallsVisitorMethod()
    {
        var visitor = new TestVisitor();

        var result = EmptySequence.Instance.Accept(visitor);

        result.Should().Be("EmptySequence");
    }

    [Fact]
    public void EmptySequence_Location_CanBeAccessed()
    {
        // EmptySequence is a singleton so location may be null
        EmptySequence.Instance.Location.Should().BeNull();
    }

    #endregion

    #region ContextItemExpression Tests

    [Fact]
    public void ContextItemExpression_Instance_IsSingleton()
    {
        var instance1 = ContextItemExpression.Instance;
        var instance2 = ContextItemExpression.Instance;

        instance1.Should().BeSameAs(instance2);
    }

    [Fact]
    public void ContextItemExpression_ToString_ReturnsDot()
    {
        ContextItemExpression.Instance.ToString().Should().Be(".");
    }

    [Fact]
    public void ContextItemExpression_Accept_CallsVisitorMethod()
    {
        var visitor = new TestVisitor();

        var result = ContextItemExpression.Instance.Accept(visitor);

        result.Should().Be("ContextItem");
    }

    [Fact]
    public void ContextItemExpression_Location_CanBeAccessed()
    {
        // ContextItemExpression is a singleton so location may be null
        ContextItemExpression.Instance.Location.Should().BeNull();
    }

    #endregion

    #region SourceLocation Tests

    [Fact]
    public void SourceLocation_Constructor_StoresAllValues()
    {
        var location = new SourceLocation(5, 10, 100, 105);

        location.Line.Should().Be(5);
        location.Column.Should().Be(10);
        location.StartIndex.Should().Be(100);
        location.EndIndex.Should().Be(105);
    }

    [Fact]
    public void SourceLocation_Equality_WorksCorrectly()
    {
        var location1 = new SourceLocation(1, 2, 3, 4);
        var location2 = new SourceLocation(1, 2, 3, 4);
        var location3 = new SourceLocation(1, 2, 3, 5);

        location1.Should().Be(location2);
        location1.Should().NotBe(location3);
    }

    #endregion

    #region LiteralExpression Base Class Tests

    [Fact]
    public void LiteralExpression_AllSubtypes_DeriveFromLiteralExpression()
    {
        new IntegerLiteral { Value = 0 }.Should().BeAssignableTo<LiteralExpression>();
        new DecimalLiteral { Value = 0 }.Should().BeAssignableTo<LiteralExpression>();
        new DoubleLiteral { Value = 0 }.Should().BeAssignableTo<LiteralExpression>();
        new StringLiteral { Value = "" }.Should().BeAssignableTo<LiteralExpression>();
        new BooleanLiteral { Value = false }.Should().BeAssignableTo<LiteralExpression>();
    }

    [Fact]
    public void LiteralExpression_AllSubtypes_DeriveFromXQueryExpression()
    {
        new IntegerLiteral { Value = 0 }.Should().BeAssignableTo<XQueryExpression>();
        new DecimalLiteral { Value = 0 }.Should().BeAssignableTo<XQueryExpression>();
        new DoubleLiteral { Value = 0 }.Should().BeAssignableTo<XQueryExpression>();
        new StringLiteral { Value = "" }.Should().BeAssignableTo<XQueryExpression>();
        new BooleanLiteral { Value = false }.Should().BeAssignableTo<XQueryExpression>();
        EmptySequence.Instance.Should().BeAssignableTo<XQueryExpression>();
        ContextItemExpression.Instance.Should().BeAssignableTo<XQueryExpression>();
    }

    #endregion

    #region XdmSequenceType Tests

    [Fact]
    public void XdmSequenceType_Empty_HasCorrectValues()
    {
        var type = XdmSequenceType.Empty;

        type.ItemType.Should().Be(ItemType.Empty);
        type.Occurrence.Should().Be(Occurrence.Zero);
    }

    [Fact]
    public void XdmSequenceType_Item_HasCorrectValues()
    {
        var type = XdmSequenceType.Item;

        type.ItemType.Should().Be(ItemType.Item);
        type.Occurrence.Should().Be(Occurrence.ExactlyOne);
    }

    [Fact]
    public void XdmSequenceType_OptionalItem_HasCorrectValues()
    {
        var type = XdmSequenceType.OptionalItem;

        type.ItemType.Should().Be(ItemType.Item);
        type.Occurrence.Should().Be(Occurrence.ZeroOrOne);
    }

    [Fact]
    public void XdmSequenceType_ZeroOrMoreItems_HasCorrectValues()
    {
        var type = XdmSequenceType.ZeroOrMoreItems;

        type.ItemType.Should().Be(ItemType.Item);
        type.Occurrence.Should().Be(Occurrence.ZeroOrMore);
    }

    [Fact]
    public void XdmSequenceType_OneOrMoreItems_HasCorrectValues()
    {
        var type = XdmSequenceType.OneOrMoreItems;

        type.ItemType.Should().Be(ItemType.Item);
        type.Occurrence.Should().Be(Occurrence.OneOrMore);
    }

    [Fact]
    public void XdmSequenceType_Integer_HasCorrectValues()
    {
        var type = XdmSequenceType.Integer;

        type.ItemType.Should().Be(ItemType.Integer);
        type.Occurrence.Should().Be(Occurrence.ExactlyOne);
    }

    [Fact]
    public void XdmSequenceType_String_HasCorrectValues()
    {
        var type = XdmSequenceType.String;

        type.ItemType.Should().Be(ItemType.String);
        type.Occurrence.Should().Be(Occurrence.ExactlyOne);
    }

    [Fact]
    public void XdmSequenceType_Boolean_HasCorrectValues()
    {
        var type = XdmSequenceType.Boolean;

        type.ItemType.Should().Be(ItemType.Boolean);
        type.Occurrence.Should().Be(Occurrence.ExactlyOne);
    }

    [Fact]
    public void XdmSequenceType_Double_HasCorrectValues()
    {
        var type = XdmSequenceType.Double;

        type.ItemType.Should().Be(ItemType.Double);
        type.Occurrence.Should().Be(Occurrence.ExactlyOne);
    }

    [Fact]
    public void XdmSequenceType_Decimal_HasCorrectValues()
    {
        var type = XdmSequenceType.Decimal;

        type.ItemType.Should().Be(ItemType.Decimal);
        type.Occurrence.Should().Be(Occurrence.ExactlyOne);
    }

    [Fact]
    public void XdmSequenceType_ToString_ExactlyOne_NoIndicator()
    {
        var type = XdmSequenceType.Integer;

        type.ToString().Should().Be("Integer");
    }

    [Fact]
    public void XdmSequenceType_ToString_ZeroOrOne_QuestionMark()
    {
        var type = XdmSequenceType.OptionalString;

        type.ToString().Should().Be("String?");
    }

    [Fact]
    public void XdmSequenceType_ToString_ZeroOrMore_Asterisk()
    {
        var type = XdmSequenceType.ZeroOrMoreItems;

        type.ToString().Should().Be("Item*");
    }

    [Fact]
    public void XdmSequenceType_ToString_OneOrMore_Plus()
    {
        var type = XdmSequenceType.OneOrMoreItems;

        type.ToString().Should().Be("Item+");
    }

    [Fact]
    public void XdmSequenceType_ToString_Empty_ReturnsEmptySequence()
    {
        var type = XdmSequenceType.Empty;

        type.ToString().Should().Be("empty-sequence()");
    }

    #endregion

    /// <summary>
    /// Test visitor implementation for verifying Accept calls.
    /// </summary>
    private class TestVisitor : XQueryExpressionVisitor<string>
    {
        public override string VisitIntegerLiteral(IntegerLiteral expr) => $"IntegerLiteral:{expr.Value}";
        public override string VisitDecimalLiteral(DecimalLiteral expr) => $"DecimalLiteral:{expr.Value}";
        public override string VisitDoubleLiteral(DoubleLiteral expr) => $"DoubleLiteral:{expr.Value}";
        public override string VisitStringLiteral(StringLiteral expr) => $"StringLiteral:{expr.Value}";
        public override string VisitBooleanLiteral(BooleanLiteral expr) => $"BooleanLiteral:{expr.Value}";
        public override string VisitEmptySequence(EmptySequence expr) => "EmptySequence";
        public override string VisitContextItem(ContextItemExpression expr) => "ContextItem";
    }
}
