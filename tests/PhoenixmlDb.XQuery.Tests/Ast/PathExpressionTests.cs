using FluentAssertions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Ast;

/// <summary>
/// Tests for path expression AST nodes.
/// </summary>
public class PathExpressionTests
{
    #region PathExpression Tests

    [Fact]
    public void PathExpression_RelativePath_IsAbsoluteIsFalse()
    {
        var path = new PathExpression
        {
            IsAbsolute = false,
            Steps = [CreateSimpleStep("name")]
        };

        path.IsAbsolute.Should().BeFalse();
    }

    [Fact]
    public void PathExpression_AbsolutePath_IsAbsoluteIsTrue()
    {
        var path = new PathExpression
        {
            IsAbsolute = true,
            Steps = [CreateSimpleStep("root")]
        };

        path.IsAbsolute.Should().BeTrue();
    }

    [Fact]
    public void PathExpression_SingleStep_StoresCorrectly()
    {
        var step = CreateSimpleStep("element");
        var path = new PathExpression
        {
            IsAbsolute = false,
            Steps = [step]
        };

        path.Steps.Should().HaveCount(1);
        path.Steps[0].Should().BeSameAs(step);
    }

    [Fact]
    public void PathExpression_MultipleSteps_StoresInOrder()
    {
        var step1 = CreateSimpleStep("parent");
        var step2 = CreateSimpleStep("child");
        var step3 = CreateSimpleStep("grandchild");

        var path = new PathExpression
        {
            IsAbsolute = true,
            Steps = [step1, step2, step3]
        };

        path.Steps.Should().HaveCount(3);
        path.Steps[0].Should().BeSameAs(step1);
        path.Steps[1].Should().BeSameAs(step2);
        path.Steps[2].Should().BeSameAs(step3);
    }

    [Fact]
    public void PathExpression_EmptySteps_IsValid()
    {
        var path = new PathExpression
        {
            IsAbsolute = true,
            Steps = []
        };

        path.Steps.Should().BeEmpty();
    }

    [Fact]
    public void PathExpression_ToString_RelativePath()
    {
        var path = new PathExpression
        {
            IsAbsolute = false,
            Steps = [CreateSimpleStep("child")]
        };

        path.ToString().Should().Be("child");
    }

    [Fact]
    public void PathExpression_ToString_AbsolutePath()
    {
        var path = new PathExpression
        {
            IsAbsolute = true,
            Steps = [CreateSimpleStep("root")]
        };

        path.ToString().Should().Be("/root");
    }

    [Fact]
    public void PathExpression_ToString_MultipleSteps()
    {
        var path = new PathExpression
        {
            IsAbsolute = true,
            Steps = [CreateSimpleStep("root"), CreateSimpleStep("child")]
        };

        path.ToString().Should().Be("/root/child");
    }

    [Fact]
    public void PathExpression_Accept_CallsVisitor()
    {
        var path = new PathExpression
        {
            IsAbsolute = false,
            Steps = []
        };
        var visitor = new TestVisitor();

        var result = path.Accept(visitor);

        result.Should().Be("PathExpression");
    }

    #endregion

    #region Axis Tests

    [Theory]
    [InlineData(Axis.Child)]
    [InlineData(Axis.Descendant)]
    [InlineData(Axis.Attribute)]
    [InlineData(Axis.Self)]
    [InlineData(Axis.DescendantOrSelf)]
    [InlineData(Axis.FollowingSibling)]
    [InlineData(Axis.Following)]
    [InlineData(Axis.Parent)]
    [InlineData(Axis.Ancestor)]
    [InlineData(Axis.PrecedingSibling)]
    [InlineData(Axis.Preceding)]
    [InlineData(Axis.AncestorOrSelf)]
    [InlineData(Axis.Namespace)]
    public void Axis_AllValues_AreDefinedCorrectly(Axis axis)
    {
        Enum.IsDefined(axis).Should().BeTrue();
    }

    [Fact]
    public void Axis_HasAllExpectedValues()
    {
        var values = Enum.GetValues<Axis>();
        values.Should().HaveCount(13);
    }

    [Fact]
    public void Axis_Child_IsDefault()
    {
        var step = new StepExpression
        {
            Axis = default,
            NodeTest = new NameTest { LocalName = "test" },
            Predicates = []
        };

        step.Axis.Should().Be(Axis.Child);
    }

    #endregion

    #region StepExpression Tests

    [Fact]
    public void StepExpression_WithChildAxis_StoresCorrectly()
    {
        var step = new StepExpression
        {
            Axis = Axis.Child,
            NodeTest = new NameTest { LocalName = "element" },
            Predicates = []
        };

        step.Axis.Should().Be(Axis.Child);
    }

