using FluentAssertions;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Parser;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Parser;

/// <summary>
/// End-to-end tests for XQueryParserFacade: string → AST.
/// </summary>
[Trait("Category", "Parser")]
public class XQueryParserFacadeTests
{
    private readonly XQueryParserFacade _parser = new();

    // ==================== Literals ====================

    [Fact]
    public void Parse_IntegerLiteral()
    {
        var result = _parser.Parse("42");
        result.Should().BeOfType<IntegerLiteral>()
            .Which.Value.Should().Be(42);
    }

    [Fact]
    public void Parse_DecimalLiteral()
    {
        var result = _parser.Parse("3.14");
        result.Should().BeOfType<DecimalLiteral>()
            .Which.Value.Should().Be(3.14m);
    }

    [Fact]
    public void Parse_DoubleLiteral()
    {
        var result = _parser.Parse("1.5e10");
        result.Should().BeOfType<DoubleLiteral>()
            .Which.Value.Should().Be(1.5e10);
    }

    [Fact]
    public void Parse_StringLiteral_DoubleQuotes()
    {
        var result = _parser.Parse("\"hello\"");
        result.Should().BeOfType<StringLiteral>()
            .Which.Value.Should().Be("hello");
    }

    [Fact]
    public void Parse_StringLiteral_SingleQuotes()
    {
        var result = _parser.Parse("'world'");
        result.Should().BeOfType<StringLiteral>()
            .Which.Value.Should().Be("world");
    }

    [Fact]
    public void Parse_StringLiteral_EscapedQuotes()
    {
        var result = _parser.Parse("\"he said \"\"hi\"\"\"");
        result.Should().BeOfType<StringLiteral>()
            .Which.Value.Should().Be("he said \"hi\"");
    }

    [Fact]
    public void Parse_StringLiteral_RawAmpersand_XPathMode()
    {
        // XPath (XSLT context) string literals allow a raw '&' — the '&amp;' was already
        // decoded by the XML parser before the expression reached us. Regex conformance
        // test regex-070 mode j uses the literal string 'Special characters$&'.
        var parser = new XQueryParserFacade { AllowRawAmpersand = true };
        var result = parser.Parse("'Special characters$&'");
        result.Should().BeOfType<StringLiteral>()
            .Which.Value.Should().Be("Special characters$&");
    }

    [Fact]
    public void Parse_StringLiteral_RawAmpersand_MidString_XPathMode()
    {
        var parser = new XQueryParserFacade { AllowRawAmpersand = true };
        var result = parser.Parse("'AT&T'");
        result.Should().BeOfType<StringLiteral>()
            .Which.Value.Should().Be("AT&T");
    }

    [Fact]
    public void Parse_StringLiteral_RawAmpersand_DoubleQuoted_XPathMode()
    {
        var parser = new XQueryParserFacade { AllowRawAmpersand = true };
        var result = parser.Parse("\"a & b\"");
        result.Should().BeOfType<StringLiteral>()
            .Which.Value.Should().Be("a & b");
    }

    [Fact]
    public void Parse_StringLiteral_RawAmpersand_RejectedInStrictXQueryMode()
    {
        // Default (strict XQuery) still rejects a raw '&' in a string literal — it must be
        // a predefined entity reference or character reference.
        Action act = () => _parser.Parse("'AT&T'");
        act.Should().Throw<XQueryParseException>();
    }

    [Fact]
    public void Parse_EmptySequence()
    {
        var result = _parser.Parse("()");
        result.Should().BeOfType<EmptySequence>();
    }

    [Fact]
    public void Parse_ContextItem()
    {
        var result = _parser.Parse(".");
        result.Should().BeOfType<ContextItemExpression>();
    }

    // ==================== Variables ====================

    [Fact]
    public void Parse_VariableReference()
    {
        var result = _parser.Parse("$x");
        result.Should().BeOfType<VariableReference>()
            .Which.Name.LocalName.Should().Be("x");
    }

    [Fact]
    public void Parse_PrefixedVariableReference()
    {
        var result = _parser.Parse("$ns:var");
        var varRef = result.Should().BeOfType<VariableReference>().Subject;
        varRef.Name.LocalName.Should().Be("var");
        varRef.Name.Prefix.Should().Be("ns");
    }

    // ==================== Arithmetic ====================

    [Fact]
    public void Parse_Addition()
    {
        var result = _parser.Parse("1 + 2");
        var bin = result.Should().BeOfType<BinaryExpression>().Subject;
        bin.Operator.Should().Be(BinaryOperator.Add);
        bin.Left.Should().BeOfType<IntegerLiteral>().Which.Value.Should().Be(1);
        bin.Right.Should().BeOfType<IntegerLiteral>().Which.Value.Should().Be(2);
    }

