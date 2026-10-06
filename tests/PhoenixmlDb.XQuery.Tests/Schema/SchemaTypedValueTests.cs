using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using PhoenixmlDb.XQuery;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Schema;

/// <summary>
/// The typed value of a validated node (XDM 3.1 §5.15). Validation recorded the type annotation,
/// but atomization ignored it, so every validated node still atomized to xs:untypedAtomic:
/// <c>data($price) instance of xs:decimal</c> was false and <c>$a + $b</c> over two xs:float
/// elements was computed as xs:double (QT3 schema-import-5..25, casthcds*, ForExprType049..051).
/// </summary>
public sealed class SchemaTypedValueTests : System.IDisposable
{
    private const string Prolog = """
        import schema namespace t = 'urn:t' at 'schema.xsd';
        declare variable $raw := <t:order t:qty="3"><t:price>-0.0</t:price><t:rate>1.5</t:rate><t:flag>1</t:flag><t:tags>a b c</t:tags><t:note>n</t:note><t:size>7</t:size><t:sizes>7 8 9</t:sizes><t:weight unit="kg">2.5</t:weight><t:box><t:size>8</t:size></t:box><t:code>abc</t:code><t:grade>7</t:grade></t:order>;
        declare variable $valid := validate strict { $raw };

        """;

    private readonly string _tempDir;
    private readonly XQueryFacade _facade = new();

