using FluentAssertions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Ast;

/// <summary>
/// Tests for FLWOR expression AST nodes.
/// </summary>
public class FlworExpressionTests
{
    #region FlworExpression Tests

    [Fact]
    public void FlworExpression_WithSingleForClause_StoresCorrectly()
    {
        var forClause = CreateForClause("x", new IntegerLiteral { Value = 1 });
        var flwor = new FlworExpression
        {
            Clauses = [forClause],
            ReturnExpression = new VariableReference { Name = CreateQName("x") }
        };

        flwor.Clauses.Should().HaveCount(1);
        flwor.Clauses[0].Should().BeOfType<ForClause>();
    }

    [Fact]
    public void FlworExpression_WithMultipleClauses_StoresInOrder()
    {
        var forClause = CreateForClause("x", new IntegerLiteral { Value = 1 });
        var letClause = CreateLetClause("y", new IntegerLiteral { Value = 2 });
        var whereClause = new WhereClause { Condition = BooleanLiteral.True };
        var orderClause = new OrderByClause
        {
            OrderSpecs = [new OrderSpec { Expression = new VariableReference { Name = CreateQName("x") } }]
        };

        var flwor = new FlworExpression
        {
            Clauses = [forClause, letClause, whereClause, orderClause],
            ReturnExpression = new VariableReference { Name = CreateQName("x") }
        };

        flwor.Clauses.Should().HaveCount(4);
        flwor.Clauses[0].Should().BeOfType<ForClause>();
        flwor.Clauses[1].Should().BeOfType<LetClause>();
        flwor.Clauses[2].Should().BeOfType<WhereClause>();
        flwor.Clauses[3].Should().BeOfType<OrderByClause>();
    }

    [Fact]
    public void FlworExpression_ReturnExpression_StoresCorrectly()
    {
        var returnExpr = new StringLiteral { Value = "result" };
        var flwor = new FlworExpression
        {
            Clauses = [CreateForClause("x", new IntegerLiteral { Value = 1 })],
            ReturnExpression = returnExpr
        };

        flwor.ReturnExpression.Should().BeSameAs(returnExpr);
    }

    [Fact]
    public void FlworExpression_ToString_IncludesReturn()
    {
        var flwor = new FlworExpression
        {
            Clauses = [CreateForClause("x", new IntegerLiteral { Value = 1 })],
            ReturnExpression = new VariableReference { Name = CreateQName("x") }
        };

        flwor.ToString().Should().Contain("return");
    }

    [Fact]
    public void FlworExpression_Accept_CallsVisitor()
    {
        var flwor = new FlworExpression
        {
            Clauses = [],
            ReturnExpression = new IntegerLiteral { Value = 1 }
        };
        var visitor = new TestVisitor();

        var result = flwor.Accept(visitor);

        result.Should().Be("FlworExpression");
    }

    #endregion

    #region ForClause Tests

    [Fact]
    public void ForClause_SingleBinding_StoresCorrectly()
    {
        var binding = CreateForBinding("x", new IntegerLiteral { Value = 1 });
        var forClause = new ForClause { Bindings = [binding] };

        forClause.Bindings.Should().HaveCount(1);
    }

    [Fact]
    public void ForClause_MultipleBindings_StoresInOrder()
    {
        var binding1 = CreateForBinding("x", new IntegerLiteral { Value = 1 });
        var binding2 = CreateForBinding("y", new IntegerLiteral { Value = 2 });

        var forClause = new ForClause { Bindings = [binding1, binding2] };

        forClause.Bindings.Should().HaveCount(2);
    }

    [Fact]
    public void ForClause_ToString_StartsWithFor()
    {
        var forClause = CreateForClause("x", new IntegerLiteral { Value = 1 });

        forClause.ToString().Should().StartWith("for ");
    }

    #endregion

    #region ForBinding Tests

    [Fact]
    public void ForBinding_Variable_StoresCorrectly()
    {
        var binding = new ForBinding
        {
            Variable = CreateQName("x"),
            Expression = new IntegerLiteral { Value = 1 }
        };

        binding.Variable.LocalName.Should().Be("x");
    }

