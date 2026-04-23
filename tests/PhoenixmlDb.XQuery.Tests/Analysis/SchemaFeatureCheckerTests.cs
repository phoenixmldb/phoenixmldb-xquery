using FluentAssertions;
using PhoenixmlDb.XQuery.Analysis;
using PhoenixmlDb.XQuery.Ast;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Analysis;

/// <summary>
/// Tests that schema-aware features are correctly gated when no ISchemaProvider is registered.
/// Without a provider, the SchemaFeatureChecker should report errors for:
/// - validate expressions (XQST0075)
/// - schema-element() / schema-attribute() node tests (XPST0008)
/// - schema-element() / schema-attribute() in sequence types (XPST0008)
/// - import schema declarations (XQST0009)
/// </summary>
public class SchemaFeatureCheckerTests
{
    // ──────────────────────────────────────────────
    //  validate expression → XQST0075
    // ──────────────────────────────────────────────

    [Fact]
    public void ValidateExpression_ReportsXQST0075()
    {
        var checker = new SchemaFeatureChecker();
        var expr = new ValidateExpression
        {
            Mode = ValidationMode.Strict,
            Expression = new IntegerLiteral { Value = 1 }
        };

        checker.Walk(expr);

        checker.Errors.Should().ContainSingle()
            .Which.Code.Should().Be("XQST0075");
    }

    [Fact]
    public void ValidateLax_ReportsXQST0075()
    {
        var checker = new SchemaFeatureChecker();
        var expr = new ValidateExpression
        {
            Mode = ValidationMode.Lax,
            Expression = new IntegerLiteral { Value = 1 }
        };

        checker.Walk(expr);

        checker.Errors.Should().ContainSingle()
            .Which.Code.Should().Be("XQST0075");
    }

    [Fact]
    public void ValidateType_ReportsXQST0075()
    {
        var checker = new SchemaFeatureChecker();
        var expr = new ValidateExpression
        {
            Mode = ValidationMode.Type,
            TypeName = new XdmTypeName { LocalName = "myType", Prefix = "ns" },
            Expression = new IntegerLiteral { Value = 1 }
        };

        checker.Walk(expr);

        checker.Errors.Should().ContainSingle()
            .Which.Code.Should().Be("XQST0075");
    }

    // ──────────────────────────────────────────────
    //  schema-element() / schema-attribute() in step → XPST0008
    // ──────────────────────────────────────────────

    [Fact]
    public void SchemaElementTest_InStep_ReportsXPST0008()
    {
        var checker = new SchemaFeatureChecker();
        var step = new StepExpression
        {
            Axis = Axis.Child,
            NodeTest = new SchemaElementTest { LocalName = "invoice" },
            Predicates = []
        };

        checker.Walk(step);

        checker.Errors.Should().ContainSingle()
            .Which.Code.Should().Be("XPST0008");
    }

    [Fact]
    public void SchemaAttributeTest_InStep_ReportsXPST0008()
    {
        var checker = new SchemaFeatureChecker();
        var step = new StepExpression
        {
            Axis = Axis.Attribute,
            NodeTest = new SchemaAttributeTest { LocalName = "id" },
            Predicates = []
        };

        checker.Walk(step);

        checker.Errors.Should().ContainSingle()
            .Which.Code.Should().Be("XPST0008");
    }

    [Fact]
    public void SchemaElementTest_ErrorMessageIncludesName()
    {
        var checker = new SchemaFeatureChecker();
        var step = new StepExpression
        {
            Axis = Axis.Child,
            NodeTest = new SchemaElementTest { LocalName = "purchase-order", Prefix = "po" },
            Predicates = []
        };

        checker.Walk(step);

        checker.Errors.Should().ContainSingle()
            .Which.Message.Should().Contain("po:purchase-order");
    }

    // ──────────────────────────────────────────────
    //  schema-element() / schema-attribute() in sequence types → XPST0008
    // ──────────────────────────────────────────────

