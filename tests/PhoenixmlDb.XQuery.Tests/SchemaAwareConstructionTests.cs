using System.Xml.Schema;
using FluentAssertions;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

public sealed class SchemaAwareConstructionTests
{
    private const string SimpleSchema = """
        <?xml version="1.0"?>
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"
                   targetNamespace="http://test.example/types"
                   xmlns="http://test.example/types"
                   elementFormDefault="qualified">
          <xs:element name="amount">
            <xs:complexType>
              <xs:attribute name="value" type="xs:decimal"/>
            </xs:complexType>
          </xs:element>
        </xs:schema>
        """;

    private const string SimpleSource = """
        <amount xmlns="http://test.example/types" value="42.5"/>
        """;

    // ── helper: build a schema-validated store + root element ──────────────────

    private static (XdmDocumentStore store, XdmElement root, XsdSchemaProvider schemaProvider)
        LoadValidatedDoc()
    {
        var schemaProvider = new XsdSchemaProvider();
        schemaProvider.AddFromString("http://test.example/types", SimpleSchema);

        var schemaSet = new XmlSchemaSet();
        using (var schemaReader = new System.IO.StringReader(SimpleSchema))
        using (var xmlReader = System.Xml.XmlReader.Create(schemaReader))
        {
            schemaSet.Add(null, xmlReader);
        }
        schemaSet.Compile();

        var store = new XdmDocumentStore();
        var doc = store.LoadFromStringWithSchema(SimpleSource, "test://source", schemaSet);

        var root = doc.Children
            .Select(id => store.GetNode(id))
            .OfType<XdmElement>()
            .Single();

        return (store, root, schemaProvider);
    }

    // ── helper: compile + run a query with an XdmElement as context item ──────

    private static async System.Threading.Tasks.Task<object?> EvaluateScalarAsync(
        string query,
        XdmDocumentStore store,
        XsdSchemaProvider schemaProvider,
        XdmElement contextItem)
    {
        var engine = new QueryEngine(
            nodeProvider: store,
            documentResolver: store,
            schemaProvider: schemaProvider);

        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue(
            string.Join("; ", compiled.Errors.Select(e => e.Message)));

        using var ctx = engine.CreateContext(initialContextItem: contextItem);
        await foreach (var item in compiled.ExecutionPlan!.ExecuteAsync(ctx))
            return item;
        return null;
    }

    // ── Task 0 baseline ────────────────────────────────────────────────────────

    [Fact]
    public void SchemaValidatedLoad_FillsAttributeTypeAnnotation()
    {
        var schemaSet = new XmlSchemaSet();
        using (var schemaReader = new System.IO.StringReader(SimpleSchema))
        using (var xmlReader = System.Xml.XmlReader.Create(schemaReader))
        {
            schemaSet.Add(null, xmlReader);
        }
        schemaSet.Compile();

        var store = new XdmDocumentStore();
        var doc = store.LoadFromStringWithSchema(SimpleSource, "test://source", schemaSet);

        var root = doc.Children
            .Select(id => store.GetNode(id))
            .OfType<XdmElement>()
            .Single();

        var attr = root.Attributes
            .Select(id => store.GetNode(id))
            .OfType<XdmAttribute>()
            .Single();

        attr.LocalName.Should().Be("value");
        attr.Value.Should().Be("42.5");

        // The whole point of this plan: schema validation must populate TypeAnnotation.
        // If this fails, the bug is in PhoenixmlDb.Core's parser, not the XQuery engine.
        // XdmTypeName.XsDecimal is the static property for xs:decimal.
        attr.TypeAnnotation.Should().Be(XdmTypeName.XsDecimal,
            "schema validation should annotate value='42.5' as xs:decimal per the XSD definition");
    }

    // ── Task 1: failing tests for Constr-cont-constrmod-9/10 semantics ────────
    //
    // Both tests will FAIL today because DeepCopyNode (Task 2) and construction-mode
    // propagation (Tasks 3/4) are not yet implemented.  The attribute's TypeAnnotation
    // is silently dropped when it is embedded into a constructed element, so
    //   instance of attribute(*, xs:decimal)
    // returns false instead of true.