    [Fact]
    public void StepExpression_WithDescendantAxis_StoresCorrectly()
    {
        var step = new StepExpression
        {
            Axis = Axis.Descendant,
            NodeTest = new NameTest { LocalName = "element" },
            Predicates = []
        };

        step.Axis.Should().Be(Axis.Descendant);
    }

    [Fact]
    public void StepExpression_WithAttributeAxis_StoresCorrectly()
    {
        var step = new StepExpression
        {
            Axis = Axis.Attribute,
            NodeTest = new NameTest { LocalName = "id" },
            Predicates = []
        };

        step.Axis.Should().Be(Axis.Attribute);
    }

    [Fact]
    public void StepExpression_WithPredicates_StoresCorrectly()
    {
        var predicate = new IntegerLiteral { Value = 1 };
        var step = new StepExpression
        {
            Axis = Axis.Child,
            NodeTest = new NameTest { LocalName = "item" },
            Predicates = [predicate]
        };

        step.Predicates.Should().HaveCount(1);
        step.Predicates[0].Should().BeSameAs(predicate);
    }

    [Fact]
    public void StepExpression_WithMultiplePredicates_StoresInOrder()
    {
        var pred1 = new IntegerLiteral { Value = 1 };
        var pred2 = new StringLiteral { Value = "test" };

        var step = new StepExpression
        {
            Axis = Axis.Child,
            NodeTest = new NameTest { LocalName = "item" },
            Predicates = [pred1, pred2]
        };

        step.Predicates.Should().HaveCount(2);
        step.Predicates[0].Should().BeSameAs(pred1);
        step.Predicates[1].Should().BeSameAs(pred2);
    }

    [Fact]
    public void StepExpression_ToString_ChildAxis_NoPrefix()
    {
        var step = new StepExpression
        {
            Axis = Axis.Child,
            NodeTest = new NameTest { LocalName = "element" },
            Predicates = []
        };

        step.ToString().Should().Be("element");
    }

    [Fact]
    public void StepExpression_ToString_AttributeAxis_AtSign()
    {
        var step = new StepExpression
        {
            Axis = Axis.Attribute,
            NodeTest = new NameTest { LocalName = "id" },
            Predicates = []
        };

        step.ToString().Should().Be("@id");
    }

    [Fact]
    public void StepExpression_ToString_DescendantAxis_DoubleColon()
    {
        var step = new StepExpression
        {
            Axis = Axis.Descendant,
            NodeTest = new NameTest { LocalName = "element" },
            Predicates = []
        };

        step.ToString().Should().Be("descendant::element");
    }

    [Fact]
    public void StepExpression_ToString_WithPredicates()
    {
        var step = new StepExpression
        {
            Axis = Axis.Child,
            NodeTest = new NameTest { LocalName = "item" },
            Predicates = [new IntegerLiteral { Value = 1 }]
        };

        step.ToString().Should().Be("item[1]");
    }

    [Fact]
    public void StepExpression_Accept_CallsVisitor()
    {
        var step = CreateSimpleStep("test");
        var visitor = new TestVisitor();

        var result = step.Accept(visitor);

        result.Should().Be("StepExpression");
    }

    #endregion

    #region NameTest Tests

    [Fact]
    public void NameTest_WithLocalNameOnly_StoresCorrectly()
    {
        var test = new NameTest { LocalName = "element" };

        test.LocalName.Should().Be("element");
        test.NamespaceUri.Should().BeNull();
        test.Prefix.Should().BeNull();
    }

    [Fact]
    public void NameTest_WithPrefix_StoresCorrectly()
    {
        var test = new NameTest
        {
            LocalName = "element",
            Prefix = "ns",
            NamespaceUri = "http://example.com/ns"
        };

        test.LocalName.Should().Be("element");
        test.Prefix.Should().Be("ns");
        test.NamespaceUri.Should().Be("http://example.com/ns");
    }

    [Fact]
    public void NameTest_Wildcard_LocalNameWildcard()
    {
        var test = new NameTest { LocalName = "*" };

        test.IsLocalNameWildcard.Should().BeTrue();
    }

    [Fact]
    public void NameTest_NotWildcard_LocalNameWildcardFalse()
    {
        var test = new NameTest { LocalName = "element" };

        test.IsLocalNameWildcard.Should().BeFalse();
    }