    [Fact]
    public void ForBinding_Expression_StoresCorrectly()
    {
        var expr = new IntegerLiteral { Value = 42 };
        var binding = new ForBinding
        {
            Variable = CreateQName("x"),
            Expression = expr
        };

        binding.Expression.Should().BeSameAs(expr);
    }

    [Fact]
    public void ForBinding_TypeDeclaration_StoresCorrectly()
    {
        var binding = new ForBinding
        {
            Variable = CreateQName("x"),
            Expression = new IntegerLiteral { Value = 1 },
            TypeDeclaration = XdmSequenceType.Integer
        };

        binding.TypeDeclaration.Should().Be(XdmSequenceType.Integer);
    }

    [Fact]
    public void ForBinding_AllowingEmpty_DefaultsFalse()
    {
        var binding = new ForBinding
        {
            Variable = CreateQName("x"),
            Expression = new IntegerLiteral { Value = 1 }
        };

        binding.AllowingEmpty.Should().BeFalse();
    }

    [Fact]
    public void ForBinding_AllowingEmpty_CanBeSet()
    {
        var binding = new ForBinding
        {
            Variable = CreateQName("x"),
            Expression = new IntegerLiteral { Value = 1 },
            AllowingEmpty = true
        };

        binding.AllowingEmpty.Should().BeTrue();
    }

    [Fact]
    public void ForBinding_PositionalVariable_StoresCorrectly()
    {
        var binding = new ForBinding
        {
            Variable = CreateQName("x"),
            Expression = new IntegerLiteral { Value = 1 },
            PositionalVariable = CreateQName("i")
        };

        binding.PositionalVariable.Should().NotBeNull();
        binding.PositionalVariable!.Value.LocalName.Should().Be("i");
    }

    [Fact]
    public void ForBinding_ToString_IncludesVariable()
    {
        var binding = CreateForBinding("x", new IntegerLiteral { Value = 1 });

        binding.ToString().Should().Contain("$x");
    }

    [Fact]
    public void ForBinding_ToString_WithTypeDeclaration()
    {
        var binding = new ForBinding
        {
            Variable = CreateQName("x"),
            Expression = new IntegerLiteral { Value = 1 },
            TypeDeclaration = XdmSequenceType.Integer
        };

        binding.ToString().Should().Contain("as");
    }

    [Fact]
    public void ForBinding_ToString_WithAllowingEmpty()
    {
        var binding = new ForBinding
        {
            Variable = CreateQName("x"),
            Expression = new IntegerLiteral { Value = 1 },
            AllowingEmpty = true
        };

        binding.ToString().Should().Contain("allowing empty");
    }

    [Fact]
    public void ForBinding_ToString_WithPositionalVariable()
    {
        var binding = new ForBinding
        {
            Variable = CreateQName("x"),
            Expression = new IntegerLiteral { Value = 1 },
            PositionalVariable = CreateQName("i")
        };

        binding.ToString().Should().Contain("at $i");
    }

    #endregion

    #region LetClause Tests

    [Fact]
    public void LetClause_SingleBinding_StoresCorrectly()
    {
        var binding = CreateLetBinding("x", new IntegerLiteral { Value = 1 });
        var letClause = new LetClause { Bindings = [binding] };

        letClause.Bindings.Should().HaveCount(1);
    }

    [Fact]
    public void LetClause_MultipleBindings_StoresInOrder()
    {
        var binding1 = CreateLetBinding("x", new IntegerLiteral { Value = 1 });
        var binding2 = CreateLetBinding("y", new IntegerLiteral { Value = 2 });

        var letClause = new LetClause { Bindings = [binding1, binding2] };

        letClause.Bindings.Should().HaveCount(2);
    }

    [Fact]
    public void LetClause_ToString_StartsWithLet()
    {
        var letClause = CreateLetClause("x", new IntegerLiteral { Value = 1 });

        letClause.ToString().Should().StartWith("let ");
    }