    /// <summary>
    /// Mirrors QT3 Constr-cont-constrmod-9:
    ///   declare construction preserve;
    ///   &lt;elem&gt;{//*:amount/@*:value}&lt;/elem&gt;/@*:value instance of attribute(*, xs:decimal)
    ///
    /// The attribute is extracted from the schema-validated source and embedded
    /// directly into a constructed element.  With construction=preserve the type
    /// annotation must survive — expect true.
    /// </summary>
    [Fact]
    public async System.Threading.Tasks.Task
        ConstructionPreserve_AttributeDirectlyWrappedInConstructor_RetainsDecimalTypeAnnotation()
    {
        // Mirrors QT3 Constr-cont-constrmod-9
        var (store, root, schemaProvider) = LoadValidatedDoc();

        // The context item (.) is the schema-validated <amount> element.
        // We navigate to its @value attribute, wrap it in a fresh <elem>,
        // then ask whether the copy is still typed as xs:decimal.
        var query = """
            declare namespace t = "http://test.example/types";
            declare construction preserve;
            <elem>{./@*:value}</elem>/@*:value instance of attribute(*, xs:decimal)
            """;

        var result = await EvaluateScalarAsync(query, store, schemaProvider, root);

        // EXPECTED FAILURE until Task 2 (DeepCopyNode) + Task 3/4 are done.
        // When fixed this becomes true; for now it is false, which is what the
        // test asserts to lock in the "currently broken" state.
        result.Should().Be(true,
            "construction=preserve must retain the xs:decimal type annotation on @value " +
            "when the attribute is copied into a constructed element (QT3 Constr-cont-constrmod-9)");
    }

    /// <summary>
    /// Mirrors QT3 Constr-cont-constrmod-10:
    ///   declare construction preserve;
    ///   &lt;elem&gt;{//*:amount}&lt;/elem&gt;/*/@*:value instance of attribute(*, xs:decimal)
    ///
    /// The whole parent element is embedded.  The attribute reached via the
    /// parent copy must also retain its type annotation.
    /// </summary>
    [Fact]
    public async System.Threading.Tasks.Task
        ConstructionPreserve_ElementWithTypedAttrWrappedInConstructor_AttrRetainsDecimalTypeAnnotation()
    {
        // Mirrors QT3 Constr-cont-constrmod-10
        var (store, root, schemaProvider) = LoadValidatedDoc();

        // The context item (.) is the schema-validated <amount> element.
        // We wrap the element itself in <elem>; then navigate to its @value child.
        var query = """
            declare namespace t = "http://test.example/types";
            declare construction preserve;
            <elem>{.}</elem>/t:amount/@*:value instance of attribute(*, xs:decimal)
            """;

        var result = await EvaluateScalarAsync(query, store, schemaProvider, root);

        // EXPECTED FAILURE until Task 2 (DeepCopyNode) + Task 3/4 are done.
        result.Should().Be(true,
            "construction=preserve must retain the xs:decimal type annotation on @value " +
            "when the attribute is reached through a copied parent element (QT3 Constr-cont-constrmod-10)");
    }

    // ── Task 5: XQTY0086 for QName-typed attributes in construction=preserve ──
    //
    // QT3 Constr-cont-nsmode-7/8/10: when an attribute with xs:QName type annotation
    // is copied into element content under construction=preserve, the engine must
    // raise XQTY0086 — the QName's prefix may refer to a namespace that is not in
    // scope in the new construction context.

    private const string QNameAttrSchema = """
        <?xml version="1.0"?>
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"
                   targetNamespace="http://www.w3.org/XQueryTest"
                   xmlns:atomic="http://www.w3.org/XQueryTest"
                   elementFormDefault="qualified"
                   attributeFormDefault="qualified">
          <xs:element name="QName">
            <xs:complexType>
              <xs:simpleContent>
                <xs:extension base="xs:QName">
                  <xs:attribute ref="atomic:attr" use="required"/>
                </xs:extension>
              </xs:simpleContent>
            </xs:complexType>
          </xs:element>
          <xs:attribute name="attr" type="xs:QName"/>
        </xs:schema>
        """;

    // The document uses the namespace prefix "foo" as part of the QName value.
    private const string QNameAttrSource = """
        <atomic:QName xmlns:atomic="http://www.w3.org/XQueryTest"
                      xmlns:foo="http://www.example.com/foo"
                      atomic:attr="foo:aQname">foo:aQname</atomic:QName>
        """;

