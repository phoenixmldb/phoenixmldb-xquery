using FluentAssertions;
using PhoenixmlDb.XQuery.Ast;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Ast;

/// <summary>
/// Tests for operator expression AST nodes.
/// </summary>
public class OperatorExpressionTests
{
    #region BinaryExpression Tests

    [Fact]
    public void BinaryExpression_Left_StoresCorrectly()
    {
        var left = new IntegerLiteral { Value = 1 };
        var binary = new BinaryExpression
        {
            Left = left,
            Operator = BinaryOperator.Add,
            Right = new IntegerLiteral { Value = 2 }
        };

        binary.Left.Should().BeSameAs(left);
    }

    [Fact]
    public void BinaryExpression_Right_StoresCorrectly()
    {
        var right = new IntegerLiteral { Value = 2 };
        var binary = new BinaryExpression
        {
            Left = new IntegerLiteral { Value = 1 },
            Operator = BinaryOperator.Add,
            Right = right
        };

        binary.Right.Should().BeSameAs(right);
    }

    [Fact]
    public void BinaryExpression_Operator_StoresCorrectly()
    {
        var binary = new BinaryExpression
        {
            Left = new IntegerLiteral { Value = 1 },
            Operator = BinaryOperator.Multiply,
            Right = new IntegerLiteral { Value = 2 }
        };

        binary.Operator.Should().Be(BinaryOperator.Multiply);
    }

    [Fact]
    public void BinaryExpression_Accept_CallsVisitor()
    {
        var binary = CreateBinaryExpr(BinaryOperator.Add);
        var visitor = new TestVisitor();

        var result = binary.Accept(visitor);

        result.Should().Be("BinaryExpression:Add");
    }

    #region Arithmetic Operators

    [Fact]
    public void BinaryExpression_Add_ToString()
    {
        var binary = CreateBinaryExpr(BinaryOperator.Add);
        binary.ToString().Should().Contain("+");
    }

    [Fact]
    public void BinaryExpression_Subtract_ToString()
    {
        var binary = CreateBinaryExpr(BinaryOperator.Subtract);
        binary.ToString().Should().Contain("-");
    }

    [Fact]
    public void BinaryExpression_Multiply_ToString()
    {
        var binary = CreateBinaryExpr(BinaryOperator.Multiply);
        binary.ToString().Should().Contain("*");
    }

    [Fact]
    public void BinaryExpression_Divide_ToString()
    {
        var binary = CreateBinaryExpr(BinaryOperator.Divide);
        binary.ToString().Should().Contain("div");
    }

    [Fact]
    public void BinaryExpression_IntegerDivide_ToString()
    {
        var binary = CreateBinaryExpr(BinaryOperator.IntegerDivide);
        binary.ToString().Should().Contain("idiv");
    }

    [Fact]
    public void BinaryExpression_Modulo_ToString()
    {
        var binary = CreateBinaryExpr(BinaryOperator.Modulo);
        binary.ToString().Should().Contain("mod");
    }

    #endregion

    #region Value Comparison Operators

    [Fact]
    public void BinaryExpression_Equal_ToString()
    {
        var binary = CreateBinaryExpr(BinaryOperator.Equal);
        binary.ToString().Should().Contain("eq");
    }

    [Fact]
    public void BinaryExpression_NotEqual_ToString()
    {
        var binary = CreateBinaryExpr(BinaryOperator.NotEqual);
        binary.ToString().Should().Contain("ne");
    }

    [Fact]
    public void BinaryExpression_LessThan_ToString()
    {
        var binary = CreateBinaryExpr(BinaryOperator.LessThan);
        binary.ToString().Should().Contain("lt");
    }

    [Fact]
    public void BinaryExpression_LessOrEqual_ToString()
    {
        var binary = CreateBinaryExpr(BinaryOperator.LessOrEqual);
        binary.ToString().Should().Contain("le");
    }

    [Fact]
    public void BinaryExpression_GreaterThan_ToString()
    {
        var binary = CreateBinaryExpr(BinaryOperator.GreaterThan);
        binary.ToString().Should().Contain("gt");
    }

    [Fact]
    public void BinaryExpression_GreaterOrEqual_ToString()
    {
        var binary = CreateBinaryExpr(BinaryOperator.GreaterOrEqual);
        binary.ToString().Should().Contain("ge");
    }