    #endregion

    #region LetBinding Tests

    [Fact]
    public void LetBinding_Variable_StoresCorrectly()
    {
        var binding = new LetBinding
        {
            Variable = CreateQName("x"),
            Expression = new IntegerLiteral { Value = 1 }
        };

        binding.Variable.LocalName.Should().Be("x");
    }

    [Fact]
    public void LetBinding_Expression_StoresCorrectly()
    {
        var expr = new IntegerLiteral { Value = 42 };
        var binding = new LetBinding
        {
            Variable = CreateQName("x"),
            Expression = expr
        };

        binding.Expression.Should().BeSameAs(expr);
    }

    [Fact]
    public void LetBinding_TypeDeclaration_StoresCorrectly()
    {
        var binding = new LetBinding
        {
            Variable = CreateQName("x"),
            Expression = new IntegerLiteral { Value = 1 },
            TypeDeclaration = XdmSequenceType.String
        };

        binding.TypeDeclaration.Should().Be(XdmSequenceType.String);
    }

    [Fact]
    public void LetBinding_ToString_IncludesAssignment()
    {
        var binding = CreateLetBinding("x", new IntegerLiteral { Value = 1 });

        binding.ToString().Should().Contain(":=");
    }

    [Fact]
    public void LetBinding_ToString_WithTypeDeclaration()
    {
        var binding = new LetBinding
        {
            Variable = CreateQName("x"),
            Expression = new IntegerLiteral { Value = 1 },
            TypeDeclaration = XdmSequenceType.Integer
        };

        binding.ToString().Should().Contain("as");
    }

    #endregion

    #region WhereClause Tests

    [Fact]
    public void WhereClause_Condition_StoresCorrectly()
    {
        var condition = BooleanLiteral.True;
        var whereClause = new WhereClause { Condition = condition };

        whereClause.Condition.Should().BeSameAs(condition);
    }

    [Fact]
    public void WhereClause_ToString_StartsWithWhere()
    {
        var whereClause = new WhereClause { Condition = BooleanLiteral.True };

        whereClause.ToString().Should().StartWith("where ");
    }

    #endregion

    #region OrderByClause Tests

    [Fact]
    public void OrderByClause_Stable_DefaultsFalse()
    {
        var orderClause = new OrderByClause
        {
            OrderSpecs = [new OrderSpec { Expression = new IntegerLiteral { Value = 1 } }]
        };

        orderClause.Stable.Should().BeFalse();
    }

    [Fact]
    public void OrderByClause_Stable_CanBeSet()
    {
        var orderClause = new OrderByClause
        {
            Stable = true,
            OrderSpecs = [new OrderSpec { Expression = new IntegerLiteral { Value = 1 } }]
        };

        orderClause.Stable.Should().BeTrue();
    }

    [Fact]
    public void OrderByClause_SingleSpec_StoresCorrectly()
    {
        var spec = new OrderSpec { Expression = new IntegerLiteral { Value = 1 } };
        var orderClause = new OrderByClause { OrderSpecs = [spec] };

        orderClause.OrderSpecs.Should().HaveCount(1);
    }

    [Fact]
    public void OrderByClause_MultipleSpecs_StoresInOrder()
    {
        var spec1 = new OrderSpec { Expression = new IntegerLiteral { Value = 1 } };
        var spec2 = new OrderSpec { Expression = new IntegerLiteral { Value = 2 } };

        var orderClause = new OrderByClause { OrderSpecs = [spec1, spec2] };

        orderClause.OrderSpecs.Should().HaveCount(2);
    }

    [Fact]
    public void OrderByClause_ToString_IncludesOrderBy()
    {
        var orderClause = new OrderByClause
        {
            OrderSpecs = [new OrderSpec { Expression = new IntegerLiteral { Value = 1 } }]
        };

        orderClause.ToString().Should().Contain("order by");
    }

    [Fact]
    public void OrderByClause_ToString_Stable()
    {
        var orderClause = new OrderByClause
        {
            Stable = true,
            OrderSpecs = [new OrderSpec { Expression = new IntegerLiteral { Value = 1 } }]
        };

        orderClause.ToString().Should().StartWith("stable order by");
    }