    public SchemaTypedValueTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"phoenixmldb-typed-value-{System.Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "schema.xsd"), """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:t="urn:t"
                       targetNamespace="urn:t" elementFormDefault="qualified">
              <xs:attribute name="qty" type="xs:integer"/>
              <xs:simpleType name="hatsize">
                <xs:restriction base="xs:integer"><xs:minInclusive value="1"/></xs:restriction>
              </xs:simpleType>
              <xs:simpleType name="hatsizes"><xs:list itemType="t:hatsize"/></xs:simpleType>
              <xs:complexType name="weight">
                <xs:simpleContent>
                  <xs:extension base="xs:decimal"><xs:attribute name="unit" type="xs:string"/></xs:extension>
                </xs:simpleContent>
              </xs:complexType>
              <xs:complexType name="box">
                <xs:sequence><xs:element name="size" type="t:hatsize"/></xs:sequence>
              </xs:complexType>
              <xs:element name="order">
                <xs:complexType>
                  <xs:sequence>
                    <xs:element name="price" type="xs:decimal"/>
                    <xs:element name="rate" type="xs:float"/>
                    <xs:element name="flag" type="xs:boolean"/>
                    <xs:element name="tags" type="xs:NMTOKENS"/>
                    <xs:element name="note" type="xs:string"/>
                    <xs:element name="size" type="t:hatsize"/>
                    <xs:element name="sizes" type="t:hatsizes"/>
                    <xs:element name="weight" type="t:weight"/>
                    <xs:element name="box" type="t:box"/>
                    <xs:element name="code" type="xs:NCName"/>
                    <xs:element name="grade">
                      <xs:simpleType><xs:restriction base="xs:integer"><xs:maxInclusive value="9"/></xs:restriction></xs:simpleType>
                    </xs:element>
                  </xs:sequence>
                  <xs:attribute ref="t:qty"/>
                </xs:complexType>
              </xs:element>
            </xs:schema>
            """);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { }
    }

    private async Task<string> Run(string body, string setters = "")
    {
        var queryBaseUri = new System.Uri(Path.Combine(_tempDir, "test.xq"));
        var result = await _facade.EvaluateAsync(setters + Prolog + body, inputXml: null, baseUri: null,
            queryBaseUri: queryBaseUri);
        return result.Trim();
    }

    [Fact]
    public async Task Data_ReturnsTheAnnotatedType()
    {
        (await Run("""
            string-join((
              data($valid/t:price) instance of xs:decimal,
              data($valid/t:rate) instance of xs:float,
              data($valid/t:flag) instance of xs:boolean,
              data($valid/t:note) instance of xs:string,
              data($valid/@t:qty) instance of xs:integer) ! string(), ' ')
            """)).Should().Be("true true true true true");
    }

    [Fact]
    public async Task Data_OfAnUnvalidatedNode_StaysUntypedAtomic()
    {
        (await Run("""
            string-join((
              data($raw/t:price) instance of xs:untypedAtomic,
              data($raw/@t:qty) instance of xs:untypedAtomic) ! string(), ' ')
            """)).Should().Be("true true");
    }

    [Fact]
    public async Task ImplicitAtomization_UsesTheTypedValue()
    {
        // Untyped operands are cast to xs:double; typed ones keep their own type.
        (await Run("""
            string-join((
              ($valid/t:rate + $valid/t:rate) instance of xs:float,
              ($raw/t:rate + $raw/t:rate) instance of xs:double,
              ($valid/@t:qty * 2) instance of xs:integer,
              $valid/t:flag = true()) ! string(), ' ')
            """)).Should().Be("true true true true");
    }

    [Fact]
    public async Task ListType_AtomizesToOneItemPerToken()
    {
        (await Run("string-join((count(data($valid/t:tags)), count(data($raw/t:tags))) ! string(), ' ')"))
            .Should().Be("3 1");
    }

    [Fact]
    public async Task String_IsTheStringValue_NotTheTypedValuesForm()
    {
        // xs:decimal("-0.0") is 0; the node's string value is still what the document holds.
        (await Run("string-join((string($valid/t:price), $valid/t:price/string(), string(data($valid/t:price))), ' ')"))
            .Should().Be("-0.0 -0.0 0");
    }

    // ── Schema-defined types ────────────────────────────────────────────────────────────────
    // Their typed value needs the schema, which atomization cannot reach; the provider records
    // how each is built when it annotates the tree (QT3 validate-sc-1, validateexpr-sc-8/9,
    // fn-data-1, cbcl-data-003/005).

    [Fact]
    public async Task DerivedAtomicType_AtomizesAsItsBuiltInBase()
    {
        (await Run("""
            string-join((
              data($valid/t:size) instance of xs:integer,
              $valid/t:size + 1,
              data($raw/t:size) instance of xs:untypedAtomic) ! string(), ' ')
            """)).Should().Be("true 8 true");
    }

    [Fact]
    public async Task SchemaDefinedListType_AtomizesToTypedItems()
    {
        (await Run("""
            string-join((
              count(data($valid/t:sizes)),
              every $i in data($valid/t:sizes) satisfies $i instance of xs:integer,
              sum(data($valid/t:sizes))) ! string(), ' ')
            """)).Should().Be("3 true 24");
    }

    [Fact]
    public async Task ComplexTypeWithSimpleContent_AtomizesAsItsContentType()
    {
        (await Run("data($valid/t:weight) instance of xs:decimal")).Should().Be("true");
    }

    [Fact]
    public async Task ElementOnlyContent_HasNoTypedValue()
    {
        var act = () => Run("data($valid/t:box)");
        (await act.Should().ThrowAsync<System.Exception>()).Which.Message.Should().Contain("element-only content");
        // The string value is unaffected, and so is an unvalidated element of the same shape.
        (await Run("string-join((string($valid/t:box), string(data($raw/t:box))), ' ')")).Should().Be("8 8");
        // The zero-argument forms are defined on fn:string(.), so they read it too.
        (await Run("string-join(($valid/t:box/string-length(), $valid/t:box/normalize-space()) ! string(), ' ')"))
            .Should().Be("1 8");
    }

    // ── Type tests against schema-defined and anonymous types ───────────────────────────────
    // A validated node's annotation named its type's namespace by the node store's id, while
    // element(*, T) names it by the provider's: the two never compared equal, so no node was
    // ever an instance of a schema-defined type. And a node of an anonymous type had no
    // annotation at all (QT3 fn-nilled-40/49/53, cbcl-schema-element-1/2).

    [Fact]
    public async Task InstanceOf_ElementWithASchemaDefinedType()
    {
        (await Run("""
            string-join((
              $valid/t:size instance of element(*, t:hatsize),
              $valid/t:size instance of element(*, xs:integer),
              $valid/t:size instance of element(*, xs:string),
              $raw/t:size instance of element(*, t:hatsize)) ! string(), ' ')
            """)).Should().Be("true true false false");
    }

    [Fact]
    public async Task AnonymousType_IsAnnotatedAndTyped()
    {
        (await Run("""
            string-join((
              data($valid/t:grade) instance of xs:integer,
              $valid/t:grade instance of element(*, xs:integer),
              $valid/t:grade instance of element(*, xs:untyped),
              $raw/t:grade instance of element(*, xs:untyped)) ! string(), ' ')
            """)).Should().Be("true true false true");
    }

    [Fact]
    public async Task DocumentConstructor_StripsAnnotationsFromItsCopies()
    {
        // Under construction mode strip the copy is xs:untyped: one untyped value, not three
        // integers. The document constructor kept the annotation where the element constructor
        // stripped it (QT3 Constr-docnode-constrmod-1).
        const string query = "count(data((document { $valid/t:sizes })/*))";
        (await Run(query, "declare construction strip; ")).Should().Be("1");
        (await Run(query, "declare construction preserve; ")).Should().Be("3");
    }

    [Fact]
    public async Task ComputedProcessingInstruction_AcceptsAStringSubtypeName()
    {
        // The name comes from a validated xs:NCName element, whose typed value is a string subtype.
        (await Run("processing-instruction { $valid/t:code } { 'x' }")).Should().Be("<?abc x?>");
    }
}