    #endregion

    #region General Comparison Operators

    [Fact]
    public void BinaryExpression_GeneralEqual_ToString()
    {
        var binary = CreateBinaryExpr(BinaryOperator.GeneralEqual);
        binary.ToString().Should().Contain("=");
    }

    [Fact]
    public void BinaryExpression_GeneralNotEqual_ToString()
    {
        var binary = CreateBinaryExpr(BinaryOperator.GeneralNotEqual);
        binary.ToString().Should().Contain("!=");
    }

    [Fact]
    public void BinaryExpression_GeneralLessThan_ToString()
    {
        var binary = CreateBinaryExpr(BinaryOperator.GeneralLessThan);
        binary.ToString().Should().Contain("<");
    }

    [Fact]
    public void BinaryExpression_GeneralLessOrEqual_ToString()
    {
        var binary = CreateBinaryExpr(BinaryOperator.GeneralLessOrEqual);
        binary.ToString().Should().Contain("<=");
    }

    [Fact]
    public void BinaryExpression_GeneralGreaterThan_ToString()
    {
        var binary = CreateBinaryExpr(BinaryOperator.GeneralGreaterThan);
        binary.ToString().Should().Contain(">");
    }

    [Fact]
    public void BinaryExpression_GeneralGreaterOrEqual_ToString()
    {
        var binary = CreateBinaryExpr(BinaryOperator.GeneralGreaterOrEqual);
        binary.ToString().Should().Contain(">=");
    }

    #endregion

    #region Node Comparison Operators

    [Fact]
    public void BinaryExpression_Is_ToString()
    {
        var binary = CreateBinaryExpr(BinaryOperator.Is);
        binary.ToString().Should().Contain("is");
    }

    [Fact]
    public void BinaryExpression_Precedes_ToString()
    {
        var binary = CreateBinaryExpr(BinaryOperator.Precedes);
        binary.ToString().Should().Contain("<<");
    }

    [Fact]
    public void BinaryExpression_Follows_ToString()
    {
        var binary = CreateBinaryExpr(BinaryOperator.Follows);
        binary.ToString().Should().Contain(">>");
    }

    #endregion

    #region Logical Operators

    [Fact]
    public void BinaryExpression_And_ToString()
    {
        var binary = CreateBinaryExpr(BinaryOperator.And);
        binary.ToString().Should().Contain("and");
    }

    [Fact]
    public void BinaryExpression_Or_ToString()
    {
        var binary = CreateBinaryExpr(BinaryOperator.Or);
        binary.ToString().Should().Contain("or");
    }

    #endregion

    #region Sequence Operators

    [Fact]
    public void BinaryExpression_Union_ToString()
    {
        var binary = CreateBinaryExpr(BinaryOperator.Union);
        binary.ToString().Should().Contain("union");
    }

    [Fact]
    public void BinaryExpression_Intersect_ToString()
    {
        var binary = CreateBinaryExpr(BinaryOperator.Intersect);
        binary.ToString().Should().Contain("intersect");
    }

    [Fact]
    public void BinaryExpression_Except_ToString()
    {
        var binary = CreateBinaryExpr(BinaryOperator.Except);
        binary.ToString().Should().Contain("except");
    }

    #endregion

    #region Other Operators

    [Fact]
    public void BinaryExpression_To_ToString()
    {
        var binary = CreateBinaryExpr(BinaryOperator.To);
        binary.ToString().Should().Contain("to");
    }

    [Fact]
    public void BinaryExpression_Concat_ToString()
    {
        var binary = CreateBinaryExpr(BinaryOperator.Concat);
        binary.ToString().Should().Contain("||");
    }

    [Fact]
    public void BinaryExpression_MapLookup_ToString()
    {
        var binary = CreateBinaryExpr(BinaryOperator.MapLookup);
        binary.ToString().Should().Contain("?");
    }

    #endregion

    #endregion

    #region BinaryOperator Enum Tests

    [Fact]
    public void BinaryOperator_HasAllArithmeticOperators()
    {
        Enum.IsDefined(BinaryOperator.Add).Should().BeTrue();
        Enum.IsDefined(BinaryOperator.Subtract).Should().BeTrue();
        Enum.IsDefined(BinaryOperator.Multiply).Should().BeTrue();
        Enum.IsDefined(BinaryOperator.Divide).Should().BeTrue();
        Enum.IsDefined(BinaryOperator.IntegerDivide).Should().BeTrue();
        Enum.IsDefined(BinaryOperator.Modulo).Should().BeTrue();
    }