    #endregion

    #region OrderSpec Tests

    [Fact]
    public void OrderSpec_Expression_StoresCorrectly()
    {
        var expr = new IntegerLiteral { Value = 1 };
        var spec = new OrderSpec { Expression = expr };

        spec.Expression.Should().BeSameAs(expr);
    }

    [Fact]
    public void OrderSpec_Direction_DefaultsAscending()
    {
        var spec = new OrderSpec { Expression = new IntegerLiteral { Value = 1 } };

        spec.Direction.Should().Be(OrderDirection.Ascending);
    }

    [Fact]
    public void OrderSpec_Direction_CanBeDescending()
    {
        var spec = new OrderSpec
        {
            Expression = new IntegerLiteral { Value = 1 },
            Direction = OrderDirection.Descending
        };

        spec.Direction.Should().Be(OrderDirection.Descending);
    }

    [Fact]
    public void OrderSpec_EmptyOrder_DefaultsLeast()
    {
        var spec = new OrderSpec { Expression = new IntegerLiteral { Value = 1 } };

        spec.EmptyOrder.Should().Be(EmptyOrder.Least);
    }

    [Fact]
    public void OrderSpec_EmptyOrder_CanBeGreatest()
    {
        var spec = new OrderSpec
        {
            Expression = new IntegerLiteral { Value = 1 },
            EmptyOrder = EmptyOrder.Greatest
        };

        spec.EmptyOrder.Should().Be(EmptyOrder.Greatest);
    }

    [Fact]
    public void OrderSpec_Collation_StoresCorrectly()
    {
        var spec = new OrderSpec
        {
            Expression = new IntegerLiteral { Value = 1 },
            Collation = "http://example.com/collation"
        };

        spec.Collation.Should().Be("http://example.com/collation");
    }

    [Fact]
    public void OrderSpec_ToString_Descending()
    {
        var spec = new OrderSpec
        {
            Expression = new IntegerLiteral { Value = 1 },
            Direction = OrderDirection.Descending
        };

        spec.ToString().Should().Contain("descending");
    }

    [Fact]
    public void OrderSpec_ToString_EmptyGreatest()
    {
        var spec = new OrderSpec
        {
            Expression = new IntegerLiteral { Value = 1 },
            EmptyOrder = EmptyOrder.Greatest
        };

        spec.ToString().Should().Contain("empty greatest");
    }

    [Fact]
    public void OrderSpec_ToString_WithCollation()
    {
        var spec = new OrderSpec
        {
            Expression = new IntegerLiteral { Value = 1 },
            Collation = "http://example.com/collation"
        };

        spec.ToString().Should().Contain("collation");
    }

    #endregion

    #region GroupByClause Tests

    [Fact]
    public void GroupByClause_SingleSpec_StoresCorrectly()
    {
        var spec = new GroupingSpec { Variable = CreateQName("x") };
        var groupClause = new GroupByClause { GroupingSpecs = [spec] };

        groupClause.GroupingSpecs.Should().HaveCount(1);
    }

    [Fact]
    public void GroupByClause_MultipleSpecs_StoresInOrder()
    {
        var spec1 = new GroupingSpec { Variable = CreateQName("x") };
        var spec2 = new GroupingSpec { Variable = CreateQName("y") };

        var groupClause = new GroupByClause { GroupingSpecs = [spec1, spec2] };

        groupClause.GroupingSpecs.Should().HaveCount(2);
    }

    [Fact]
    public void GroupByClause_ToString_StartsWithGroupBy()
    {
        var groupClause = new GroupByClause
        {
            GroupingSpecs = [new GroupingSpec { Variable = CreateQName("x") }]
        };

        groupClause.ToString().Should().StartWith("group by");
    }

    #endregion

    #region GroupingSpec Tests