    [Fact]
    public void Parse_Subtraction()
    {
        var result = _parser.Parse("5 - 3");
        var bin = result.Should().BeOfType<BinaryExpression>().Subject;
        bin.Operator.Should().Be(BinaryOperator.Subtract);
    }

    [Fact]
    public void Parse_Multiplication()
    {
        var result = _parser.Parse("4 * 3");
        var bin = result.Should().BeOfType<BinaryExpression>().Subject;
        bin.Operator.Should().Be(BinaryOperator.Multiply);
    }

    [Fact]
    public void Parse_Division()
    {
        var result = _parser.Parse("10 div 2");
        var bin = result.Should().BeOfType<BinaryExpression>().Subject;
        bin.Operator.Should().Be(BinaryOperator.Divide);
    }

    [Fact]
    public void Parse_Modulo()
    {
        var result = _parser.Parse("7 mod 3");
        var bin = result.Should().BeOfType<BinaryExpression>().Subject;
        bin.Operator.Should().Be(BinaryOperator.Modulo);
    }

    // ==================== Numeric-literal / keyword adjacency (XPST0003) ====================
    //
    // The XPath/XQuery grammar forbids a numeric literal immediately followed by a name
    // with no intervening whitespace. ANTLR discards whitespace, so `10mod 3` would
    // otherwise lex identically to `10 mod 3`; the lexer adapter rejects the abutting form.

    [Theory]
    [InlineData("10mod 3")]   // K-NumericMod-22
    [InlineData("10div 3")]   // K-NumericDivide-37
    [InlineData("10idiv 3")]  // K-NumericIntegerDivide-43
    [InlineData("1eq 2")]
    [InlineData("1to 5")]
    [InlineData("3.5mod 2")]
    public void Parse_NumericLiteralAbuttingKeyword_RaisesXPST0003(string query)
    {
        var ex = Record.Exception(() => _parser.Parse(query));
        ex.Should().NotBeNull("'{0}' must be a static error per the grammar's whitespace rules", query);
        // Either surfaced as our XQueryException(XPST0003) or wrapped as a parse exception.
        if (ex is PhoenixmlDb.XQuery.Functions.XQueryException xqe)
            xqe.ErrorCode.Should().Be("XPST0003");
    }

    [Theory]
    [InlineData("10 mod 3")]
    [InlineData("10 div 3")]
    [InlineData("10 idiv 3")]
    [InlineData("(10) mod 3")]
    public void Parse_NumericLiteralWithWhitespace_Succeeds(string query)
    {
        var result = _parser.Parse(query);
        result.Should().NotBeNull();
    }

    [Fact]
    public void Parse_UnaryMinus()
    {
        var result = _parser.Parse("-5");
        var unary = result.Should().BeOfType<UnaryExpression>().Subject;
        unary.Operator.Should().Be(UnaryOperator.Minus);
        unary.Operand.Should().BeOfType<IntegerLiteral>().Which.Value.Should().Be(5);
    }

    // ==================== Comparison ====================

    [Fact]
    public void Parse_ValueComparison_eq()
    {
        var result = _parser.Parse("$x eq 1");
        var bin = result.Should().BeOfType<BinaryExpression>().Subject;
        bin.Operator.Should().Be(BinaryOperator.Equal);
    }

    [Fact]
    public void Parse_GeneralComparison_Equals()
    {
        var result = _parser.Parse("$x = 1");
        var bin = result.Should().BeOfType<BinaryExpression>().Subject;
        bin.Operator.Should().Be(BinaryOperator.GeneralEqual);
    }

    [Fact]
    public void Parse_GeneralComparison_NotEquals()
    {
        var result = _parser.Parse("$x != 1");
        var bin = result.Should().BeOfType<BinaryExpression>().Subject;
        bin.Operator.Should().Be(BinaryOperator.GeneralNotEqual);
    }

    // ==================== Logical ====================

    [Fact]
    public void Parse_And()
    {
        var result = _parser.Parse("$x and $y");
        var bin = result.Should().BeOfType<BinaryExpression>().Subject;
        bin.Operator.Should().Be(BinaryOperator.And);
    }

    [Fact]
    public void Parse_Or()
    {
        var result = _parser.Parse("$x or $y");
        var bin = result.Should().BeOfType<BinaryExpression>().Subject;
        bin.Operator.Should().Be(BinaryOperator.Or);
    }

    // ==================== Sequences ====================

    [Fact]
    public void Parse_SequenceExpression()
    {
        var result = _parser.Parse("(1, 2, 3)");
        var seq = result.Should().BeOfType<SequenceExpression>().Subject;
        seq.Items.Should().HaveCount(3);
    }

    [Fact]
    public void Parse_RangeExpression()
    {
        var result = _parser.Parse("1 to 10");
        var range = result.Should().BeOfType<RangeExpression>().Subject;
        range.Start.Should().BeOfType<IntegerLiteral>().Which.Value.Should().Be(1);
        range.End.Should().BeOfType<IntegerLiteral>().Which.Value.Should().Be(10);
    }