    [Fact]
    public void BinaryOperator_HasAllValueComparisonOperators()
    {
        Enum.IsDefined(BinaryOperator.Equal).Should().BeTrue();
        Enum.IsDefined(BinaryOperator.NotEqual).Should().BeTrue();
        Enum.IsDefined(BinaryOperator.LessThan).Should().BeTrue();
        Enum.IsDefined(BinaryOperator.LessOrEqual).Should().BeTrue();
        Enum.IsDefined(BinaryOperator.GreaterThan).Should().BeTrue();
        Enum.IsDefined(BinaryOperator.GreaterOrEqual).Should().BeTrue();
    }

    [Fact]
    public void BinaryOperator_HasAllGeneralComparisonOperators()
    {
        Enum.IsDefined(BinaryOperator.GeneralEqual).Should().BeTrue();
        Enum.IsDefined(BinaryOperator.GeneralNotEqual).Should().BeTrue();
        Enum.IsDefined(BinaryOperator.GeneralLessThan).Should().BeTrue();
        Enum.IsDefined(BinaryOperator.GeneralLessOrEqual).Should().BeTrue();
        Enum.IsDefined(BinaryOperator.GeneralGreaterThan).Should().BeTrue();
        Enum.IsDefined(BinaryOperator.GeneralGreaterOrEqual).Should().BeTrue();
    }

    [Fact]
    public void BinaryOperator_HasAllNodeComparisonOperators()
    {
        Enum.IsDefined(BinaryOperator.Is).Should().BeTrue();
        Enum.IsDefined(BinaryOperator.Precedes).Should().BeTrue();
        Enum.IsDefined(BinaryOperator.Follows).Should().BeTrue();
    }

    [Fact]
    public void BinaryOperator_HasAllLogicalOperators()
    {
        Enum.IsDefined(BinaryOperator.And).Should().BeTrue();
        Enum.IsDefined(BinaryOperator.Or).Should().BeTrue();
    }

    [Fact]
    public void BinaryOperator_HasAllSequenceOperators()
    {
        Enum.IsDefined(BinaryOperator.Union).Should().BeTrue();
        Enum.IsDefined(BinaryOperator.Intersect).Should().BeTrue();
        Enum.IsDefined(BinaryOperator.Except).Should().BeTrue();
    }

    [Fact]
    public void BinaryOperator_HasOtherOperators()
    {
        Enum.IsDefined(BinaryOperator.To).Should().BeTrue();
        Enum.IsDefined(BinaryOperator.Concat).Should().BeTrue();
        Enum.IsDefined(BinaryOperator.MapLookup).Should().BeTrue();
    }

    #endregion

    #region UnaryExpression Tests

    [Fact]
    public void UnaryExpression_Operator_StoresCorrectly()
    {
        var unary = new UnaryExpression
        {
            Operator = UnaryOperator.Minus,
            Operand = new IntegerLiteral { Value = 42 }
        };

        unary.Operator.Should().Be(UnaryOperator.Minus);
    }

    [Fact]
    public void UnaryExpression_Operand_StoresCorrectly()
    {
        var operand = new IntegerLiteral { Value = 42 };
        var unary = new UnaryExpression
        {
            Operator = UnaryOperator.Minus,
            Operand = operand
        };

        unary.Operand.Should().BeSameAs(operand);
    }

    [Fact]
    public void UnaryExpression_Plus_ToString()
    {
        var unary = new UnaryExpression
        {
            Operator = UnaryOperator.Plus,
            Operand = new IntegerLiteral { Value = 42 }
        };

        unary.ToString().Should().Contain("+");
    }

    [Fact]
    public void UnaryExpression_Minus_ToString()
    {
        var unary = new UnaryExpression
        {
            Operator = UnaryOperator.Minus,
            Operand = new IntegerLiteral { Value = 42 }
        };

        unary.ToString().Should().Contain("-");
    }

    [Fact]
    public void UnaryExpression_Not_ToString()
    {
        var unary = new UnaryExpression
        {
            Operator = UnaryOperator.Not,
            Operand = BooleanLiteral.True
        };

        unary.ToString().Should().Contain("not");
    }

