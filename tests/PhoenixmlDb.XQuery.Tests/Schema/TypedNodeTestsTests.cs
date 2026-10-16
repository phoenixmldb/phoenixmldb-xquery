using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.Parser;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Schema;

/// <summary>
/// element(*, T), attribute(*, T) and schema-element(N) against validated nodes.
/// </summary>
public class TypedNodeTestsTests
{
    private const string Xsd = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" targetNamespace="urn:u" xmlns:u="urn:u" elementFormDefault="qualified">
          <xs:simpleType name="part"><xs:restriction base="xs:string"><xs:pattern value="\d{3}-[A-Z]{2}"/></xs:restriction></xs:simpleType>
          <xs:simpleType name="partOrInt"><xs:union memberTypes="u:part xs:integer"/></xs:simpleType>
          <xs:simpleType name="outer"><xs:union memberTypes="u:partOrInt xs:boolean"/></xs:simpleType>
          <xs:element name="un" type="u:partOrInt"/>
          <xs:element name="deep" type="u:outer"/>
          <xs:element name="holder"><xs:complexType><xs:attribute name="p" type="u:part"/></xs:complexType></xs:element>
        </xs:schema>
        """;

    private static async Task<string> EvalAsync(string query, string document)
    {
        var schemas = new XsdSchemaProvider();
        schemas.AddFromString("urn:u", Xsd);
        var store = new XdmDocumentStore();
        var doc = schemas.ValidateAndAnnotate(document, store, ValidationMode.Strict);
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store, schemaProvider: schemas);
        var compiled = engine.Compile("declare namespace u = 'urn:u'; " + query);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors));
        var items = new List<object?>();
        await foreach (var i in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext(initialContextItem: doc)))
            items.Add(i);
        return string.Join(",", items.Select(i => i?.ToString() ?? ""));
    }

    /// <summary>
    /// Since XPath 3.0 an element or attribute whose annotation is a member type of a union U
    /// matches element(*, U), and an element of a union type is annotated with the member its
    /// value matched. No element of a union type matched element(*, U), nor schema-element(N)
    /// of its declaration.
    /// </summary>
    [Theory]
    [InlineData("<u:un xmlns:u='urn:u'>100</u:un>")]
    [InlineData("<u:un xmlns:u='urn:u'>123-AB</u:un>")]
    public async Task An_element_of_a_union_type_matches_the_union(string document)
        => (await EvalAsync("u:un instance of element(*, u:partOrInt), u:un instance of element(u:un, u:partOrInt), u:un instance of schema-element(u:un)", document))
            .Should().Be("True,True,True");

    [Fact]
    public async Task A_member_of_a_union_in_a_union_matches_the_outer_union()
        => (await EvalAsync("u:deep instance of element(*, u:outer), u:deep instance of schema-element(u:deep)", "<u:deep xmlns:u='urn:u'>123-AB</u:deep>"))
            .Should().Be("True,True");

    [Fact]
    public async Task An_element_of_another_type_does_not_match_the_union()
        => (await EvalAsync("u:holder instance of element(*, u:partOrInt)", "<u:holder xmlns:u='urn:u' p='123-AB'/>"))
            .Should().Be("False");

    /// <summary>
    /// A validated attribute matches attribute(*, T) for a type T it is derived from, as an
    /// element does. Only the untyped hierarchy was known for attributes.
    /// </summary>
    [Fact]
    public async Task A_validated_attribute_matches_a_type_it_derives_from()
        => (await EvalAsync("u:holder/@p instance of attribute(*, u:part), u:holder/@p instance of attribute(*, xs:string), u:holder/@p instance of attribute(p, xs:anyAtomicType), u:holder/@p instance of attribute(*, xs:integer)",
                "<u:holder xmlns:u='urn:u' p='123-AB'/>"))
            .Should().Be("True,True,True,False");

    /// <summary>
    /// schema-element(N) with no prefix, in a host language's expression, is in the default
    /// namespace for elements that the host gives (xpath-default-namespace in XSLT).
    /// </summary>
    [Fact]
    public void Schema_element_takes_the_default_namespace_of_the_host()
    {
        var parser = new XQueryParserFacade { AllowNamespaceAxis = true };
        var type = ((PhoenixmlDb.XQuery.Ast.InstanceOfExpression)parser.Parse(". instance of schema-element(un)",
            prefix => prefix.Length == 0 ? "urn:u" : null)).TargetType;
        type.SchemaElementNamespace.Should().Be("urn:u");
    }

    /// <summary>
    /// A validated element whose type has element-only content has no whitespace text children
    /// (XDM 3.1 §6.2.3). They were kept. Whitespace in mixed or simple content stays.
    /// </summary>
    [Fact]
    public void Whitespace_in_element_only_content_is_not_in_the_validated_tree()
    {
        const string xsd = """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
              <xs:element name="list"><xs:complexType><xs:sequence>
                <xs:element name="item" type="xs:string" maxOccurs="unbounded"/>
                <xs:element name="note"><xs:complexType mixed="true"><xs:sequence><xs:element name="b" minOccurs="0"/></xs:sequence></xs:complexType></xs:element>
              </xs:sequence></xs:complexType></xs:element>
            </xs:schema>
            """;
        var schemas = new XsdSchemaProvider();
        schemas.AddFromString("", xsd);
        var store = new XdmDocumentStore();
        var document = (PhoenixmlDb.Xdm.Nodes.XdmDocument)schemas.ValidateAndAnnotate(
            "<list>\n  <item>  </item>\n  <item>a</item>\n  <note> <b/> </note>\n</list>", store, ValidationMode.Strict)!;
        var list = (PhoenixmlDb.Xdm.Nodes.XdmElement)store.GetNode(document.Children[0])!;
        var kinds = list.Children.Select(id => store.GetNode(id)!).Select(n => n is PhoenixmlDb.Xdm.Nodes.XdmElement e ? e.LocalName : "text").ToList();
        kinds.Should().Equal("item", "item", "note");
        var firstItem = (PhoenixmlDb.Xdm.Nodes.XdmElement)store.GetNode(list.Children[0])!;
        firstItem.Children.Should().HaveCount(1, "the whitespace value of an element of a simple type stays");
        var note = (PhoenixmlDb.Xdm.Nodes.XdmElement)store.GetNode(list.Children[2])!;
        note.Children.Should().HaveCount(3, "whitespace in mixed content stays");
    }

    /// <summary>
    /// fn:sum, fn:avg, fn:min and fn:max of nodes of a list type take each item of their typed
    /// values (atomization flattens). Each node gave the whole list as one value: max failed as
    /// "not orderable" and avg as "invalid argument type".
    /// </summary>
    [Fact]
    public async Task Aggregates_take_each_item_of_a_list_typed_value()
    {
        const string xsd = """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
              <xs:simpleType name="decimals"><xs:list itemType="xs:decimal"/></xs:simpleType>
              <xs:element name="t"><xs:complexType><xs:sequence>
                <xs:element name="v" type="decimals" maxOccurs="unbounded"/>
              </xs:sequence></xs:complexType></xs:element>
            </xs:schema>
            """;
        var schemas = new XsdSchemaProvider();
        schemas.AddFromString("", xsd);
        var store = new XdmDocumentStore();
        var doc = schemas.ValidateAndAnnotate("<t><v>1.5 2</v><v>4</v></t>", store, ValidationMode.Strict);
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store, schemaProvider: schemas);
        var compiled = engine.Compile("(sum(/t/v), avg(/t/v), min(/t/v), max(/t/v), count(data(/t/v)))");
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors));
        var items = new List<double>();
        await foreach (var i in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext(initialContextItem: doc)))
            items.Add(Convert.ToDouble(i, System.Globalization.CultureInfo.InvariantCulture));
        items.Should().Equal(7.5, 2.5, 1.5, 4, 3);
    }

    /// <summary>
    /// The typed value of an attribute of a NOTATION type is an xs:NOTATION, and an instance
    /// of its own type; it is not an xs:QName. No value was ever an xs:NOTATION.
    /// </summary>
    [Fact]
    public async Task The_typed_value_of_a_notation_attribute_is_an_xs_NOTATION()
    {
        const string xsd = """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" targetNamespace="urn:n" xmlns:n="urn:n">
              <xs:notation name="jpeg" public="image/jpeg"/>
              <xs:notation name="png" public="image/png"/>
              <xs:simpleType name="pic"><xs:restriction base="xs:NOTATION"><xs:enumeration value="n:jpeg"/><xs:enumeration value="n:png"/></xs:restriction></xs:simpleType>
              <xs:element name="img"><xs:complexType><xs:attribute name="type" type="n:pic"/></xs:complexType></xs:element>
            </xs:schema>
            """;
        var schemas = new XsdSchemaProvider();
        schemas.AddFromString("urn:n", xsd);
        var store = new XdmDocumentStore();
        var doc = schemas.ValidateAndAnnotate("<n:img xmlns:n='urn:n' type='n:png'/>", store, ValidationMode.Strict);
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store, schemaProvider: schemas);
        var compiled = engine.Compile("declare namespace n = 'urn:n'; let $v := data(/n:img/@type) return ($v instance of xs:NOTATION, $v instance of n:pic, $v instance of xs:QName, string($v), $v eq data(/n:img/@type))");
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors));
        var items = new List<string>();
        await foreach (var i in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext(initialContextItem: doc)))
            items.Add(i?.ToString() ?? "");
        items.Should().Equal("True", "True", "False", "n:png", "True");
    }

    /// <summary>
    /// The constructor function of a type derived from xs:NOTATION makes an xs:NOTATION, and a
    /// QName or a NOTATION casts to such a type (XPath 3.1 §19.1). The constructed value was an
    /// instance of its own type and not of xs:NOTATION, and a QName cast was "Cannot cast
    /// xs:QName to Notation".
    /// </summary>
    [Fact]
    public async Task A_notation_type_constructs_and_casts_to_xs_NOTATION()
    {
        const string xsd = """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" targetNamespace="urn:n" xmlns:n="urn:n">
              <xs:notation name="jpeg" public="image/jpeg"/>
              <xs:notation name="png" public="image/png"/>
              <xs:simpleType name="pic"><xs:restriction base="xs:NOTATION"><xs:enumeration value="n:jpeg"/><xs:enumeration value="n:png"/></xs:restriction></xs:simpleType>
              <xs:simpleType name="pic2"><xs:restriction base="xs:NOTATION"><xs:enumeration value="n:png"/></xs:restriction></xs:simpleType>
            </xs:schema>
            """;
        var schemas = new XsdSchemaProvider();
        schemas.AddFromString("urn:n", xsd);
        var engine = new QueryEngine(schemaProvider: schemas);
        var compiled = engine.Compile("import schema namespace n = 'urn:n'; "
            + "(n:pic('n:png') instance of xs:NOTATION, n:pic('n:png') instance of n:pic, "
            + "xs:QName('n:png') castable as n:pic, (xs:QName('n:png') cast as n:pic) instance of xs:NOTATION, "
            + "n:pic('n:png') castable as n:pic2, local-name-from-QName(xs:QName(n:pic2(n:pic('n:png')))))");
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors));
        var items = new List<string>();
        await foreach (var i in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
            items.Add(i?.ToString() ?? "");
        items.Should().Equal("True", "True", "True", "True", "True", "png");
    }

    /// <summary>
    /// A value of type xs:ENTITY names an entity the document declares. The validating parse
    /// reads no document type declaration, so the caller that read one names its entities with
    /// <see cref="XsdSchemaProvider.DeclaringEntities"/>; without it every such value was
    /// "Reference to an undeclared entity". A name the scope does not hold is still an error.
    /// </summary>
    [Fact]
    public void An_entity_value_is_valid_when_the_caller_declares_the_entity()
    {
        const string xsd = """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
              <xs:element name="e"><xs:complexType><xs:attribute name="ent" type="xs:ENTITY"/></xs:complexType></xs:element>
            </xs:schema>
            """;
        var schemas = new XsdSchemaProvider();
        schemas.AddFromString("", xsd);
        var store = new XdmDocumentStore();
        var undeclared = () => schemas.ValidateAndAnnotate("<e ent='pic'/>", store, ValidationMode.Strict);
        undeclared.Should().Throw<SchemaValidationException>().WithMessage("*undeclared entity*");
        using (XsdSchemaProvider.DeclaringEntities(["pic"]))
        {
            schemas.ValidateAndAnnotate("<e ent='pic'/>", store, ValidationMode.Strict).Should().NotBeNull();
            var other = () => schemas.ValidateAndAnnotate("<e ent='other'/>", store, ValidationMode.Strict);
            other.Should().Throw<SchemaValidationException>();
        }
        undeclared.Should().Throw<SchemaValidationException>();
    }

    /// <summary>
    /// fn:distinct-values of validated nodes compares their typed values: 1.0 and 1 of type
    /// xs:decimal are one value, and each item of a list counts. The string values were compared.
    /// </summary>
    [Fact]
    public async Task Distinct_values_of_validated_nodes_compares_typed_values()
    {
        const string xsd = """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
              <xs:simpleType name="decimals"><xs:list itemType="xs:decimal"/></xs:simpleType>
              <xs:element name="t"><xs:complexType><xs:sequence>
                <xs:element name="d" type="xs:decimal" maxOccurs="unbounded"/>
                <xs:element name="l" type="decimals"/>
              </xs:sequence></xs:complexType></xs:element>
            </xs:schema>
            """;
        var schemas = new XsdSchemaProvider();
        schemas.AddFromString("", xsd);
        var store = new XdmDocumentStore();
        var doc = schemas.ValidateAndAnnotate("<t><d>1.0</d><d>1</d><d>2</d><l>2 3 3</l></t>", store, ValidationMode.Strict);
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store, schemaProvider: schemas);
        var compiled = engine.Compile("(count(distinct-values(/t/d)), count(distinct-values(/t/(d, l))))");
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors));
        var items = new List<string>();
        await foreach (var i in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext(initialContextItem: doc)))
            items.Add(i?.ToString() ?? "");
        items.Should().Equal("2", "3");
    }
}