    [Fact]
    public void Parse_StringConcat()
    {
        var result = _parser.Parse("\"a\" || \"b\" || \"c\"");
        var concat = result.Should().BeOfType<StringConcatExpression>().Subject;
        concat.Operands.Should().HaveCount(3);
    }

    // ==================== FLWOR ====================

    [Fact]
    public void Parse_SimpleFor()
    {
        var result = _parser.Parse("for $x in (1, 2, 3) return $x * 2");
        var flwor = result.Should().BeOfType<FlworExpression>().Subject;
        flwor.Clauses.Should().HaveCount(1);
        flwor.Clauses[0].Should().BeOfType<ForClause>();

        var forClause = (ForClause)flwor.Clauses[0];
        forClause.Bindings.Should().HaveCount(1);
        forClause.Bindings[0].Variable.LocalName.Should().Be("x");
    }

    [Fact]
    public void Parse_ForWithPositionalVar()
    {
        var result = _parser.Parse("for $x at $i in (1, 2) return $i");
        var flwor = result.Should().BeOfType<FlworExpression>().Subject;
        var forClause = (ForClause)flwor.Clauses[0];
        forClause.Bindings[0].PositionalVariable.Should().NotBeNull();
        forClause.Bindings[0].PositionalVariable!.Value.LocalName.Should().Be("i");
    }

    [Fact]
    public void Parse_Let()
    {
        var result = _parser.Parse("let $x := 42 return $x");
        var flwor = result.Should().BeOfType<FlworExpression>().Subject;
        flwor.Clauses[0].Should().BeOfType<LetClause>();
        var letClause = (LetClause)flwor.Clauses[0];
        letClause.Bindings[0].Variable.LocalName.Should().Be("x");
    }

    [Fact]
    public void Parse_ForLetWhereOrderBy()
    {
        var result = _parser.Parse("for $x in (1, 2, 3) let $y := $x * 2 where $y > 2 order by $y descending return $y");
        var flwor = result.Should().BeOfType<FlworExpression>().Subject;
        flwor.Clauses.Should().HaveCount(4);
        flwor.Clauses[0].Should().BeOfType<ForClause>();
        flwor.Clauses[1].Should().BeOfType<LetClause>();
        flwor.Clauses[2].Should().BeOfType<WhereClause>();
        flwor.Clauses[3].Should().BeOfType<OrderByClause>();
    }

    [Fact]
    public void Parse_GroupBy()
    {
        var result = _parser.Parse("for $x in (1, 2, 1, 2) group by $x return $x");
        var flwor = result.Should().BeOfType<FlworExpression>().Subject;
        flwor.Clauses.Should().Contain(c => c is GroupByClause);
    }

    [Fact]
    public void Parse_Count()
    {
        var result = _parser.Parse("for $x in (1, 2, 3) count $c return $c");
        var flwor = result.Should().BeOfType<FlworExpression>().Subject;
        flwor.Clauses.Should().Contain(c => c is CountClause);
    }

    // ==================== Conditionals ====================

    [Fact]
    public void Parse_IfThenElse()
    {
        var result = _parser.Parse("if ($x > 0) then $x else -$x");
        var ifExpr = result.Should().BeOfType<IfExpression>().Subject;
        ifExpr.Then.Should().NotBeNull();
        ifExpr.Else.Should().NotBeNull();
    }

    [Fact]
    public void Parse_QuantifiedSome()
    {
        var result = _parser.Parse("some $x in (1, 2, 3) satisfies $x > 2");
        var quant = result.Should().BeOfType<QuantifiedExpression>().Subject;
        quant.Quantifier.Should().Be(Quantifier.Some);
        quant.Bindings.Should().HaveCount(1);
    }

    [Fact]
    public void Parse_QuantifiedEvery()
    {
        var result = _parser.Parse("every $x in (1, 2, 3) satisfies $x > 0");
        var quant = result.Should().BeOfType<QuantifiedExpression>().Subject;
        quant.Quantifier.Should().Be(Quantifier.Every);
    }

    [Fact]
    public void Parse_TryCatch()
    {
        var result = _parser.Parse("try { 1 div 0 } catch * { 0 }");
        var tc = result.Should().BeOfType<TryCatchExpression>().Subject;
        tc.CatchClauses.Should().HaveCount(1);
    }

    // ==================== Functions ====================

    [Fact]
    public void Parse_FunctionCall_NoArgs()
    {
        var result = _parser.Parse("fn:true()");
        var call = result.Should().BeOfType<FunctionCallExpression>().Subject;
        call.Name.LocalName.Should().Be("true");
        call.Name.Prefix.Should().Be("fn");
        call.Arguments.Should().BeEmpty();
    }

