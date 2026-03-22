using FluentAssertions;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Parser;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Parser;

/// <summary>
/// Tests that the AST builder produces correct node types and structures.
/// </summary>
[Trait("Category", "Parser")]
public class XQueryAstBuilderTests
{
    private readonly XQueryParserFacade _parser = new();

    // ==================== Constructors ====================

    [Fact]
    public void Parse_ComputedElementConstructor()
    {
        var result = _parser.Parse("element foo { \"content\" }");
        result.Should().BeOfType<ComputedElementConstructor>();
    }

    [Fact]
    public void Parse_ComputedAttributeConstructor()
    {
        var result = _parser.Parse("attribute name { \"value\" }");
        result.Should().BeOfType<ComputedAttributeConstructor>();
    }

    [Fact]
    public void Parse_ComputedTextConstructor()
    {
        var result = _parser.Parse("text { \"hello\" }");
        result.Should().BeOfType<TextConstructor>();
    }

    [Fact]
    public void Parse_ComputedCommentConstructor()
    {
        var result = _parser.Parse("comment { \"a comment\" }");
        result.Should().BeOfType<CommentConstructor>();
    }

    [Fact]
    public void Parse_DocumentConstructor()
    {
        var result = _parser.Parse("document { element root { } }");
        result.Should().BeOfType<DocumentConstructor>();
    }

    // ==================== Direct Element Constructors ====================

    [Fact]
    public void Parse_DirectElementConstructor_SelfClosing()
    {
        var result = _parser.Parse("<br/>");
        var elem = result.Should().BeOfType<ElementConstructor>().Subject;
        elem.Name.LocalName.Should().Be("br");
        elem.Attributes.Should().BeEmpty();
        elem.Content.Should().BeEmpty();
    }

    [Fact]
    public void Parse_DirectElementConstructor_EmptyElement()
    {
        var result = _parser.Parse("<div></div>");
        var elem = result.Should().BeOfType<ElementConstructor>().Subject;
        elem.Name.LocalName.Should().Be("div");
        elem.Content.Should().BeEmpty();
    }

    [Fact]
    public void Parse_DirectElementConstructor_WithTextContent()
    {
        var result = _parser.Parse("<msg>hello world</msg>");
        var elem = result.Should().BeOfType<ElementConstructor>().Subject;
        elem.Name.LocalName.Should().Be("msg");
        elem.Content.Should().HaveCount(1);
        elem.Content[0].Should().BeOfType<StringLiteral>()
            .Which.Value.Should().Be("hello world");
    }

    [Fact]
    public void Parse_DirectElementConstructor_WithEnclosedExpr()
    {
        var result = _parser.Parse("<element>static text {current-dateTime()}</element>");
        var elem = result.Should().BeOfType<ElementConstructor>().Subject;
        elem.Name.LocalName.Should().Be("element");
        elem.Content.Should().HaveCount(2);
        elem.Content[0].Should().BeOfType<StringLiteral>()
            .Which.Value.Should().Be("static text ");
    }

    [Fact]
    public void Parse_DirectElementConstructor_WithAttributes()
    {
        var result = _parser.Parse("<div class=\"main\" id=\"top\">text</div>");
        var elem = result.Should().BeOfType<ElementConstructor>().Subject;
        elem.Name.LocalName.Should().Be("div");
        elem.Attributes.Should().HaveCount(2);
        var attr0 = elem.Attributes[0].Should().BeOfType<AttributeConstructor>().Subject;
        attr0.Name.LocalName.Should().Be("class");
    }

    [Fact]
    public void Parse_DirectElementConstructor_Nested()
    {
        var result = _parser.Parse("<outer><inner>text</inner></outer>");
        var elem = result.Should().BeOfType<ElementConstructor>().Subject;
        elem.Name.LocalName.Should().Be("outer");
        elem.Content.Should().HaveCount(1);
        var inner = elem.Content[0].Should().BeOfType<ElementConstructor>().Subject;
        inner.Name.LocalName.Should().Be("inner");
        inner.Content.Should().HaveCount(1);
    }