    [Fact]
    public void UnaryExpression_Accept_CallsVisitor()
    {
        var unary = new UnaryExpression
        {
            Operator = UnaryOperator.Minus,
            Operand = new IntegerLiteral { Value = 1 }
        };
        var visitor = new TestVisitor();

        var result = unary.Accept(visitor);

        result.Should().Be("UnaryExpression:Minus");
    }

    #endregion

    #region UnaryOperator Enum Tests

    [Fact]
    public void UnaryOperator_Plus_IsDefined()
    {
        Enum.IsDefined(UnaryOperator.Plus).Should().BeTrue();
    }

    [Fact]
    public void UnaryOperator_Minus_IsDefined()
    {
        Enum.IsDefined(UnaryOperator.Minus).Should().BeTrue();
    }

    [Fact]
    public void UnaryOperator_Not_IsDefined()
    {
        Enum.IsDefined(UnaryOperator.Not).Should().BeTrue();
    }

    #endregion

    #region SequenceExpression Tests

    [Fact]
    public void SequenceExpression_EmptyItems_StoresCorrectly()
    {
        var sequence = new SequenceExpression { Items = [] };

        sequence.Items.Should().BeEmpty();
    }

    [Fact]
    public void SequenceExpression_SingleItem_StoresCorrectly()
    {
        var item = new IntegerLiteral { Value = 1 };
        var sequence = new SequenceExpression { Items = [item] };

        sequence.Items.Should().HaveCount(1);
        sequence.Items[0].Should().BeSameAs(item);
    }

    [Fact]
    public void SequenceExpression_MultipleItems_StoresInOrder()
    {
        var item1 = new IntegerLiteral { Value = 1 };
        var item2 = new StringLiteral { Value = "two" };
        var item3 = new DecimalLiteral { Value = 3.0m };

        var sequence = new SequenceExpression { Items = [item1, item2, item3] };

        sequence.Items.Should().HaveCount(3);
        sequence.Items[0].Should().BeSameAs(item1);
        sequence.Items[1].Should().BeSameAs(item2);
        sequence.Items[2].Should().BeSameAs(item3);
    }

    [Fact]
    public void SequenceExpression_ToString_ShowsParentheses()
    {
        var sequence = new SequenceExpression
        {
            Items = [new IntegerLiteral { Value = 1 }, new IntegerLiteral { Value = 2 }]
        };

        sequence.ToString().Should().StartWith("(");
        sequence.ToString().Should().EndWith(")");
    }

    [Fact]
    public void SequenceExpression_ToString_SeparatesWithCommas()
    {
        var sequence = new SequenceExpression
        {
            Items = [new IntegerLiteral { Value = 1 }, new IntegerLiteral { Value = 2 }]
        };

        sequence.ToString().Should().Contain(",");
    }

    [Fact]
    public void SequenceExpression_Accept_CallsVisitor()
    {
        var sequence = new SequenceExpression { Items = [] };
        var visitor = new TestVisitor();

        var result = sequence.Accept(visitor);

        result.Should().Be("SequenceExpression");
    }

    #endregion

    #region RangeExpression Tests

    [Fact]
    public void RangeExpression_Start_StoresCorrectly()
    {
        var start = new IntegerLiteral { Value = 1 };
        var range = new RangeExpression
        {
            Start = start,
            End = new IntegerLiteral { Value = 10 }
        };

        range.Start.Should().BeSameAs(start);
    }

    [Fact]
    public void RangeExpression_End_StoresCorrectly()
    {
        var end = new IntegerLiteral { Value = 10 };
        var range = new RangeExpression
        {
            Start = new IntegerLiteral { Value = 1 },
            End = end
        };

        range.End.Should().BeSameAs(end);
    }

    [Fact]
    public void RangeExpression_ToString_ShowsToKeyword()
    {
        var range = new RangeExpression
        {
            Start = new IntegerLiteral { Value = 1 },
            End = new IntegerLiteral { Value = 10 }
        };

        range.ToString().Should().Contain("to");
    }