    [Fact]
    public void Parse_FunctionCall_WithArgs()
    {
        var result = _parser.Parse("fn:substring(\"hello\", 1, 3)");
        var call = result.Should().BeOfType<FunctionCallExpression>().Subject;
        call.Name.LocalName.Should().Be("substring");
        call.Arguments.Should().HaveCount(3);
    }

    [Fact]
    public void Parse_NamedFunctionRef()
    {
        var result = _parser.Parse("fn:name#1");
        var fref = result.Should().BeOfType<NamedFunctionRef>().Subject;
        fref.Name.LocalName.Should().Be("name");
        fref.Arity.Should().Be(1);
    }

    [Fact]
    public void Parse_InlineFunction()
    {
        var result = _parser.Parse("function($x) { $x + 1 }");
        var inline = result.Should().BeOfType<InlineFunctionExpression>().Subject;
        inline.Parameters.Should().HaveCount(1);
        inline.Parameters[0].Name.LocalName.Should().Be("x");
    }

    // ==================== Map / Array ====================

    [Fact]
    public void Parse_MapConstructor()
    {
        var result = _parser.Parse("map { \"key\": \"value\" }");
        var map = result.Should().BeOfType<MapConstructor>().Subject;
        map.Entries.Should().HaveCount(1);
    }

    [Fact]
    public void Parse_SquareArrayConstructor()
    {
        var result = _parser.Parse("[1, 2, 3]");
        var arr = result.Should().BeOfType<ArrayConstructor>().Subject;
        arr.Kind.Should().Be(ArrayConstructorKind.Square);
        arr.Members.Should().HaveCount(3);
    }

    [Fact]
    public void Parse_CurlyArrayConstructor()
    {
        var result = _parser.Parse("array { 1 to 5 }");
        var arr = result.Should().BeOfType<ArrayConstructor>().Subject;
        arr.Kind.Should().Be(ArrayConstructorKind.Curly);
    }

    // ==================== Type Expressions ====================

    [Fact]
    public void Parse_InstanceOf()
    {
        var result = _parser.Parse("$x instance of xs:integer");
        var inst = result.Should().BeOfType<InstanceOfExpression>().Subject;
        inst.TargetType.ItemType.Should().Be(ItemType.Integer);
    }

    [Fact]
    public void Parse_CastAs()
    {
        var result = _parser.Parse("$x cast as xs:string");
        var cast = result.Should().BeOfType<CastExpression>().Subject;
        cast.TargetType.ItemType.Should().Be(ItemType.String);
    }

    // ==================== Constructor-local namespace (XQuery §4.13) ====================

    /// <summary>
    /// K2-DirectConElemNamespace-19..22 regression guard: a bare type name inside an
    /// element constructor that declares xmlns="http://www.w3.org/2001/XMLSchema" must
    /// be accepted. Per XQuery 3.0+ §4.13, the default type namespace tracks the default
    /// element namespace, so the constructor-local xmlns sets the default type namespace
    /// and `integer` expands to xs:integer. Regressed in bb7ecb7 (MapTest-008 fix).
    /// </summary>
    [Fact]
    public void Parse_BareTypeInInstanceOf_InsideConstructorWithXsDefaultNs_Succeeds()
    {
        // xmlns="http://www.w3.org/2001/XMLSchema" sets the default element namespace locally,
        // so `integer` in the enclosed attribute expression resolves to xs:integer.
        var query = """<e a="{1 instance of integer}" xmlns="http://www.w3.org/2001/XMLSchema"/>""";
        Action act = () => _parser.Parse(query);
        act.Should().NotThrow("constructor-local xmlns sets the default element/type namespace to XSD, making bare 'integer' valid");
    }

    [Fact]
    public void Parse_BareTypeInInstanceOf_NoDefaultNs_RaisesXPST0051()
    {
        // Without xmlns="...XMLSchema", bare `integer` has no default type namespace → XPST0051.
        Action act = () => _parser.Parse("1 instance of integer");
        act.Should().Throw<XQueryParseException>("bare type name with no default type namespace must raise XPST0051");
    }

    // ==================== Error Handling ====================

    [Fact]
    public void Parse_InvalidSyntax_ThrowsException()
    {
        Action act = () => _parser.Parse("for for for");
        act.Should().Throw<XQueryParseException>();
    }

    [Fact]
    public void TryParse_InvalidSyntax_ReturnsNull()
    {
        var result = _parser.TryParse("for for for", out var errors);
        result.Should().BeNull();
        errors.Should().NotBeEmpty();
    }

    // ==================== Source Location ====================

    [Fact]
    public void Parse_IncludesSourceLocation()
    {
        var result = _parser.Parse("42");
        result.Location.Should().NotBeNull();
        result.Location!.Line.Should().Be(1);
        result.Location.Column.Should().Be(0);
    }
}
