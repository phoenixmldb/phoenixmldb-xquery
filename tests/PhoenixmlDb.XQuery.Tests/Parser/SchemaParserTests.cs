using FluentAssertions;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Parser;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Parser;

/// <summary>
/// Tests that the parser builds correct AST nodes for schema-aware features.
/// Previously these constructs threw at parse time; now they produce proper AST
/// nodes that get gated at static analysis when no ISchemaProvider is registered.
/// </summary>
[Trait("Category", "Parser")]
public class SchemaParserTests
{
    private readonly XQueryParserFacade _parser = new();

    // ──────────────────────────────────────────────
    //  validate expression
    // ──────────────────────────────────────────────

    [Fact]
    public void Parse_ValidateStrict_ProducesValidateExpression()
    {
        var result = _parser.Parse("validate { 1 }");

        result.Should().BeOfType<ValidateExpression>();
        var validate = (ValidateExpression)result;
        validate.Mode.Should().Be(ValidationMode.Strict);
        validate.TypeName.Should().BeNull();
    }

    [Fact]
    public void Parse_ValidateStrictExplicit_ProducesStrictMode()
    {
        var result = _parser.Parse("validate strict { 1 }");

        var validate = result.Should().BeOfType<ValidateExpression>().Subject;
        validate.Mode.Should().Be(ValidationMode.Strict);
    }

    [Fact]
    public void Parse_ValidateLax_ProducesLaxMode()
    {
        var result = _parser.Parse("validate lax { 1 }");

        var validate = result.Should().BeOfType<ValidateExpression>().Subject;
        validate.Mode.Should().Be(ValidationMode.Lax);
    }

    [Fact]
    public void Parse_ValidateType_ProducesTypeModeWithName()
    {
        var result = _parser.Parse("validate type xs:integer { 1 }");

        var validate = result.Should().BeOfType<ValidateExpression>().Subject;
        validate.Mode.Should().Be(ValidationMode.Type);
        validate.TypeName.Should().NotBeNull();
        validate.TypeName!.LocalName.Should().Be("integer");
    }

    [Fact]
    public void Parse_ValidateExpression_BodyIsParsed()
    {
        var result = _parser.Parse("validate { 1 + 2 }");

        var validate = result.Should().BeOfType<ValidateExpression>().Subject;
        validate.Expression.Should().BeOfType<BinaryExpression>();
    }

    // ──────────────────────────────────────────────
    //  schema-element() / schema-attribute() in path steps
    // ──────────────────────────────────────────────

    [Fact]
    public void Parse_SchemaElement_InStep_ProducesSchemaElementTest()
    {
        var result = _parser.Parse("./schema-element(invoice)");

        var path = result.Should().BeOfType<PathExpression>().Subject;
        path.Steps.Should().ContainSingle();
        path.Steps[0].NodeTest.Should().BeOfType<SchemaElementTest>();
        var test = (SchemaElementTest)path.Steps[0].NodeTest;
        test.LocalName.Should().Be("invoice");
    }

    [Fact]
    public void Parse_SchemaAttribute_InStep_ProducesSchemaAttributeTest()
    {
        var result = _parser.Parse("./schema-attribute(id)");

        var path = result.Should().BeOfType<PathExpression>().Subject;
        path.Steps.Should().ContainSingle();
        path.Steps[0].NodeTest.Should().BeOfType<SchemaAttributeTest>();
        var test = (SchemaAttributeTest)path.Steps[0].NodeTest;
        test.LocalName.Should().Be("id");
    }

    // ──────────────────────────────────────────────
    //  schema-element() / schema-attribute() in sequence types
    // ──────────────────────────────────────────────

    [Fact]
    public void Parse_InstanceOf_SchemaElement_ProducesSchemaElementItemType()
    {
        var result = _parser.Parse("1 instance of schema-element(invoice)");

        var instanceOf = result.Should().BeOfType<InstanceOfExpression>().Subject;
        instanceOf.TargetType.ItemType.Should().Be(ItemType.SchemaElement);
        instanceOf.TargetType.SchemaElementName.Should().Be("invoice");
    }