    [Fact]
    public void RangeExpression_Accept_CallsVisitor()
    {
        var range = new RangeExpression
        {
            Start = new IntegerLiteral { Value = 1 },
            End = new IntegerLiteral { Value = 10 }
        };
        var visitor = new TestVisitor();

        var result = range.Accept(visitor);

        result.Should().Be("RangeExpression");
    }

    #endregion

    #region InstanceOfExpression Tests

    [Fact]
    public void InstanceOfExpression_Expression_StoresCorrectly()
    {
        var expr = new IntegerLiteral { Value = 42 };
        var instanceOf = new InstanceOfExpression
        {
            Expression = expr,
            TargetType = XdmSequenceType.Integer
        };

        instanceOf.Expression.Should().BeSameAs(expr);
    }

    [Fact]
    public void InstanceOfExpression_TargetType_StoresCorrectly()
    {
        var instanceOf = new InstanceOfExpression
        {
            Expression = new IntegerLiteral { Value = 42 },
            TargetType = XdmSequenceType.String
        };

        instanceOf.TargetType.Should().Be(XdmSequenceType.String);
    }

    [Fact]
    public void InstanceOfExpression_ToString_ShowsInstanceOf()
    {
        var instanceOf = new InstanceOfExpression
        {
            Expression = new IntegerLiteral { Value = 42 },
            TargetType = XdmSequenceType.Integer
        };

        instanceOf.ToString().Should().Contain("instance of");
    }

    [Fact]
    public void InstanceOfExpression_Accept_CallsVisitor()
    {
        var instanceOf = new InstanceOfExpression
        {
            Expression = new IntegerLiteral { Value = 42 },
            TargetType = XdmSequenceType.Integer
        };
        var visitor = new TestVisitor();

        var result = instanceOf.Accept(visitor);

        result.Should().Be("InstanceOfExpression");
    }

    #endregion

    #region CastExpression Tests

    [Fact]
    public void CastExpression_Expression_StoresCorrectly()
    {
        var expr = new StringLiteral { Value = "42" };
        var cast = new CastExpression
        {
            Expression = expr,
            TargetType = XdmSequenceType.Integer
        };

        cast.Expression.Should().BeSameAs(expr);
    }

    [Fact]
    public void CastExpression_TargetType_StoresCorrectly()
    {
        var cast = new CastExpression
        {
            Expression = new StringLiteral { Value = "42" },
            TargetType = XdmSequenceType.Integer
        };

        cast.TargetType.Should().Be(XdmSequenceType.Integer);
    }

    [Fact]
    public void CastExpression_ToString_ShowsCastAs()
    {
        var cast = new CastExpression
        {
            Expression = new StringLiteral { Value = "42" },
            TargetType = XdmSequenceType.Integer
        };

        cast.ToString().Should().Contain("cast as");
    }

    [Fact]
    public void CastExpression_Accept_CallsVisitor()
    {
        var cast = new CastExpression
        {
            Expression = new StringLiteral { Value = "42" },
            TargetType = XdmSequenceType.Integer
        };
        var visitor = new TestVisitor();

        var result = cast.Accept(visitor);

        result.Should().Be("CastExpression");
    }

    #endregion

    #region CastableExpression Tests

    [Fact]
    public void CastableExpression_Expression_StoresCorrectly()
    {
        var expr = new StringLiteral { Value = "42" };
        var castable = new CastableExpression
        {
            Expression = expr,
            TargetType = XdmSequenceType.Integer
        };

        castable.Expression.Should().BeSameAs(expr);
    }

    [Fact]
    public void CastableExpression_TargetType_StoresCorrectly()
    {
        var castable = new CastableExpression
        {
            Expression = new StringLiteral { Value = "42" },
            TargetType = XdmSequenceType.Integer
        };

        castable.TargetType.Should().Be(XdmSequenceType.Integer);
    }

    [Fact]
    public void CastableExpression_ToString_ShowsCastableAs()
    {
        var castable = new CastableExpression
        {
            Expression = new StringLiteral { Value = "42" },
            TargetType = XdmSequenceType.Integer
        };

        castable.ToString().Should().Contain("castable as");
    }

    [Fact]
    public void CastableExpression_Accept_CallsVisitor()
    {
        var castable = new CastableExpression
        {
            Expression = new StringLiteral { Value = "42" },
            TargetType = XdmSequenceType.Integer
        };
        var visitor = new TestVisitor();

        var result = castable.Accept(visitor);

        result.Should().Be("CastableExpression");
    }

