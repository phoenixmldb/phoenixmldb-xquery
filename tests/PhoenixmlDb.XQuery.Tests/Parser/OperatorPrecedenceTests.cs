using AwesomeAssertions;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Parser;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Parser;

/// <summary>
/// The operators from 'or' to 'cast as' are parsed by two left-recursive rules, not a rule per
/// level (#401). These check what the separate rules gave: the precedence of each operator, the
/// expressions the grammar allows only once on an operand, and the one string-concatenation
/// expression of a || b || c.
/// </summary>
public sealed class OperatorPrecedenceTests
{
    [Theory]
    [InlineData("1 + 2 * 3", "7")]
    [InlineData("10 - 4 - 3", "3")]
    [InlineData("2 * 3 idiv 4", "1")]
    [InlineData("1 to 2 + 1", "1 2 3")]
    [InlineData("'a' || 1 + 1", "a2")]
    [InlineData("(1 + 2) instance of xs:integer", "true")]
    [InlineData("1 = 1 and 2 = 3 or 1 = 1", "true")]
    [InlineData("not 1 = 2 and true()", "true")]
    [InlineData("not(1 = 2)", "true")]
    [InlineData("'5' cast as xs:integer castable as xs:string", "true")]
    [InlineData("count((1, 2) union (2, 3) ! ., ())", "error")]
    [InlineData("() otherwise 1 || 2", "12")]
    public async Task Operators_bind_as_before(string query, string expected)
    {
        string result;
        try
        {
            result = await new XQueryFacade().EvaluateAsync($"string-join(({query}) ! string(.), ' ')");
        }
#pragma warning disable CA1031 // a type or static error is an outcome here
        catch (Exception)
#pragma warning restore CA1031
        {
            result = "error";
        }
        result.Should().Be(expected, query);
    }

    [Theory]
    [InlineData("1 = 1 = 1")]
    [InlineData("1 eq 1 lt 2")]
    [InlineData("1 to 2 to 3")]
    [InlineData("'1' cast as xs:integer cast as xs:string")]
    [InlineData("1 castable as xs:integer castable as xs:boolean")]
    [InlineData("1 instance of xs:integer instance of xs:boolean")]
    [InlineData("1 treat as xs:integer treat as xs:integer")]
    public void An_expression_allowed_once_on_an_operand_is_XPST0003_twice(string query)
    {
        var act = () => new XQueryParserFacade().Parse(query);
        act.Should().Throw<XQueryParseException>().Which.ErrorCode.Should().Be("XPST0003");
    }

    [Fact]
    public void Instance_of_binds_tighter_than_addition()
    {
        var add = new XQueryParserFacade().Parse("1 + 2 instance of xs:integer").Should().BeOfType<BinaryExpression>().Which;
        add.Operator.Should().Be(BinaryOperator.Add);
        add.Right.Should().BeOfType<InstanceOfExpression>();
    }

    [Fact]
    public void A_string_concatenation_chain_is_one_expression()
        => new XQueryParserFacade().Parse("'a' || 'b' || 'c'").Should().BeOfType<StringConcatExpression>()
            .Which.Operands.Should().HaveCount(3);
}