    [Fact]
    public void Parse_TreatAs_SchemaAttribute_ProducesSchemaAttributeItemType()
    {
        var result = _parser.Parse("1 treat as schema-attribute(code)");

        var treat = result.Should().BeOfType<TreatExpression>().Subject;
        treat.TargetType.ItemType.Should().Be(ItemType.SchemaAttribute);
        treat.TargetType.SchemaAttributeName.Should().Be("code");
    }

    // ──────────────────────────────────────────────
    //  import schema in prolog
    // ──────────────────────────────────────────────

    [Fact]
    public void Parse_ImportSchema_ProducesSchemaImportExpression()
    {
        var result = _parser.Parse("""
            import schema "http://example.com/orders";
            1
            """);

        // Module expression wraps declarations + body
        var module = result.Should().BeOfType<ModuleExpression>().Subject;
        module.Declarations.Should().ContainSingle()
            .Which.Should().BeOfType<SchemaImportExpression>();

        var import = (SchemaImportExpression)module.Declarations[0];
        import.TargetNamespace.Should().Be("http://example.com/orders");
        import.Prefix.Should().BeNull();
        import.IsDefaultElementNamespace.Should().BeFalse();
        import.LocationHints.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ImportSchemaWithPrefix_ParsesPrefixCorrectly()
    {
        var result = _parser.Parse("""
            import schema namespace ord = "http://example.com/orders";
            1
            """);

        var module = result.Should().BeOfType<ModuleExpression>().Subject;
        var import = module.Declarations.OfType<SchemaImportExpression>().Single();
        import.Prefix.Should().Be("ord");
        import.TargetNamespace.Should().Be("http://example.com/orders");
    }

    [Fact]
    public void Parse_ImportSchemaDefaultElementNamespace_SetsFlag()
    {
        var result = _parser.Parse("""
            import schema default element namespace "http://example.com/orders";
            1
            """);

        var module = result.Should().BeOfType<ModuleExpression>().Subject;
        var import = module.Declarations.OfType<SchemaImportExpression>().Single();
        import.IsDefaultElementNamespace.Should().BeTrue();
        import.Prefix.Should().BeNull();
    }

    [Fact]
    public void Parse_ImportSchemaWithLocationHint_ParsesHints()
    {
        var result = _parser.Parse("""
            import schema "http://example.com/orders" at "orders.xsd";
            1
            """);

        var module = result.Should().BeOfType<ModuleExpression>().Subject;
        var import = module.Declarations.OfType<SchemaImportExpression>().Single();
        import.LocationHints.Should().ContainSingle().Which.Should().Be("orders.xsd");
    }

    [Fact]
    public void Parse_ImportSchemaWithMultipleLocationHints_ParsesAll()
    {
        var result = _parser.Parse("""
            import schema "http://example.com/orders" at "orders.xsd", "backup.xsd";
            1
            """);

        var module = result.Should().BeOfType<ModuleExpression>().Subject;
        var import = module.Declarations.OfType<SchemaImportExpression>().Single();
        import.LocationHints.Should().HaveCount(2);
        import.LocationHints[0].Should().Be("orders.xsd");
        import.LocationHints[1].Should().Be("backup.xsd");
    }

    // ──────────────────────────────────────────────
    //  End-to-end: schema features produce static errors (no provider)
    // ──────────────────────────────────────────────

    [Fact]
    public async Task Evaluate_ValidateExpression_WithoutProvider_ThrowsWithSchemaMessage()
    {
        var facade = new XQueryFacade();

        var act = () => facade.EvaluateAsync("validate { <root/> }");
        var ex = await act.Should().ThrowAsync<Exception>();
        ex.Which.Message.Should().Contain("Schema Validation Feature");
    }

    [Fact]
    public async Task Evaluate_ImportSchema_WithoutProvider_ThrowsWithSchemaMessage()
    {
        var facade = new XQueryFacade();

        var act = () => facade.EvaluateAsync("""
            import schema "http://example.com/schema";
            1
            """);
        var ex = await act.Should().ThrowAsync<Exception>();
        ex.Which.Message.Should().Contain("Schema Import Feature");
    }
}