    [Fact]
    public void GroupingSpec_Variable_StoresCorrectly()
    {
        var spec = new GroupingSpec { Variable = CreateQName("x") };

        spec.Variable.LocalName.Should().Be("x");
    }

    [Fact]
    public void GroupingSpec_Expression_StoresCorrectly()
    {
        var expr = new IntegerLiteral { Value = 1 };
        var spec = new GroupingSpec
        {
            Variable = CreateQName("x"),
            Expression = expr
        };

        spec.Expression.Should().BeSameAs(expr);
    }

    [Fact]
    public void GroupingSpec_Collation_StoresCorrectly()
    {
        var spec = new GroupingSpec
        {
            Variable = CreateQName("x"),
            Collation = "http://example.com/collation"
        };

        spec.Collation.Should().Be("http://example.com/collation");
    }

    [Fact]
    public void GroupingSpec_ToString_IncludesVariable()
    {
        var spec = new GroupingSpec { Variable = CreateQName("x") };

        spec.ToString().Should().Contain("$x");
    }

    [Fact]
    public void GroupingSpec_ToString_WithExpression()
    {
        var spec = new GroupingSpec
        {
            Variable = CreateQName("x"),
            Expression = new IntegerLiteral { Value = 1 }
        };

        spec.ToString().Should().Contain(":=");
    }

    [Fact]
    public void GroupingSpec_ToString_WithCollation()
    {
        var spec = new GroupingSpec
        {
            Variable = CreateQName("x"),
            Collation = "http://example.com/collation"
        };

        spec.ToString().Should().Contain("collation");
    }

    #endregion

    #region CountClause Tests

    [Fact]
    public void CountClause_Variable_StoresCorrectly()
    {
        var countClause = new CountClause { Variable = CreateQName("n") };

        countClause.Variable.LocalName.Should().Be("n");
    }

    [Fact]
    public void CountClause_ToString_StartsWithCount()
    {
        var countClause = new CountClause { Variable = CreateQName("n") };

        countClause.ToString().Should().StartWith("count");
    }

    [Fact]
    public void CountClause_ToString_IncludesVariable()
    {
        var countClause = new CountClause { Variable = CreateQName("n") };

        countClause.ToString().Should().Contain("$n");
    }

    #endregion

    #region WindowClause Tests

    [Fact]
    public void WindowClause_TumblingWindow_StoresCorrectly()
    {
        var windowClause = new WindowClause
        {
            Kind = WindowKind.Tumbling,
            Variable = CreateQName("w"),
            Expression = new IntegerLiteral { Value = 1 },
            Start = new WindowCondition { When = BooleanLiteral.True }
        };

        windowClause.Kind.Should().Be(WindowKind.Tumbling);
    }

    [Fact]
    public void WindowClause_SlidingWindow_StoresCorrectly()
    {
        var windowClause = new WindowClause
        {
            Kind = WindowKind.Sliding,
            Variable = CreateQName("w"),
            Expression = new IntegerLiteral { Value = 1 },
            Start = new WindowCondition { When = BooleanLiteral.True }
        };

        windowClause.Kind.Should().Be(WindowKind.Sliding);
    }

    [Fact]
    public void WindowClause_Variable_StoresCorrectly()
    {
        var windowClause = new WindowClause
        {
            Kind = WindowKind.Tumbling,
            Variable = CreateQName("window"),
            Expression = new IntegerLiteral { Value = 1 },
            Start = new WindowCondition { When = BooleanLiteral.True }
        };

        windowClause.Variable.LocalName.Should().Be("window");
    }

    [Fact]
    public void WindowClause_TypeDeclaration_StoresCorrectly()
    {
        var windowClause = new WindowClause
        {
            Kind = WindowKind.Tumbling,
            Variable = CreateQName("w"),
            TypeDeclaration = XdmSequenceType.ZeroOrMoreItems,
            Expression = new IntegerLiteral { Value = 1 },
            Start = new WindowCondition { When = BooleanLiteral.True }
        };

        windowClause.TypeDeclaration.Should().Be(XdmSequenceType.ZeroOrMoreItems);
    }