    #endregion

    #region TreatExpression Tests

    [Fact]
    public void TreatExpression_Expression_StoresCorrectly()
    {
        var expr = new StringLiteral { Value = "test" };
        var treat = new TreatExpression
        {
            Expression = expr,
            TargetType = XdmSequenceType.String
        };

        treat.Expression.Should().BeSameAs(expr);
    }

    [Fact]
    public void TreatExpression_TargetType_StoresCorrectly()
    {
        var treat = new TreatExpression
        {
            Expression = new StringLiteral { Value = "test" },
            TargetType = XdmSequenceType.String
        };

        treat.TargetType.Should().Be(XdmSequenceType.String);
    }

    [Fact]
    public void TreatExpression_ToString_ShowsTreatAs()
    {
        var treat = new TreatExpression
        {
            Expression = new StringLiteral { Value = "test" },
            TargetType = XdmSequenceType.String
        };

        treat.ToString().Should().Contain("treat as");
    }

    [Fact]
    public void TreatExpression_Accept_CallsVisitor()
    {
        var treat = new TreatExpression
        {
            Expression = new StringLiteral { Value = "test" },
            TargetType = XdmSequenceType.String
        };
        var visitor = new TestVisitor();

        var result = treat.Accept(visitor);

        result.Should().Be("TreatExpression");
    }

    #endregion

    #region ArrowExpression Tests

    [Fact]
    public void ArrowExpression_Expression_StoresCorrectly()
    {
        var expr = new StringLiteral { Value = "test" };
        var arrow = new ArrowExpression
        {
            Expression = expr,
            FunctionCall = new StringLiteral { Value = "fn" }
        };

        arrow.Expression.Should().BeSameAs(expr);
    }

    [Fact]
    public void ArrowExpression_FunctionCall_StoresCorrectly()
    {
        var funcCall = new StringLiteral { Value = "fn" };
        var arrow = new ArrowExpression
        {
            Expression = new StringLiteral { Value = "test" },
            FunctionCall = funcCall
        };

        arrow.FunctionCall.Should().BeSameAs(funcCall);
    }

    [Fact]
    public void ArrowExpression_ToString_ShowsArrow()
    {
        var arrow = new ArrowExpression
        {
            Expression = new StringLiteral { Value = "test" },
            FunctionCall = new StringLiteral { Value = "fn" }
        };

        arrow.ToString().Should().Contain("=>");
    }

    [Fact]
    public void ArrowExpression_Accept_CallsVisitor()
    {
        var arrow = new ArrowExpression
        {
            Expression = new StringLiteral { Value = "test" },
            FunctionCall = new StringLiteral { Value = "fn" }
        };
        var visitor = new TestVisitor();

        var result = arrow.Accept(visitor);

        result.Should().Be("ArrowExpression");
    }

    #endregion

    #region SimpleMapExpression Tests

    [Fact]
    public void SimpleMapExpression_Left_StoresCorrectly()
    {
        var left = new IntegerLiteral { Value = 1 };
        var map = new SimpleMapExpression
        {
            Left = left,
            Right = new IntegerLiteral { Value = 2 }
        };

        map.Left.Should().BeSameAs(left);
    }

    [Fact]
    public void SimpleMapExpression_Right_StoresCorrectly()
    {
        var right = new IntegerLiteral { Value = 2 };
        var map = new SimpleMapExpression
        {
            Left = new IntegerLiteral { Value = 1 },
            Right = right
        };

        map.Right.Should().BeSameAs(right);
    }

    [Fact]
    public void SimpleMapExpression_ToString_ShowsBang()
    {
        var map = new SimpleMapExpression
        {
            Left = new IntegerLiteral { Value = 1 },
            Right = new IntegerLiteral { Value = 2 }
        };

        map.ToString().Should().Contain("!");
    }

    [Fact]
    public void SimpleMapExpression_Accept_CallsVisitor()
    {
        var map = new SimpleMapExpression
        {
            Left = new IntegerLiteral { Value = 1 },
            Right = new IntegerLiteral { Value = 2 }
        };
        var visitor = new TestVisitor();

        var result = map.Accept(visitor);

        result.Should().Be("SimpleMapExpression");
    }

    #endregion

    #region StringConcatExpression Tests