    [Fact]
    public void NameTest_NamespaceWildcard_IsNamespaceWildcardTrue()
    {
        var test = new NameTest
        {
            LocalName = "element",
            NamespaceUri = "*"
        };

        test.IsNamespaceWildcard.Should().BeTrue();
    }

    [Fact]
    public void NameTest_NoNamespaceWildcard_IsNamespaceWildcardFalse()
    {
        var test = new NameTest
        {
            LocalName = "element",
            NamespaceUri = "http://example.com"
        };

        test.IsNamespaceWildcard.Should().BeFalse();
    }

    [Fact]
    public void NameTest_Matches_SameLocalName_ReturnsTrue()
    {
        var test = new NameTest { LocalName = "element" };

        test.Matches(XdmNodeKind.Element, null, "element").Should().BeTrue();
    }

    [Fact]
    public void NameTest_Matches_DifferentLocalName_ReturnsFalse()
    {
        var test = new NameTest { LocalName = "element" };

        test.Matches(XdmNodeKind.Element, null, "other").Should().BeFalse();
    }

    [Fact]
    public void NameTest_Matches_LocalNameWildcard_ReturnsTrue()
    {
        var test = new NameTest { LocalName = "*" };

        test.Matches(XdmNodeKind.Element, null, "anything").Should().BeTrue();
    }

    [Fact]
    public void NameTest_Matches_WithResolvedNamespace_MatchingNs_ReturnsTrue()
    {
        var test = new NameTest
        {
            LocalName = "element",
            NamespaceUri = "http://example.com"
        };
        test.ResolvedNamespace = new NamespaceId(100);

        test.Matches(XdmNodeKind.Element, new NamespaceId(100), "element").Should().BeTrue();
    }

    [Fact]
    public void NameTest_Matches_WithResolvedNamespace_DifferentNs_ReturnsFalse()
    {
        var test = new NameTest
        {
            LocalName = "element",
            NamespaceUri = "http://example.com"
        };
        test.ResolvedNamespace = new NamespaceId(100);

        test.Matches(XdmNodeKind.Element, new NamespaceId(200), "element").Should().BeFalse();
    }

    [Fact]
    public void NameTest_ToString_LocalNameOnly()
    {
        var test = new NameTest { LocalName = "element" };

        test.ToString().Should().Be("element");
    }

    [Fact]
    public void NameTest_ToString_WithPrefix()
    {
        var test = new NameTest
        {
            LocalName = "element",
            Prefix = "ns"
        };

        test.ToString().Should().Be("ns:element");
    }

    [Fact]
    public void NameTest_ToString_NamespaceWildcard()
    {
        var test = new NameTest
        {
            LocalName = "element",
            NamespaceUri = "*"
        };

        test.ToString().Should().Be("*:element");
    }

    #endregion

    #region KindTest Tests

    [Fact]
    public void KindTest_ElementKind_StoresCorrectly()
    {
        var test = new KindTest { Kind = XdmNodeKind.Element };

        test.Kind.Should().Be(XdmNodeKind.Element);
    }

    [Fact]
    public void KindTest_AttributeKind_StoresCorrectly()
    {
        var test = new KindTest { Kind = XdmNodeKind.Attribute };

        test.Kind.Should().Be(XdmNodeKind.Attribute);
    }

    [Fact]
    public void KindTest_TextKind_StoresCorrectly()
    {
        var test = new KindTest { Kind = XdmNodeKind.Text };

        test.Kind.Should().Be(XdmNodeKind.Text);
    }

    [Fact]
    public void KindTest_CommentKind_StoresCorrectly()
    {
        var test = new KindTest { Kind = XdmNodeKind.Comment };

        test.Kind.Should().Be(XdmNodeKind.Comment);
    }

    [Fact]
    public void KindTest_PIKind_StoresCorrectly()
    {
        var test = new KindTest { Kind = XdmNodeKind.ProcessingInstruction };

        test.Kind.Should().Be(XdmNodeKind.ProcessingInstruction);
    }

    [Fact]
    public void KindTest_DocumentKind_StoresCorrectly()
    {
        var test = new KindTest { Kind = XdmNodeKind.Document };

        test.Kind.Should().Be(XdmNodeKind.Document);
    }

    [Fact]
    public void KindTest_NamespaceKind_StoresCorrectly()
    {
        var test = new KindTest { Kind = XdmNodeKind.Namespace };

        test.Kind.Should().Be(XdmNodeKind.Namespace);
    }

    [Fact]
    public void KindTest_NoneKind_MatchesEverything()
    {
        var test = new KindTest { Kind = XdmNodeKind.None };

        test.Matches(XdmNodeKind.Element, null, null).Should().BeTrue();
        test.Matches(XdmNodeKind.Attribute, null, null).Should().BeTrue();
        test.Matches(XdmNodeKind.Text, null, null).Should().BeTrue();
    }