    private static (XdmDocumentStore store, XdmElement root, XsdSchemaProvider schemaProvider)
        LoadQNameAttrDoc()
    {
        var schemaProvider = new XsdSchemaProvider();
        schemaProvider.AddFromString("http://www.w3.org/XQueryTest", QNameAttrSchema);

        var schemaSet = new XmlSchemaSet();
        using (var schemaReader = new System.IO.StringReader(QNameAttrSchema))
        using (var xmlReader = System.Xml.XmlReader.Create(schemaReader))
        {
            schemaSet.Add(null, xmlReader);
        }
        schemaSet.Compile();

        var store = new XdmDocumentStore();
        var doc = store.LoadFromStringWithSchema(QNameAttrSource, "test://qname-source", schemaSet);

        var root = doc.Children
            .Select(id => store.GetNode(id))
            .OfType<XdmElement>()
            .Single();

        return (store, root, schemaProvider);
    }

    /// <summary>
    /// Mirrors QT3 Constr-cont-nsmode-7 (no-preserve, inherit) and
    /// Constr-cont-nsmode-8 (preserve, inherit) and
    /// Constr-cont-nsmode-10 (default copy-namespaces):
    ///   declare construction preserve;
    ///   &lt;new&gt;{ (//atomic:QName/@atomic:attr)[1] }&lt;/new&gt;
    ///
    /// Copying a QName-typed attribute into element content under construction=preserve
    /// must raise XQTY0086 regardless of copy-namespaces setting.
    /// </summary>
    [Theory]
    [InlineData("declare copy-namespaces no-preserve, inherit;")]   // nsmode-7
    [InlineData("declare copy-namespaces preserve, inherit;")]       // nsmode-8
    [InlineData("")]                                                  // nsmode-10 (default)
    public async System.Threading.Tasks.Task
        ConstructionPreserve_QNameTypedAttribute_RaisesXQTY0086(string copyNsDecl)
    {
        var (store, root, schemaProvider) = LoadQNameAttrDoc();

        var query =
            "declare namespace atomic = \"http://www.w3.org/XQueryTest\";\n"
            + (string.IsNullOrEmpty(copyNsDecl) ? "" : copyNsDecl + "\n")
            + "declare construction preserve;\n"
            + "<new>{ ./@atomic:attr }</new>";

        var engine = new QueryEngine(
            nodeProvider: store,
            documentResolver: store,
            schemaProvider: schemaProvider);

        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue(
            string.Join("; ", compiled.Errors.Select(e => e.Message)));

        using var ctx = engine.CreateContext(initialContextItem: root);
        var act = async () =>
        {
            await foreach (var _ in compiled.ExecutionPlan!.ExecuteAsync(ctx))
            {
                // consume — must throw before yielding
            }
        };

        var ex = await act.Should().ThrowAsync<XQueryRuntimeException>(
            $"construction=preserve with QName-typed attribute (copy-namespaces: '{copyNsDecl}') must raise XQTY0086");
        ex.Which.ErrorCode.Should().Be("XQTY0086");
    }

    /// <summary>
    /// Mirrors QT3 K2-DirectConElemContent-35b:
    ///   &lt;e/&gt; instance of element(*, xs:untyped)
    ///
    /// Directly-constructed elements always have type annotation xs:untyped
    /// (XQuery 3.1 §3.9.1.1), regardless of construction mode.
    /// ConstructionMode (preserve/strip) only affects *copied* element subtrees.
    /// </summary>
    [Theory]
    [InlineData("")]                                  // default (preserve)
    [InlineData("declare construction preserve;")]    // explicit preserve
    [InlineData("declare construction strip;")]       // strip
    public async System.Threading.Tasks.Task
        DirectlyConstructedElement_TypeAnnotation_IsAlwaysXsUntyped(string constructionDecl)
    {
        // Element constructors require an INodeBuilder-backed store to return XdmElement
        // (without one they fall back to string serialization and instance-of checks fail).
        var store = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store);
        var query = (string.IsNullOrEmpty(constructionDecl) ? "" : constructionDecl + "\n")
                    + "<e/> instance of element(*, xs:untyped)";

        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue(
            string.Join("; ", compiled.Errors.Select(e => e.Message)));

        using var ctx = engine.CreateContext();
        object? result = null;
        await foreach (var item in compiled.ExecutionPlan!.ExecuteAsync(ctx))
            result = item;

        result.Should().Be(true,
            $"<e/> must be instance of element(*,xs:untyped) with construction mode '{constructionDecl}'");
    }

}