    [Fact]
    public void Parse_DirectElementConstructor_MultipleEnclosedExprs()
    {
        var result = _parser.Parse("<r>{1}{2}{3}</r>");
        var elem = result.Should().BeOfType<ElementConstructor>().Subject;
        elem.Content.Should().HaveCount(3);
    }

    // ==================== Operator Precedence ====================

    [Fact]
    public void Parse_PrecedenceMultiplicationBeforeAddition()
    {
        var result = _parser.Parse("1 + 2 * 3");
        // Should parse as 1 + (2 * 3)
        var add = result.Should().BeOfType<BinaryExpression>().Subject;
        add.Operator.Should().Be(BinaryOperator.Add);
        add.Right.Should().BeOfType<BinaryExpression>()
            .Which.Operator.Should().Be(BinaryOperator.Multiply);
    }

    [Fact]
    public void Parse_PrecedenceComparisonBeforeLogical()
    {
        var result = _parser.Parse("$x eq 1 and $y eq 2");
        // Should parse as ($x eq 1) and ($y eq 2)
        var andExpr = result.Should().BeOfType<BinaryExpression>().Subject;
        andExpr.Operator.Should().Be(BinaryOperator.And);
        andExpr.Left.Should().BeOfType<BinaryExpression>()
            .Which.Operator.Should().Be(BinaryOperator.Equal);
    }

    // ==================== Type Expressions ====================

    [Fact]
    public void Parse_TreatAs()
    {
        var result = _parser.Parse("$x treat as xs:integer");
        result.Should().BeOfType<TreatExpression>();
    }

    [Fact]
    public void Parse_CastableAs()
    {
        var result = _parser.Parse("$x castable as xs:integer");
        result.Should().BeOfType<CastableExpression>();
    }

    // ==================== Switch / Typeswitch ====================

    [Fact]
    public void Parse_Switch()
    {
        var result = _parser.Parse("switch ($x) case 1 return \"one\" case 2 return \"two\" default return \"other\"");
        var sw = result.Should().BeOfType<SwitchExpression>().Subject;
        sw.Cases.Should().HaveCount(2);
    }

    [Fact]
    public void Parse_Typeswitch()
    {
        var result = _parser.Parse("typeswitch ($x) case xs:integer return \"int\" case xs:string return \"str\" default return \"other\"");
        var ts = result.Should().BeOfType<TypeswitchExpression>().Subject;
        ts.Cases.Should().HaveCount(2);
    }

    // ==================== Map and Array Operations ====================

    [Fact]
    public void Parse_EmptyMap()
    {
        var result = _parser.Parse("map { }");
        var map = result.Should().BeOfType<MapConstructor>().Subject;
        map.Entries.Should().BeEmpty();
    }

    [Fact]
    public void Parse_MapWithMultipleEntries()
    {
        var result = _parser.Parse("map { \"a\": 1, \"b\": 2 }");
        var map = result.Should().BeOfType<MapConstructor>().Subject;
        map.Entries.Should().HaveCount(2);
    }

    [Fact]
    public void Parse_EmptyArray()
    {
        var result = _parser.Parse("[]");
        var arr = result.Should().BeOfType<ArrayConstructor>().Subject;
        arr.Members.Should().BeEmpty();
    }

    // ==================== Simple Map ====================

    [Fact]
    public void Parse_SimpleMapExpression()
    {
        var result = _parser.Parse("(1, 2, 3) ! (. * 2)");
        result.Should().BeOfType<SimpleMapExpression>();
    }

    // ==================== Complex Expressions ====================

    [Fact]
    public void Parse_NestedFlwor()
    {
        var result = _parser.Parse("for $x in (1, 2) return for $y in (3, 4) return $x + $y");
        var flwor = result.Should().BeOfType<FlworExpression>().Subject;
        flwor.ReturnExpression.Should().BeOfType<FlworExpression>();
    }

    [Fact]
    public void Parse_XQueryComment_IsSkipped()
    {
        var result = _parser.Parse("(: this is a comment :) 42");
        result.Should().BeOfType<IntegerLiteral>()
            .Which.Value.Should().Be(42);
    }
}
