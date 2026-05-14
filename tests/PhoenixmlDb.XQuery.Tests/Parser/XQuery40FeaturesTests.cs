using FluentAssertions;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Parser;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Parser;

/// <summary>
/// Tests for XQuery 4.0-specific features: otherwise, thin arrow, while, braced-if.
/// </summary>
[Trait("Category", "Parser")]
public class XQuery40FeaturesTests
{
    private readonly XQueryParserFacade _parser = new();

    // ==================== Otherwise ====================

    [Fact]
    public void Parse_Otherwise_SimpleExpression()
    {
        var result = _parser.Parse("$items otherwise \"default\"");
        var bin = result.Should().BeOfType<BinaryExpression>().Subject;
        bin.Operator.Should().Be(BinaryOperator.Otherwise);
        bin.Left.Should().BeOfType<VariableReference>();
        bin.Right.Should().BeOfType<StringLiteral>();
    }

    [Fact]
    public void Parse_Otherwise_Chained()
    {
        var result = _parser.Parse("$a otherwise $b otherwise $c");
        var outer = result.Should().BeOfType<BinaryExpression>().Subject;
        outer.Operator.Should().Be(BinaryOperator.Otherwise);
        // Left-associative: ($a otherwise $b) otherwise $c
        outer.Left.Should().BeOfType<BinaryExpression>()
            .Which.Operator.Should().Be(BinaryOperator.Otherwise);
    }

    // ==================== Braced If ====================

    [Fact]
    public void Parse_BracedIf_NoElse()
    {
        var result = _parser.Parse("if ($x > 0) { $x }");
        var ifExpr = result.Should().BeOfType<IfExpression>().Subject;
        ifExpr.Condition.Should().NotBeNull();
        ifExpr.Then.Should().NotBeNull();
        ifExpr.Else.Should().BeNull();
    }

    [Fact]
    public void Parse_BracedIf_WithBody()
    {
        var result = _parser.Parse("if ($x > 0) { $x * 2 }");
        var ifExpr = result.Should().BeOfType<IfExpression>().Subject;
        ifExpr.Else.Should().BeNull();
        ifExpr.Then.Should().BeOfType<BinaryExpression>();
    }

    // ==================== While Clause ====================

    [Fact]
    public void Parse_WhileClause()
    {
        var result = _parser.Parse("for $x in 1 to 10 while ($x < 5) return $x");
        var flwor = result.Should().BeOfType<FlworExpression>().Subject;
        flwor.Clauses.Should().HaveCount(2);
        flwor.Clauses[0].Should().BeOfType<ForClause>();
        flwor.Clauses[1].Should().BeOfType<WhileClause>();

        var whileClause = (WhileClause)flwor.Clauses[1];
        whileClause.Condition.Should().BeOfType<BinaryExpression>();
    }

    // ==================== Arrow Expressions ====================

    [Fact]
    public void Parse_FatArrow()
    {
        var result = _parser.Parse("$data => fn:sort()");
        var arrow = result.Should().BeOfType<ArrowExpression>().Subject;
        arrow.Expression.Should().BeOfType<VariableReference>();
    }

    [Fact]
    public void Parse_ChainedFatArrows()
    {
        var result = _parser.Parse("$data => fn:sort() => fn:reverse()");
        var outer = result.Should().BeOfType<ArrowExpression>().Subject;
        outer.Expression.Should().BeOfType<ArrowExpression>();
    }

    // ==================== Not Expression ====================

    // XQuery 4.0 NotExpression — `not <expr>` parses as UnaryExpression(Not, expr).
    // The semantic predicate in XQueryParser.g4's `notExpr` rule disambiguates from
    // the fn:not() function call: `not(...)` keeps function-call shape (heavily used
    // in QT3 and required for higher-order references like not#1), while `not <expr>`
    // (no opening paren) parses as the 4.0 operator.
    [Fact]
    public void Parse_NotExpression()
    {
        var result = _parser.Parse("not $x eq 1");
        var unary = result.Should().BeOfType<UnaryExpression>().Subject;
        unary.Operator.Should().Be(UnaryOperator.Not);
    }

    [Fact]
    public void Parse_NotFunctionCall_StillParsesAsFunctionCall()
    {
        // `not($x)` must remain a function call so QT3 and higher-order references
        // (not#1) keep working — the semantic predicate prevents NotExpression here.
        var result = _parser.Parse("not($x eq 1)");
        result.Should().BeOfType<FunctionCallExpression>();
    }

    // ==================== Combination ====================

    [Fact]
    public void Parse_OtherwiseWithComparison()
    {
        var result = _parser.Parse("($a otherwise $b) = $c");
        var cmp = result.Should().BeOfType<BinaryExpression>().Subject;
        cmp.Operator.Should().Be(BinaryOperator.GeneralEqual);
    }
}
