using FluentAssertions;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Schema;

/// <summary>
/// Verifies the DEFAULT interface implementation of <see cref="ISchemaProvider.ValidateXml"/>
/// (and its fragment counterpart). A provider that implements only the node-based
/// <see cref="ISchemaProvider.Validate(XdmNode, ValidationMode, string?, string?)"/> must get a
/// working string overload for free: the default parses the content into an XDM document and
/// delegates. It must NOT throw <see cref="System.NotSupportedException"/>.
/// </summary>
public class DefaultValidateXmlTests
{
    /// <summary>
    /// Minimal provider that implements ONLY the node-based Validate. Every other member is a
    /// stub. Validate rejects a document whose root element is named <c>invalid</c>, otherwise
    /// records the node it received and returns it unchanged. This lets us assert the string
    /// overload (a) reaches Validate at all and (b) surfaces its SchemaValidationException.
    /// </summary>
    private sealed class NodeOnlySchemaProvider : ISchemaProvider
    {
        private readonly bool _rejectEverything;

        public NodeOnlySchemaProvider(bool rejectEverything = false)
            => _rejectEverything = rejectEverything;

        public XdmNode? LastValidated { get; private set; }

        public XdmNode Validate(XdmNode node, ValidationMode mode,
            string? typeNamespaceUri = null, string? typeLocalName = null)
        {
            LastValidated = node;
            if (_rejectEverything)
                throw new SchemaValidationException("XQDY0027", "Validation failed: rejected by provider");
            return node;
        }

        // --- Stubs for the rest of the interface (not exercised by these tests) ---
        public void ImportSchema(string targetNamespace, IReadOnlyList<string>? locationHints = null) { }
        public bool IsSubtypeOf(XdmTypeName actualType, XdmTypeName requiredType) => false;
        public bool HasElementDeclaration(XdmQName name) => false;
        public bool HasAttributeDeclaration(XdmQName name) => false;
        public XdmTypeName? GetElementType(XdmQName name) => null;
        public XdmTypeName? GetAttributeType(XdmQName name) => null;
        public bool MatchesSchemaElement(XdmElement element, XdmQName declarationName) => false;
        public bool MatchesSchemaAttribute(XdmAttribute attribute, XdmQName declarationName) => false;
    }

    [Fact]
    public void ValidateXml_default_parses_and_delegates_to_node_Validate()
    {
        var provider = new NodeOnlySchemaProvider();
        ISchemaProvider iface = provider;

        // A well-formed, "valid" document must pass without throwing.
        var act = () => iface.ValidateXml("<ok><child/></ok>", ValidationMode.Strict);

        act.Should().NotThrow();
        provider.LastValidated.Should().NotBeNull("the default must parse the string and hand an XDM node to Validate");
    }

    [Fact]
    public void ValidateXml_default_surfaces_validation_failure_not_NotSupported()
    {
        ISchemaProvider provider = new NodeOnlySchemaProvider(rejectEverything: true);

        var act = () => provider.ValidateXml("<invalid/>", ValidationMode.Strict);

        act.Should().Throw<SchemaValidationException>()
            .Which.ErrorCode.Should().Be("XQDY0027");
    }

    [Fact]
    public void ValidateXml_default_maps_malformed_xml_to_XQDY0027()
    {
        ISchemaProvider provider = new NodeOnlySchemaProvider();

        var act = () => provider.ValidateXml("<not-well-formed", ValidationMode.Strict);

        act.Should().Throw<SchemaValidationException>()
            .Which.ErrorCode.Should().Be("XQDY0027");
    }

    [Fact]
    public void ValidateXmlFragment_default_delegates_through_ValidateXml()
    {
        ISchemaProvider provider = new NodeOnlySchemaProvider(rejectEverything: true);

        var act = () => provider.ValidateXmlFragment("<invalid/>", ValidationMode.Strict);

        act.Should().Throw<SchemaValidationException>()
            .Which.ErrorCode.Should().Be("XQDY0027");
    }
}