    [Fact]
    public void KindTest_WithName_StoresCorrectly()
    {
        var name = new NameTest { LocalName = "element" };
        var test = new KindTest
        {
            Kind = XdmNodeKind.Element,
            Name = name
        };

        test.Name.Should().BeSameAs(name);
    }

    [Fact]
    public void KindTest_WithTypeName_StoresCorrectly()
    {
        var typeName = new XdmTypeName
        {
            LocalName = "integer",
            Prefix = "xs"
        };
        var test = new KindTest
        {
            Kind = XdmNodeKind.Element,
            TypeName = typeName
        };

        test.TypeName.Should().BeSameAs(typeName);
    }

    [Fact]
    public void KindTest_Matches_SameKind_ReturnsTrue()
    {
        var test = new KindTest { Kind = XdmNodeKind.Element };

        test.Matches(XdmNodeKind.Element, null, null).Should().BeTrue();
    }

    [Fact]
    public void KindTest_Matches_DifferentKind_ReturnsFalse()
    {
        var test = new KindTest { Kind = XdmNodeKind.Element };

        test.Matches(XdmNodeKind.Attribute, null, null).Should().BeFalse();
    }

    [Fact]
    public void KindTest_Matches_WithName_MatchingName_ReturnsTrue()
    {
        var test = new KindTest
        {
            Kind = XdmNodeKind.Element,
            Name = new NameTest { LocalName = "item" }
        };

        test.Matches(XdmNodeKind.Element, null, "item").Should().BeTrue();
    }

    [Fact]
    public void KindTest_Matches_WithName_DifferentName_ReturnsFalse()
    {
        var test = new KindTest
        {
            Kind = XdmNodeKind.Element,
            Name = new NameTest { LocalName = "item" }
        };

        test.Matches(XdmNodeKind.Element, null, "other").Should().BeFalse();
    }

    [Fact]
    public void KindTest_ToString_Node()
    {
        var test = new KindTest { Kind = XdmNodeKind.None };

        test.ToString().Should().Be("node()");
    }

    [Fact]
    public void KindTest_ToString_Element()
    {
        var test = new KindTest { Kind = XdmNodeKind.Element };

        test.ToString().Should().Be("element()");
    }

    [Fact]
    public void KindTest_ToString_Attribute()
    {
        var test = new KindTest { Kind = XdmNodeKind.Attribute };

        test.ToString().Should().Be("attribute()");
    }

    [Fact]
    public void KindTest_ToString_Text()
    {
        var test = new KindTest { Kind = XdmNodeKind.Text };

        test.ToString().Should().Be("text()");
    }

    [Fact]
    public void KindTest_ToString_Comment()
    {
        var test = new KindTest { Kind = XdmNodeKind.Comment };

        test.ToString().Should().Be("comment()");
    }

    [Fact]
    public void KindTest_ToString_PI()
    {
        var test = new KindTest { Kind = XdmNodeKind.ProcessingInstruction };

        test.ToString().Should().Be("processing-instruction()");
    }

    [Fact]
    public void KindTest_ToString_Document()
    {
        var test = new KindTest { Kind = XdmNodeKind.Document };

        test.ToString().Should().Be("document-node()");
    }

    [Fact]
    public void KindTest_ToString_WithName()
    {
        var test = new KindTest
        {
            Kind = XdmNodeKind.Element,
            Name = new NameTest { LocalName = "item" }
        };

        test.ToString().Should().Be("element(item)");
    }

    [Fact]
    public void KindTest_ToString_WithTypeName()
    {
        var test = new KindTest
        {
            Kind = XdmNodeKind.Element,
            TypeName = new XdmTypeName { LocalName = "integer", Prefix = "xs" }
        };

        test.ToString().Should().Be("element(*, xs:integer)");
    }

    #endregion

    #region XdmTypeName Tests

    [Fact]
    public void XdmTypeName_WithLocalName_StoresCorrectly()
    {
        var typeName = new XdmTypeName { LocalName = "string" };

        typeName.LocalName.Should().Be("string");
    }

    [Fact]
    public void XdmTypeName_WithNamespaceUri_StoresCorrectly()
    {
        var typeName = new XdmTypeName
        {
            LocalName = "string",
            NamespaceUri = "http://www.w3.org/2001/XMLSchema"
        };

        typeName.NamespaceUri.Should().Be("http://www.w3.org/2001/XMLSchema");
    }