    [Fact]
    public void WindowClause_Expression_StoresCorrectly()
    {
        var expr = new IntegerLiteral { Value = 42 };
        var windowClause = new WindowClause
        {
            Kind = WindowKind.Tumbling,
            Variable = CreateQName("w"),
            Expression = expr,
            Start = new WindowCondition { When = BooleanLiteral.True }
        };

        windowClause.Expression.Should().BeSameAs(expr);
    }

    [Fact]
    public void WindowClause_StartCondition_StoresCorrectly()
    {
        var start = new WindowCondition { When = BooleanLiteral.True };
        var windowClause = new WindowClause
        {
            Kind = WindowKind.Tumbling,
            Variable = CreateQName("w"),
            Expression = new IntegerLiteral { Value = 1 },
            Start = start
        };

        windowClause.Start.Should().BeSameAs(start);
    }

    [Fact]
    public void WindowClause_EndCondition_StoresCorrectly()
    {
        var end = new WindowCondition { When = BooleanLiteral.False };
        var windowClause = new WindowClause
        {
            Kind = WindowKind.Sliding,
            Variable = CreateQName("w"),
            Expression = new IntegerLiteral { Value = 1 },
            Start = new WindowCondition { When = BooleanLiteral.True },
            End = end
        };

        windowClause.End.Should().BeSameAs(end);
    }

    [Fact]
    public void WindowClause_ToString_TumblingWindow()
    {
        var windowClause = new WindowClause
        {
            Kind = WindowKind.Tumbling,
            Variable = CreateQName("w"),
            Expression = new IntegerLiteral { Value = 1 },
            Start = new WindowCondition { When = BooleanLiteral.True }
        };

        windowClause.ToString().Should().Contain("tumbling window");
    }

    [Fact]
    public void WindowClause_ToString_SlidingWindow()
    {
        var windowClause = new WindowClause
        {
            Kind = WindowKind.Sliding,
            Variable = CreateQName("w"),
            Expression = new IntegerLiteral { Value = 1 },
            Start = new WindowCondition { When = BooleanLiteral.True }
        };

        windowClause.ToString().Should().Contain("sliding window");
    }

    #endregion

    #region WindowCondition Tests

    [Fact]
    public void WindowCondition_When_StoresCorrectly()
    {
        var whenExpr = BooleanLiteral.True;
        var condition = new WindowCondition { When = whenExpr };

        condition.When.Should().BeSameAs(whenExpr);
    }

    [Fact]
    public void WindowCondition_CurrentItem_StoresCorrectly()
    {
        var condition = new WindowCondition
        {
            CurrentItem = CreateQName("current"),
            When = BooleanLiteral.True
        };

        condition.CurrentItem.Should().NotBeNull();
        condition.CurrentItem!.Value.LocalName.Should().Be("current");
    }

    [Fact]
    public void WindowCondition_PreviousItem_StoresCorrectly()
    {
        var condition = new WindowCondition
        {
            PreviousItem = CreateQName("prev"),
            When = BooleanLiteral.True
        };

        condition.PreviousItem.Should().NotBeNull();
        condition.PreviousItem!.Value.LocalName.Should().Be("prev");
    }

    [Fact]
    public void WindowCondition_NextItem_StoresCorrectly()
    {
        var condition = new WindowCondition
        {
            NextItem = CreateQName("next"),
            When = BooleanLiteral.True
        };

        condition.NextItem.Should().NotBeNull();
        condition.NextItem!.Value.LocalName.Should().Be("next");
    }

    [Fact]
    public void WindowCondition_Position_StoresCorrectly()
    {
        var condition = new WindowCondition
        {
            Position = CreateQName("pos"),
            When = BooleanLiteral.True
        };

        condition.Position.Should().NotBeNull();
        condition.Position!.Value.LocalName.Should().Be("pos");
    }

    #endregion

    #region OrderDirection Enum Tests

    [Fact]
    public void OrderDirection_Ascending_HasValue0()
    {
        ((int)OrderDirection.Ascending).Should().Be(0);
    }

