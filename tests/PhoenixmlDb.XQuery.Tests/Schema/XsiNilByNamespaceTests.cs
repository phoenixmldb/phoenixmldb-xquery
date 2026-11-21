using AwesomeAssertions;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Schema;

/// <summary>
/// xsi:nil is an attribute in the XSI namespace, whatever its prefix (XML Schema §2.6.2). The
/// schema provider and the nilled check also took any attribute named nil with the prefix xsi as
/// xsi:nil, so <c>&lt;e xmlns:xsi="urn:fake" xsi:nil="true"&gt;5&lt;/e&gt;</c>, valid with the
/// attribute as an ordinary one, was made nilled and its typed value was silently empty.
/// </summary>
public class XsiNilByNamespaceTests
{
    private const string XsiUri = "http://www.w3.org/2001/XMLSchema-instance";

    private const string Xsd = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
          <xs:element name="e" nillable="true">
            <xs:complexType>
              <xs:simpleContent>
                <xs:extension base="xs:integer"><xs:anyAttribute processContents="lax"/></xs:extension>
              </xs:simpleContent>
            </xs:complexType>
          </xs:element>
        </xs:schema>
        """;

    private static async Task<string> Eval(string document, string query)
    {
        var schemas = new XsdSchemaProvider();
        schemas.AddFromString("", Xsd);
        var store = new XdmDocumentStore();
        var doc = schemas.ValidateAndAnnotate(document, store, ValidationMode.Strict);
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store, schemaProvider: schemas);
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors));
        var items = new List<object?>();
        await foreach (var i in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext(initialContextItem: doc)))
            items.Add(i);
        return string.Join(",", items.Select(i => i?.ToString() ?? ""));
    }

    private const string Query = "nilled(/e), count(data(/e)), string-join(data(/e) ! string(), '')";

    [Fact]
    public async Task A_prefix_xsi_bound_to_another_namespace_is_not_xsi_nil()
        => (await Eval("""<e xmlns:xsi="urn:fake" xsi:nil="true">5</e>""", Query)).Should().Be("False,1,5");

    [Fact]
    public async Task Xsi_nil_under_another_prefix_is_xsi_nil()
        => (await Eval($"""<e xmlns:i="{XsiUri}" i:nil="true"/>""", Query)).Should().Be("True,0,");

    [Fact]
    public async Task Xsi_nil_under_the_usual_prefix_is_xsi_nil()
        => (await Eval($"""<e xmlns:xsi="{XsiUri}" xsi:nil="true"/>""", Query)).Should().Be("True,0,");

    // The attribute walk of TypeCastHelper.IsNilled, for an element the validator's nilled table
    // does not hold: it reads the attribute's namespace through the attribute's own store.

    private static (XdmElement Element, XdmDocumentStore Store) Typed(string nilNamespace, string prefix)
    {
        var store = new XdmDocumentStore();
        INodeBuilder builder = store;
        var doc = builder.AllocateDocumentId();
        var elemId = builder.AllocateId();
        var attrId = builder.AllocateId();
        var attr = new XdmAttribute
        {
            Id = attrId, Document = doc, Parent = elemId, LocalName = "nil", Prefix = prefix,
            Namespace = builder.InternNamespace(nilNamespace), Value = "true",
        };
        var elem = new XdmElement
        {
            Id = elemId, Document = doc, LocalName = "e", Namespace = NamespaceId.None,
            Attributes = [attrId], Children = [], NamespaceDeclarations = [],
            TypeAnnotation = new Xdm.XdmTypeName(NamespaceId.None, "anonymous"),
        };
        store.RegisterNode(attr);
        store.RegisterNode(elem);
        return (elem, store);
    }

    [Fact]
    public void The_attribute_walk_ignores_a_prefix_xsi_in_another_namespace()
    {
        var (elem, store) = Typed("urn:fake", "xsi");
        TypeCastHelper.IsNilled(elem, id => ((INodeProvider)store).GetNode(id)).Should().BeFalse();
    }

    [Fact]
    public void The_attribute_walk_finds_xsi_nil_under_another_prefix()
    {
        var (elem, store) = Typed(XsiUri, "i");
        TypeCastHelper.IsNilled(elem, id => ((INodeProvider)store).GetNode(id)).Should().BeTrue();
    }
}