    [Fact]
    public void SchemaElement_InInstanceOf_ReportsXPST0008()
    {
        var checker = new SchemaFeatureChecker();
        var expr = new InstanceOfExpression
        {
            Expression = new IntegerLiteral { Value = 1 },
            TargetType = new XdmSequenceType
            {
                ItemType = ItemType.SchemaElement,
                Occurrence = Occurrence.ExactlyOne,
                SchemaElementName = "invoice"
            }
        };

        checker.Walk(expr);

        checker.Errors.Should().ContainSingle()
            .Which.Code.Should().Be("XPST0008");
    }

    [Fact]
    public void SchemaAttribute_InTreatAs_ReportsXPST0008()
    {
        var checker = new SchemaFeatureChecker();
        var expr = new TreatExpression
        {
            Expression = new IntegerLiteral { Value = 1 },
            TargetType = new XdmSequenceType
            {
                ItemType = ItemType.SchemaAttribute,
                Occurrence = Occurrence.ExactlyOne,
                SchemaAttributeName = "id"
            }
        };

        checker.Walk(expr);

        checker.Errors.Should().ContainSingle()
            .Which.Code.Should().Be("XPST0008");
    }

    // ──────────────────────────────────────────────
    //  import schema → XQST0009
    // ──────────────────────────────────────────────

    [Fact]
    public void SchemaImport_ReportsXQST0009()
    {
        var checker = new SchemaFeatureChecker();
        var expr = new SchemaImportExpression
        {
            TargetNamespace = "http://example.com/schema"
        };

        checker.Walk(expr);

        checker.Errors.Should().ContainSingle()
            .Which.Code.Should().Be("XQST0009");
    }

    [Fact]
    public void SchemaImport_ErrorMessageIncludesNamespace()
    {
        var checker = new SchemaFeatureChecker();
        var expr = new SchemaImportExpression
        {
            TargetNamespace = "http://example.com/orders"
        };

        checker.Walk(expr);

        checker.Errors.Should().ContainSingle()
            .Which.Message.Should().Contain("http://example.com/orders");
    }

    // ──────────────────────────────────────────────
    //  Non-schema features should not trigger errors
    // ──────────────────────────────────────────────

    [Fact]
    public void RegularNameTest_NoErrors()
    {
        var checker = new SchemaFeatureChecker();
        var step = new StepExpression
        {
            Axis = Axis.Child,
            NodeTest = new NameTest { LocalName = "customer" },
            Predicates = []
        };

        checker.Walk(step);

        checker.Errors.Should().BeEmpty();
    }

    [Fact]
    public void KindTest_NoErrors()
    {
        var checker = new SchemaFeatureChecker();
        var step = new StepExpression
        {
            Axis = Axis.Child,
            NodeTest = new KindTest { Kind = PhoenixmlDb.Core.XdmNodeKind.Element },
            Predicates = []
        };

        checker.Walk(step);

        checker.Errors.Should().BeEmpty();
    }

    [Fact]
    public void InstanceOf_WithRegularType_NoErrors()
    {
        var checker = new SchemaFeatureChecker();
        var expr = new InstanceOfExpression
        {
            Expression = new IntegerLiteral { Value = 1 },
            TargetType = new XdmSequenceType
            {
                ItemType = ItemType.Integer,
                Occurrence = Occurrence.ExactlyOne
            }
        };

        checker.Walk(expr);

        checker.Errors.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────
    //  Multiple errors in a single query
    // ──────────────────────────────────────────────

    [Fact]
    public void NestedSchemaFeatures_ReportsMultipleErrors()
    {
        var checker = new SchemaFeatureChecker();

        // validate { schema-element(invoice) }
        var validateExpr = new ValidateExpression
        {
            Mode = ValidationMode.Strict,
            Expression = new StepExpression
            {
                Axis = Axis.Child,
                NodeTest = new SchemaElementTest { LocalName = "invoice" },
                Predicates = []
            }
        };

        checker.Walk(validateExpr);

        // Should report both XQST0075 (validate) and XPST0008 (schema-element)
        checker.Errors.Should().HaveCount(2);
        checker.Errors.Should().Contain(e => e.Code == "XQST0075");
        checker.Errors.Should().Contain(e => e.Code == "XPST0008");
    }
}