    [Fact]
    public void OrderDirection_Descending_HasValue1()
    {
        ((int)OrderDirection.Descending).Should().Be(1);
    }

    #endregion

    #region EmptyOrder Enum Tests

    [Fact]
    public void EmptyOrder_Least_HasValue0()
    {
        ((int)EmptyOrder.Least).Should().Be(0);
    }

    [Fact]
    public void EmptyOrder_Greatest_HasValue1()
    {
        ((int)EmptyOrder.Greatest).Should().Be(1);
    }

    #endregion

    #region WindowKind Enum Tests

    [Fact]
    public void WindowKind_Tumbling_HasValue0()
    {
        ((int)WindowKind.Tumbling).Should().Be(0);
    }

    [Fact]
    public void WindowKind_Sliding_HasValue1()
    {
        ((int)WindowKind.Sliding).Should().Be(1);
    }

    #endregion

    #region FlworClause Inheritance Tests

    [Fact]
    public void ForClause_DerivesFromFlworClause()
    {
        var forClause = CreateForClause("x", new IntegerLiteral { Value = 1 });
        forClause.Should().BeAssignableTo<FlworClause>();
    }

    [Fact]
    public void LetClause_DerivesFromFlworClause()
    {
        var letClause = CreateLetClause("x", new IntegerLiteral { Value = 1 });
        letClause.Should().BeAssignableTo<FlworClause>();
    }

    [Fact]
    public void WhereClause_DerivesFromFlworClause()
    {
        var whereClause = new WhereClause { Condition = BooleanLiteral.True };
        whereClause.Should().BeAssignableTo<FlworClause>();
    }

    [Fact]
    public void OrderByClause_DerivesFromFlworClause()
    {
        var orderClause = new OrderByClause
        {
            OrderSpecs = [new OrderSpec { Expression = new IntegerLiteral { Value = 1 } }]
        };
        orderClause.Should().BeAssignableTo<FlworClause>();
    }

    [Fact]
    public void GroupByClause_DerivesFromFlworClause()
    {
        var groupClause = new GroupByClause
        {
            GroupingSpecs = [new GroupingSpec { Variable = CreateQName("x") }]
        };
        groupClause.Should().BeAssignableTo<FlworClause>();
    }

    [Fact]
    public void CountClause_DerivesFromFlworClause()
    {
        var countClause = new CountClause { Variable = CreateQName("n") };
        countClause.Should().BeAssignableTo<FlworClause>();
    }

    [Fact]
    public void WindowClause_DerivesFromFlworClause()
    {
        var windowClause = new WindowClause
        {
            Kind = WindowKind.Tumbling,
            Variable = CreateQName("w"),
            Expression = new IntegerLiteral { Value = 1 },
            Start = new WindowCondition { When = BooleanLiteral.True }
        };
        windowClause.Should().BeAssignableTo<FlworClause>();
    }

    #endregion

    #region Helper Methods

    private static QName CreateQName(string localName)
    {
        return new QName(NamespaceId.None, localName);
    }

    private static ForClause CreateForClause(string varName, XQueryExpression expr)
    {
        return new ForClause
        {
            Bindings = [CreateForBinding(varName, expr)]
        };
    }

    private static ForBinding CreateForBinding(string varName, XQueryExpression expr)
    {
        return new ForBinding
        {
            Variable = CreateQName(varName),
            Expression = expr
        };
    }

    private static LetClause CreateLetClause(string varName, XQueryExpression expr)
    {
        return new LetClause
        {
            Bindings = [CreateLetBinding(varName, expr)]
        };
    }

    private static LetBinding CreateLetBinding(string varName, XQueryExpression expr)
    {
        return new LetBinding
        {
            Variable = CreateQName(varName),
            Expression = expr
        };
    }

    #endregion

    /// <summary>
    /// Test visitor implementation for verifying Accept calls.
    /// </summary>
    private class TestVisitor : XQueryExpressionVisitor<string>
    {
        public override string VisitFlworExpression(FlworExpression expr) => "FlworExpression";
    }
}