    [Fact]
    public void StringConcatExpression_EmptyOperands_StoresCorrectly()
    {
        var concat = new StringConcatExpression { Operands = [] };

        concat.Operands.Should().BeEmpty();
    }

    [Fact]
    public void StringConcatExpression_SingleOperand_StoresCorrectly()
    {
        var operand = new StringLiteral { Value = "test" };
        var concat = new StringConcatExpression { Operands = [operand] };

        concat.Operands.Should().HaveCount(1);
        concat.Operands[0].Should().BeSameAs(operand);
    }

    [Fact]
    public void StringConcatExpression_MultipleOperands_StoresInOrder()
    {
        var op1 = new StringLiteral { Value = "a" };
        var op2 = new StringLiteral { Value = "b" };
        var op3 = new StringLiteral { Value = "c" };

        var concat = new StringConcatExpression { Operands = [op1, op2, op3] };

        concat.Operands.Should().HaveCount(3);
        concat.Operands[0].Should().BeSameAs(op1);
        concat.Operands[1].Should().BeSameAs(op2);
        concat.Operands[2].Should().BeSameAs(op3);
    }

    [Fact]
    public void StringConcatExpression_ToString_ShowsDoublePipe()
    {
        var concat = new StringConcatExpression
        {
            Operands = [new StringLiteral { Value = "a" }, new StringLiteral { Value = "b" }]
        };

        concat.ToString().Should().Contain("||");
    }

    [Fact]
    public void StringConcatExpression_Accept_CallsVisitor()
    {
        var concat = new StringConcatExpression { Operands = [] };
        var visitor = new TestVisitor();

        var result = concat.Accept(visitor);

        result.Should().Be("StringConcatExpression");
    }

    #endregion

    #region Type Expression Base Class Tests

    [Fact]
    public void TypeExpressions_DeriveFromXQueryExpression()
    {
        var instanceOf = new InstanceOfExpression
        {
            Expression = new IntegerLiteral { Value = 1 },
            TargetType = XdmSequenceType.Integer
        };
        var cast = new CastExpression
        {
            Expression = new IntegerLiteral { Value = 1 },
            TargetType = XdmSequenceType.Integer
        };
        var castable = new CastableExpression
        {
            Expression = new IntegerLiteral { Value = 1 },
            TargetType = XdmSequenceType.Integer
        };
        var treat = new TreatExpression
        {
            Expression = new IntegerLiteral { Value = 1 },
            TargetType = XdmSequenceType.Integer
        };

        instanceOf.Should().BeAssignableTo<XQueryExpression>();
        cast.Should().BeAssignableTo<XQueryExpression>();
        castable.Should().BeAssignableTo<XQueryExpression>();
        treat.Should().BeAssignableTo<XQueryExpression>();
    }

    #endregion

    #region Helper Methods

    private static BinaryExpression CreateBinaryExpr(BinaryOperator op)
    {
        return new BinaryExpression
        {
            Left = new IntegerLiteral { Value = 1 },
            Operator = op,
            Right = new IntegerLiteral { Value = 2 }
        };
    }

    #endregion

    /// <summary>
    /// Test visitor implementation for verifying Accept calls.
    /// </summary>
    private class TestVisitor : XQueryExpressionVisitor<string>
    {
        public override string VisitBinaryExpression(BinaryExpression expr) => $"BinaryExpression:{expr.Operator}";
        public override string VisitUnaryExpression(UnaryExpression expr) => $"UnaryExpression:{expr.Operator}";
        public override string VisitSequenceExpression(SequenceExpression expr) => "SequenceExpression";
        public override string VisitRangeExpression(RangeExpression expr) => "RangeExpression";
        public override string VisitInstanceOfExpression(InstanceOfExpression expr) => "InstanceOfExpression";
        public override string VisitCastExpression(CastExpression expr) => "CastExpression";
        public override string VisitCastableExpression(CastableExpression expr) => "CastableExpression";
        public override string VisitTreatExpression(TreatExpression expr) => "TreatExpression";
        public override string VisitArrowExpression(ArrowExpression expr) => "ArrowExpression";
        public override string VisitSimpleMapExpression(SimpleMapExpression expr) => "SimpleMapExpression";
        public override string VisitStringConcatExpression(StringConcatExpression expr) => "StringConcatExpression";
    }
}