    [Fact]
    public void XdmTypeName_WithPrefix_StoresCorrectly()
    {
        var typeName = new XdmTypeName
        {
            LocalName = "string",
            Prefix = "xs"
        };

        typeName.Prefix.Should().Be("xs");
    }

    [Fact]
    public void XdmTypeName_ToString_LocalNameOnly()
    {
        var typeName = new XdmTypeName { LocalName = "string" };

        typeName.ToString().Should().Be("string");
    }

    [Fact]
    public void XdmTypeName_ToString_WithPrefix()
    {
        var typeName = new XdmTypeName
        {
            LocalName = "string",
            Prefix = "xs"
        };

        typeName.ToString().Should().Be("xs:string");
    }

    #endregion

    #region FilterExpression Tests

    [Fact]
    public void FilterExpression_WithPrimary_StoresCorrectly()
    {
        var primary = new StringLiteral { Value = "test" };
        var filter = new FilterExpression
        {
            Primary = primary,
            Predicates = []
        };

        filter.Primary.Should().BeSameAs(primary);
    }

    [Fact]
    public void FilterExpression_WithSinglePredicate_StoresCorrectly()
    {
        var predicate = new IntegerLiteral { Value = 1 };
        var filter = new FilterExpression
        {
            Primary = new StringLiteral { Value = "test" },
            Predicates = [predicate]
        };

        filter.Predicates.Should().HaveCount(1);
        filter.Predicates[0].Should().BeSameAs(predicate);
    }

    [Fact]
    public void FilterExpression_WithMultiplePredicates_StoresInOrder()
    {
        var pred1 = new IntegerLiteral { Value = 1 };
        var pred2 = new IntegerLiteral { Value = 2 };

        var filter = new FilterExpression
        {
            Primary = new StringLiteral { Value = "test" },
            Predicates = [pred1, pred2]
        };

        filter.Predicates.Should().HaveCount(2);
        filter.Predicates[0].Should().BeSameAs(pred1);
        filter.Predicates[1].Should().BeSameAs(pred2);
    }

    [Fact]
    public void FilterExpression_ToString_WithPredicates()
    {
        var filter = new FilterExpression
        {
            Primary = new StringLiteral { Value = "items" },
            Predicates = [new IntegerLiteral { Value = 1 }]
        };

        filter.ToString().Should().Contain("[1]");
    }

    [Fact]
    public void FilterExpression_Accept_CallsVisitor()
    {
        var filter = new FilterExpression
        {
            Primary = new StringLiteral { Value = "test" },
            Predicates = []
        };
        var visitor = new TestVisitor();

        var result = filter.Accept(visitor);

        result.Should().Be("FilterExpression");
    }

    #endregion

    #region All Axis ToString Tests

    [Theory]
    [InlineData(Axis.Child, "child")]
    [InlineData(Axis.Descendant, "descendant::child")]
    [InlineData(Axis.DescendantOrSelf, "descendant-or-self::child")]
    [InlineData(Axis.Parent, "parent::child")]
    [InlineData(Axis.Ancestor, "ancestor::child")]
    [InlineData(Axis.AncestorOrSelf, "ancestor-or-self::child")]
    [InlineData(Axis.FollowingSibling, "following-sibling::child")]
    [InlineData(Axis.PrecedingSibling, "preceding-sibling::child")]
    [InlineData(Axis.Following, "following::child")]
    [InlineData(Axis.Preceding, "preceding::child")]
    [InlineData(Axis.Self, "self::child")]
    [InlineData(Axis.Attribute, "@child")]
    [InlineData(Axis.Namespace, "namespace::child")]
    public void StepExpression_ToString_AllAxes(Axis axis, string expected)
    {
        var step = new StepExpression
        {
            Axis = axis,
            NodeTest = new NameTest { LocalName = "child" },
            Predicates = []
        };

        step.ToString().Should().Be(expected);
    }

    #endregion

    #region Helper Methods

    private static StepExpression CreateSimpleStep(string localName)
    {
        return new StepExpression
        {
            Axis = Axis.Child,
            NodeTest = new NameTest { LocalName = localName },
            Predicates = []
        };
    }

    #endregion

    /// <summary>
    /// Test visitor implementation for verifying Accept calls.
    /// </summary>
    private class TestVisitor : XQueryExpressionVisitor<string>
    {
        public override string VisitPathExpression(PathExpression expr) => "PathExpression";
        public override string VisitStepExpression(StepExpression expr) => "StepExpression";
        public override string VisitFilterExpression(FilterExpression expr) => "FilterExpression";
    }
}
