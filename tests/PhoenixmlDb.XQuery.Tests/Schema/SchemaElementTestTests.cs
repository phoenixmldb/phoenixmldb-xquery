using System.IO;
using System.Threading.Tasks;
using AwesomeAssertions;
using PhoenixmlDb.XQuery;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Schema;

/// <summary>
/// <c>schema-element(N)</c> and <c>schema-attribute(N)</c> against a schema with a target
/// namespace. A prefixed name was looked up in no namespace, so every such test failed XPST0008
/// for a declaration that exists; as the node test of a step it matched nothing at all; and as a
/// sequence type the declaration's namespace was dropped before the provider saw it
/// (QT3 cbcl-schema-element-3..8, cbcl-schema-attribute-1/2).
/// </summary>
public sealed class SchemaElementTestTests : System.IDisposable
{
    private const string Prolog = """
        import schema namespace t = 'urn:t' at 'schema.xsd';
        declare default element namespace 'urn:t';
        declare variable $raw := <t:root t:a="7"><t:head>h</t:head><t:member>m</t:member><t:deep>d</t:deep><t:other>o</t:other></t:root>;
        declare variable $valid := validate strict { $raw };

        """;

    private readonly string _tempDir;
    private readonly XQueryFacade _facade = new();

    public SchemaElementTestTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"phoenixmldb-schema-element-{System.Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "schema.xsd"), """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:t="urn:t"
                       targetNamespace="urn:t" elementFormDefault="qualified">
              <xs:element name="head" type="xs:string"/>
              <xs:element name="member" type="xs:string" substitutionGroup="t:head"/>
              <xs:element name="deep" type="xs:string" substitutionGroup="t:member"/>
              <xs:element name="other" type="xs:string"/>
              <xs:attribute name="a" type="xs:integer"/>
              <xs:element name="root">
                <xs:complexType>
                  <xs:sequence>
                    <xs:element ref="t:head" maxOccurs="unbounded"/>
                    <xs:element ref="t:other"/>
                  </xs:sequence>
                  <xs:attribute ref="t:a"/>
                </xs:complexType>
              </xs:element>
            </xs:schema>
            """);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { }
    }

    private async Task<string> Run(string body)
    {
        var queryBaseUri = new System.Uri(Path.Combine(_tempDir, "test.xq"));
        var result = await _facade.EvaluateAsync(Prolog + body, inputXml: null, baseUri: null,
            queryBaseUri: queryBaseUri);
        return result.Trim();
    }

    [Fact]
    public async Task Step_SelectsTheDeclarationAndItsSubstitutionGroup()
    {
        // t:deep substitutes for t:member, which substitutes for t:head; t:other does neither.
        (await Run("string-join($valid/schema-element(t:head) ! local-name(), ' ')"))
            .Should().Be("head member deep");
    }

    [Fact]
    public async Task Step_SelectsNothingFromAnUnvalidatedTree()
    {
        // Same names, but xs:untyped is not derived from the declared type.
        (await Run("count($raw/schema-element(t:head))")).Should().Be("0");
    }

    [Fact]
    public async Task InstanceOf_NeedsTheNameAndTheTypeAnnotation()
    {
        (await Run("""
            string-join((
              $valid/t:head instance of schema-element(t:head),
              $raw/t:head instance of schema-element(t:head),
              $valid/t:member instance of schema-element(t:head),
              $valid/t:other instance of schema-element(t:head)) ! string(), ' ')
            """)).Should().Be("true false true false");
    }

    [Fact]
    public async Task AnonymousDeclaredType_MatchesAValidatedElement()
    {
        // t:root's type has no name of its own; the provider gives it one, which only a
        // validated element carries.
        (await Run("string-join(($valid instance of schema-element(t:root), $raw instance of schema-element(t:root)) ! string(), ' ')"))
            .Should().Be("true false");
    }

    [Fact]
    public async Task SchemaAttribute_AsAStep_UsesTheAttributeAxis()
    {
        (await Run("string-join((count($valid/schema-attribute(t:a)), count($raw/schema-attribute(t:a))) ! string(), ' ')"))
            .Should().Be("1 0");
    }

    [Fact]
    public async Task UnprefixedName_TakesTheDefaultElementNamespace()
    {
        (await Run("count($valid/schema-element(member))")).Should().Be("2");
    }

    /// <summary>
    /// document-node(schema-element(N)) tests the document element as schema-element(N) does.
    /// The inner test was not applied, so every document matched, and document-node(element(N))
    /// compared the local name only.
    /// </summary>
    [Fact]
    public async Task DocumentNode_TestsItsElementAsAnItem()
    {
        (await Run("""
            let $doc := validate strict { document { $raw } }
            let $other := document { <x:root xmlns:x="urn:other"/> }
            let $untyped := document { $raw }
            return string-join((
              $doc instance of document-node(schema-element(t:root)),
              $other instance of document-node(schema-element(t:root)),
              $untyped instance of document-node(schema-element(t:root)),
              $doc instance of document-node(element(t:root)),
              $other instance of document-node(element(t:root)),
              $doc instance of document-node(element(t:root, xs:string))) ! string(), ' ')
            """)).Should().Be("true false false true false false");
    }

    /// <summary>
    /// A prefix the host binds (CompilationOptions.StaticNamespaces) names a declaration in
    /// schema-element(): the name is resolved while the query is parsed, and the host's
    /// prefixes were not known then, so t:root was looked for in no namespace (XPST0008).
    /// </summary>
    [Fact]
    public async Task A_prefix_the_host_binds_names_the_declaration()
    {
        var store = new XdmDocumentStore();
        var engine = new PhoenixmlDb.XQuery.Execution.QueryEngine(nodeProvider: store, documentResolver: store,
            schemaProvider: new XsdSchemaProvider());
        var schema = new System.Uri(Path.Combine(_tempDir, "schema.xsd")).AbsoluteUri;
        var compiled = engine.Compile($"""
            import schema "urn:t" at "{schema}";
            let $d := validate strict {"{"} parse-xml("<root xmlns='urn:t'><head>h</head><other>o</other></root>") {"}"}
            return string-join(($d/*:root instance of schema-element(t:root), $d instance of document-node(schema-element(t:root))) ! string(), ' ')
            """, new PhoenixmlDb.XQuery.Execution.CompilationOptions
        {
            StaticNamespaces = new Dictionary<string, string> { ["t"] = "urn:t" },
        });
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors.Select(e => e.Message)));
        var items = new List<object?>();
        await foreach (var item in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
            items.Add(item);
        items.Should().Equal("true true");
    }

    /// <summary>
    /// A member type derives from a union only when the union is pure (XPath 3.1 §2.5.2,
    /// §2.5.6.1): a union of list types is not. An element of one of its list members matched
    /// element(*, U) for U the union of two (W3C XSLT match-197); a member of a pure union still
    /// does.
    /// </summary>
    [Fact]
    public async Task A_member_derives_only_from_a_pure_union()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "unions.xsd"), """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:u="urn:u"
                       targetNamespace="urn:u" elementFormDefault="qualified">
              <xs:simpleType name="ints"><xs:list itemType="xs:integer"/></xs:simpleType>
              <xs:simpleType name="dates"><xs:list itemType="xs:date"/></xs:simpleType>
              <xs:simpleType name="lists"><xs:union memberTypes="u:ints u:dates"/></xs:simpleType>
              <xs:simpleType name="small"><xs:restriction base="xs:integer"><xs:maxInclusive value="9"/></xs:restriction></xs:simpleType>
              <xs:simpleType name="atoms"><xs:union memberTypes="u:small xs:date"/></xs:simpleType>
              <xs:element name="i" type="u:ints"/>
              <xs:element name="s" type="u:small"/>
            </xs:schema>
            """);
        var queryBaseUri = new System.Uri(Path.Combine(_tempDir, "test.xq"));
        var result = await _facade.EvaluateAsync("""
            import schema namespace u = 'urn:u' at 'unions.xsd';
            string-join((
              validate strict { <u:i>1 2</u:i> } instance of element(*, u:lists),
              validate strict { <u:i>1 2</u:i> } instance of element(*, u:ints),
              validate strict { <u:s>3</u:s> } instance of element(*, u:atoms)) ! string(), ' ')
            """, inputXml: null, baseUri: null, queryBaseUri: queryBaseUri);
        result.Trim().Should().Be("false true true");
    }

    /// <summary>
    /// An untyped element does not match schema-element(N), nor an untyped attribute
    /// schema-attribute(N), even where N is declared with xs:anyType, from which xs:untyped
    /// derives (spec bug 10065). They matched such a declaration (W3C XSLT validation-1001).
    /// </summary>
    [Fact]
    public async Task An_untyped_node_does_not_match_a_declaration_of_any_type()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "any.xsd"), """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" targetNamespace="urn:any" elementFormDefault="qualified">
              <xs:element name="free"/>
              <xs:attribute name="loose" type="xs:anySimpleType"/>
            </xs:schema>
            """);
        var result = await _facade.EvaluateAsync("""
            import schema namespace a = 'urn:any' at 'any.xsd';
            let $raw := <a:free a:loose="1"/>
            let $valid := validate strict { $raw }
            return string-join(($raw instance of schema-element(a:free), $valid instance of schema-element(a:free),
              $raw/@a:loose instance of schema-attribute(a:loose)) ! string(), ' ')
            """, inputXml: null, baseUri: null, queryBaseUri: new System.Uri(Path.Combine(_tempDir, "test.xq")));
        result.Trim().Should().Be("false true false");
    }

    /// <summary>
    /// As the node test of a step, document-node(element(N)) tests the document element and
    /// element(*, T) the type annotation. The inner test of document-node() was not applied, so
    /// the step selected every document (W3C XSLT validation-1401), and element(*, T) selected
    /// only untyped elements.
    /// </summary>
    [Fact]
    public async Task Kind_test_steps_test_the_document_element_and_the_type()
        => (await Run("""
            let $d := validate strict { document { $raw } }
            return string-join((
              count($d/self::document-node(element(t:root))), count($d/self::document-node(element(t:other))),
              count($d/self::document-node(schema-element(t:root))), count($valid/element(*, xs:string)),
              count($raw/element(*, xs:string))) ! string(), ' ')
            """)).Should().Be("1 0 1 4 0");

    /// <summary>
    /// An element in several substitution groups (XSD 1.1) is a member of each, through an
    /// abstract head too; an abstract element is a member of none; and a nilled element matches
    /// only where the declaration of its name is nillable (QT3 substitution-020..025). The schema
    /// did not load.
    /// </summary>
    [Fact]
    public async Task An_element_in_several_substitution_groups_is_a_member_of_each()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "multi.xsd"), """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:m="urn:m" targetNamespace="urn:m" elementFormDefault="qualified">
              <xs:element name="h1" type="xs:string" nillable="true"/>
              <xs:element name="h2" type="xs:string" nillable="true"/>
              <xs:element name="a" abstract="true" substitutionGroup="m:h1 m:h2"/>
              <xs:element name="b" substitutionGroup="m:a"/>
              <xs:element name="c" substitutionGroup="m:a" nillable="false"/>
              <xs:element name="box">
                <xs:complexType><xs:choice>
                  <xs:element name="a" type="xs:string"/>
                  <xs:element name="b" type="xs:string"/>
                  <xs:element name="c" type="xs:string" nillable="true"/>
                </xs:choice></xs:complexType>
              </xs:element>
            </xs:schema>
            """);
        var result = await _facade.EvaluateAsync("""
            import schema namespace m = 'urn:m' at 'multi.xsd';
            let $a := validate strict { <m:box><m:a/></m:box> }/m:a
            let $b := validate strict { <m:box><m:b/></m:box> }/m:b
            let $c := validate strict { <m:box><m:c xsi:nil="true" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"/></m:box> }/m:c
            return string-join(for $e in ($a, $b, $c) return
              ($e instance of schema-element(m:h1)) || '/' || ($e instance of schema-element(m:h2)), ' ')
            """,
            inputXml: null, baseUri: null, queryBaseUri: new System.Uri(Path.Combine(_tempDir, "test.xq")));
        result.Trim().Should().Be("false/false true/true false/false");
    }

    [Fact]
    public async Task UndeclaredName_IsStillAStaticError()
    {
        var act = () => Run("count($valid/schema-element(t:nosuch))");
        (await act.Should().ThrowAsync<System.Exception>()).Which.Message.Should().Contain("not found in in-scope schema definitions");
    }
}
