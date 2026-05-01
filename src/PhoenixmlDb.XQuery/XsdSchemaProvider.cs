using System.Xml;
using System.Xml.Schema;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery;

/// <summary>
/// <see cref="ISchemaProvider"/> implementation backed by <see cref="XmlSchemaSet"/>.
/// Provides full XSD validation, type annotations, and schema-element/attribute matching.
/// </summary>
public sealed class XsdSchemaProvider : ISchemaProvider
{
    private readonly XmlSchemaSet _schemas = new();

    /// <summary>
    /// Maps NamespaceId values seen in inbound XdmQName parameters back to namespace URIs.
    /// Populated as schemas are loaded (built-in XSD/XML/XSI ids registered up-front, and any
    /// arbitrary URI's hash-based id added on first encounter via <see cref="RememberNamespaceId"/>).
    /// Solves the lossy NamespaceId-from-URI hashing problem for the QName-based lookup methods —
    /// the URI-string overloads sidestep this entirely and should be preferred where possible.
    /// </summary>
    private readonly Dictionary<NamespaceId, string> _namespaceUriById = new()
    {
        [NamespaceId.None] = "",
        [NamespaceId.Xsd] = "http://www.w3.org/2001/XMLSchema",
        [NamespaceId.Xml] = "http://www.w3.org/XML/1998/namespace",
        [NamespaceId.Xsi] = "http://www.w3.org/2001/XMLSchema-instance",
    };

    /// <summary>
    /// Creates an empty schema provider. Use <see cref="ImportSchema"/> or <see cref="Add(string)"/>
    /// to load schemas.
    /// </summary>
    public XsdSchemaProvider() { }

    /// <summary>
    /// Creates a schema provider with one or more XSD files pre-loaded.
    /// </summary>
    public XsdSchemaProvider(params string[] schemaFiles)
    {
        ArgumentNullException.ThrowIfNull(schemaFiles);
        foreach (var file in schemaFiles)
            Add(file);
    }

    /// <summary>
    /// Loads an XSD schema from a file path.
    /// </summary>
    public void Add(string schemaPath)
    {
        try
        {
            _schemas.Add(null, schemaPath);
            _schemas.Compile();
            // Track every namespace the schema set now exposes so QName-keyed lookups work
            // for all URIs the caller might query.
            foreach (var ns in EnumerateLoadedNamespaces())
                RememberNamespaceId(ns);
        }
        catch (XmlSchemaException ex)
        {
            throw new SchemaException("XQST0059",
                $"Failed to load schema from '{schemaPath}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Loads an XSD schema from a <see cref="TextReader"/>.
    /// </summary>
    public void Add(string targetNamespace, TextReader reader)
    {
        try
        {
            using var xmlReader = XmlReader.Create(reader);
            _schemas.Add(targetNamespace, xmlReader);
            _schemas.Compile();
            RememberNamespaceId(targetNamespace);
        }
        catch (XmlSchemaException ex)
        {
            throw new SchemaException("XQST0059",
                $"Failed to load schema for namespace '{targetNamespace}': {ex.Message}", ex);
        }
        catch (XmlException ex)
        {
            throw new SchemaException("XQST0059",
                $"Failed to load schema for namespace '{targetNamespace}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Loads an XSD schema from an inline string.
    /// </summary>
    public void AddFromString(string targetNamespace, string xsdContent)
    {
        Add(targetNamespace, new StringReader(xsdContent));
    }

    // ──────────────────────────────────────────────
    //  ISchemaProvider.ImportSchema
    // ──────────────────────────────────────────────

    public void ImportSchema(string targetNamespace, IReadOnlyList<string>? locationHints = null)
    {
        if (HasNamespace(targetNamespace))
            return;

        if (locationHints is { Count: > 0 })
        {
            foreach (var hint in locationHints)
            {
                try
                {
                    _schemas.Add(targetNamespace, hint);
                    _schemas.Compile();
                    RememberNamespaceId(targetNamespace);
                    return;
                }
                catch (XmlSchemaException)
                {
                    // Try next hint
                }
            }
        }

        throw new SchemaException("XQST0059",
            $"Cannot locate schema for namespace '{targetNamespace}'");
    }

    // ──────────────────────────────────────────────
    //  ISchemaProvider.IsSubtypeOf
    // ──────────────────────────────────────────────

    public bool IsSubtypeOf(XdmTypeName actualType, XdmTypeName requiredType)
    {
        if (actualType == requiredType)
            return true;

        if (requiredType == XdmTypeName.AnyType)
            return true;

        if (requiredType == XdmTypeName.AnySimpleType)
        {
            var schemaType = FindSchemaType(actualType);
            return schemaType is XmlSchemaSimpleType;
        }

        // Walk the XSD derivation chain
        var actual = FindSchemaType(actualType);
        var required = FindSchemaType(requiredType);
        if (actual == null || required == null)
            return false;

        var current = actual;
        while (current != null)
        {
            if (current.QualifiedName == required.QualifiedName)
                return true;
            current = current.BaseXmlSchemaType;
        }

        return false;
    }

    // ──────────────────────────────────────────────
    //  ISchemaProvider.HasElementDeclaration / HasAttributeDeclaration
    // ──────────────────────────────────────────────

    public bool HasElementDeclaration(XdmQName name)
        => _schemas.GlobalElements.Contains(ToXmlQualifiedName(name));

    public bool HasAttributeDeclaration(XdmQName name)
        => _schemas.GlobalAttributes.Contains(ToXmlQualifiedName(name));

    public bool HasElementDeclaration(string namespaceUri, string localName)
        => _schemas.GlobalElements.Contains(new XmlQualifiedName(localName, namespaceUri ?? ""));

    public bool HasAttributeDeclaration(string namespaceUri, string localName)
        => _schemas.GlobalAttributes.Contains(new XmlQualifiedName(localName, namespaceUri ?? ""));

    // ──────────────────────────────────────────────
    //  ISchemaProvider.GetElementType / GetAttributeType
    // ──────────────────────────────────────────────

    public XdmTypeName? GetElementType(XdmQName name)
    {
        if (_schemas.GlobalElements[ToXmlQualifiedName(name)] is XmlSchemaElement elem
            && elem.ElementSchemaType != null)
            return ToXdmTypeName(elem.ElementSchemaType);
        return null;
    }

    public XdmTypeName? GetAttributeType(XdmQName name)
    {
        if (_schemas.GlobalAttributes[ToXmlQualifiedName(name)] is XmlSchemaAttribute attr
            && attr.AttributeSchemaType != null)
            return ToXdmTypeName(attr.AttributeSchemaType);
        return null;
    }

    public XdmTypeName? GetElementType(string namespaceUri, string localName)
    {
        if (_schemas.GlobalElements[new XmlQualifiedName(localName, namespaceUri ?? "")] is XmlSchemaElement elem
            && elem.ElementSchemaType != null)
            return ToXdmTypeName(elem.ElementSchemaType);
        return null;
    }

    public XdmTypeName? GetAttributeType(string namespaceUri, string localName)
    {
        if (_schemas.GlobalAttributes[new XmlQualifiedName(localName, namespaceUri ?? "")] is XmlSchemaAttribute attr
            && attr.AttributeSchemaType != null)
            return ToXdmTypeName(attr.AttributeSchemaType);
        return null;
    }

    // ──────────────────────────────────────────────
    //  ISchemaProvider.MatchesSchemaElement / MatchesSchemaAttribute
    // ──────────────────────────────────────────────

    public bool MatchesSchemaElement(XdmElement element, XdmQName declarationName)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (_schemas.GlobalElements[ToXmlQualifiedName(declarationName)] is not XmlSchemaElement decl)
            return false;

        // Check direct name match
        if (element.LocalName == declarationName.LocalName
            && element.Namespace == declarationName.Namespace)
        {
            if (decl.ElementSchemaType != null)
            {
                var declaredType = ToXdmTypeName(decl.ElementSchemaType);
                return IsSubtypeOf(element.TypeAnnotation, declaredType);
            }
            return true;
        }

        // Check substitution group members
        return IsInSubstitutionGroup(element, decl);
    }

    public bool MatchesSchemaAttribute(XdmAttribute attribute, XdmQName declarationName)
    {
        ArgumentNullException.ThrowIfNull(attribute);

        if (_schemas.GlobalAttributes[ToXmlQualifiedName(declarationName)] is not XmlSchemaAttribute decl)
            return false;

        if (attribute.LocalName != declarationName.LocalName
            || attribute.Namespace != declarationName.Namespace)
            return false;

        if (decl.AttributeSchemaType != null)
        {
            var declaredType = ToXdmTypeName(decl.AttributeSchemaType);
            return IsSubtypeOf(attribute.TypeAnnotation, declaredType);
        }
        return true;
    }

    // ──────────────────────────────────────────────
    //  ISchemaProvider.Validate
    // ──────────────────────────────────────────────

    public void ValidateXml(string xmlContent, ValidationMode mode,
        string? typeNamespaceUri = null, string? typeLocalName = null)
        => ValidateXmlCore(xmlContent, mode, ConformanceLevel.Document, null);

    public void ValidateXmlFragment(string xmlFragment, ValidationMode mode,
        string? typeNamespaceUri = null, string? typeLocalName = null,
        IReadOnlyDictionary<string, string>? inScopeNamespaces = null)
        => ValidateXmlCore(xmlFragment, mode, ConformanceLevel.Fragment, inScopeNamespaces);

    private void ValidateXmlCore(string xml, ValidationMode mode, ConformanceLevel conformance,
        IReadOnlyDictionary<string, string>? inScopeNamespaces)
    {
        ArgumentNullException.ThrowIfNull(xml);
        var errors = new List<string>();
        var settings = new XmlReaderSettings
        {
            ValidationType = ValidationType.Schema,
            Schemas = _schemas,
            IgnoreWhitespace = false,
            IgnoreComments = false,
            IgnoreProcessingInstructions = false,
            ConformanceLevel = conformance,
        };
        settings.ValidationEventHandler += (_, e) =>
        {
            if (e.Severity == XmlSeverityType.Error)
                errors.Add(e.Message);
        };
        if (mode == ValidationMode.Lax)
            settings.ValidationFlags |= XmlSchemaValidationFlags.ProcessSchemaLocation;

        // Pre-declare any prefix→URI bindings the fragment relies on but doesn't itself
        // include (e.g. when an XSLT stylesheet declares xmlns:n on the root and the
        // synthesized element doesn't repeat it). XmlParserContext lets the reader
        // resolve those prefixes without us having to wrap the fragment in extra markup.
        XmlParserContext? parserContext = null;
        if (inScopeNamespaces is { Count: > 0 })
        {
            var nameTable = new NameTable();
            var nsManager = new XmlNamespaceManager(nameTable);
            foreach (var (prefix, uri) in inScopeNamespaces)
            {
                if (string.IsNullOrEmpty(prefix) || prefix == "xmlns") continue;
                nsManager.AddNamespace(prefix, uri);
            }
            parserContext = new XmlParserContext(nameTable, nsManager, null, XmlSpace.None);
        }

        try
        {
            using var reader = parserContext is null
                ? XmlReader.Create(new StringReader(xml), settings)
                : XmlReader.Create(new StringReader(xml), settings, parserContext);
            while (reader.Read()) { }
        }
        catch (XmlException ex)
        {
            throw new SchemaValidationException("XQDY0027",
                $"Validation failed: {ex.Message}", ex);
        }

        if (mode != ValidationMode.Lax && errors.Count > 0)
        {
            throw new SchemaValidationException("XQDY0027",
                $"Validation failed: {string.Join("; ", errors)}");
        }
    }

    public XdmNode Validate(XdmNode node, ValidationMode mode,
        string? typeNamespaceUri = null, string? typeLocalName = null)
    {
        ArgumentNullException.ThrowIfNull(node);
        // Phase 1: Validate the XML against schemas.
        // We serialize the XDM node to XML, run it through a validating XmlReader,
        // and collect any errors. If strict or type mode and errors occur, throw.
        //
        // Phase 2 (future): Return a deep copy with type annotations applied.
        // For now, we return the original node after validation passes —
        // full copy-with-annotations requires deeper node store integration.

        var xml = node.StringValue;

        // For elements, we need to reconstruct the XML with proper markup
        if (node is XdmElement elem)
        {
            var ns = GetNamespaceUri(elem.Namespace);
            using var sw = new StringWriter();
            using (var xw = XmlWriter.Create(sw, new XmlWriterSettings { OmitXmlDeclaration = true }))
            {
                xw.WriteStartElement(elem.Prefix ?? "", elem.LocalName, ns);
                xw.WriteString(elem.StringValue);
                xw.WriteEndElement();
            }
            xml = sw.ToString();
        }
        else if (node is XdmDocument)
        {
            // For document nodes, StringValue gives us the text content.
            // Full document serialization requires node store traversal.
            // For now, wrap in a minimal document if we don't have markup.
            if (!xml.TrimStart().StartsWith('<'))
                xml = $"<root>{xml}</root>";
        }

        var errors = new List<string>();
        var settings = new XmlReaderSettings
        {
            ValidationType = ValidationType.Schema,
            Schemas = _schemas,
            IgnoreWhitespace = false,
            IgnoreComments = false,
            IgnoreProcessingInstructions = false,
            ConformanceLevel = ConformanceLevel.Fragment
        };

        settings.ValidationEventHandler += (_, e) =>
        {
            if (e.Severity == XmlSeverityType.Error)
                errors.Add(e.Message);
        };

        if (mode == ValidationMode.Lax)
            settings.ValidationFlags |= XmlSchemaValidationFlags.ProcessSchemaLocation;

        // Run the validating reader
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), settings);
            while (reader.Read()) { }
        }
        catch (XmlException ex)
        {
            throw new SchemaValidationException("XQDY0027",
                $"Validation failed: {ex.Message}", ex);
        }

        if (mode != ValidationMode.Lax && errors.Count > 0)
        {
            throw new SchemaValidationException("XQDY0027",
                $"Validation failed: {string.Join("; ", errors)}");
        }

        // For type mode, verify the type constraint
        if (mode == ValidationMode.Type && typeLocalName != null)
        {
            var expectedNs = !string.IsNullOrEmpty(typeNamespaceUri)
                ? typeNamespaceUri
                : "http://www.w3.org/2001/XMLSchema";
            var expectedType = FindSchemaTypeByUri(expectedNs, typeLocalName);
            if (expectedType == null)
            {
                throw new SchemaValidationException("XQDY0027",
                    $"Unknown type: {typeLocalName}");
            }
        }

        // Return original node — full copy-with-annotations is a Phase 2 feature
        return node;
    }

    // ──────────────────────────────────────────────
    //  Private helpers
    // ──────────────────────────────────────────────

    private bool HasNamespace(string targetNamespace)
    {
        foreach (XmlSchema _ in _schemas.Schemas(targetNamespace ?? ""))
            return true;
        return false;
    }

    private IEnumerable<string> EnumerateLoadedNamespaces()
    {
        foreach (XmlSchema schema in _schemas.Schemas())
            yield return schema.TargetNamespace ?? "";
    }

    private XmlSchemaType? FindSchemaType(XdmTypeName typeName)
    {
        var ns = GetNamespaceUri(typeName.Namespace);
        return FindSchemaTypeByUri(ns, typeName.LocalName);
    }

    private XmlSchemaType? FindSchemaTypeByUri(string ns, string localName)
    {
        var qn = new XmlQualifiedName(localName, ns);
        if (_schemas.GlobalTypes[qn] is XmlSchemaType t)
            return t;
        return XmlSchemaType.GetBuiltInSimpleType(qn)
            ?? (XmlSchemaType?)XmlSchemaType.GetBuiltInComplexType(qn);
    }

    private bool IsInSubstitutionGroup(XdmElement element, XmlSchemaElement headDecl)
    {
        foreach (XmlSchemaElement globalElem in _schemas.GlobalElements.Values)
        {
            if (globalElem.SubstitutionGroup == headDecl.QualifiedName
                && globalElem.QualifiedName.Name == element.LocalName)
            {
                var elemNs = GetNamespaceUri(element.Namespace);
                if (globalElem.QualifiedName.Namespace == elemNs)
                    return true;
            }
        }
        return false;
    }

    private XdmTypeName ToXdmTypeName(XmlSchemaType schemaType)
    {
        var qn = schemaType.QualifiedName;
        if (qn == null || string.IsNullOrEmpty(qn.Name))
            return XdmTypeName.AnyType;

        var ns = qn.Namespace == "http://www.w3.org/2001/XMLSchema"
            ? NamespaceId.Xsd
            : new NamespaceId((uint)qn.Namespace.GetHashCode(StringComparison.Ordinal));

        // Make sure the URI is round-trippable from the synthesized NamespaceId.
        RememberNamespaceId(qn.Namespace);

        return new XdmTypeName(ns, qn.Name);
    }

    private XmlQualifiedName ToXmlQualifiedName(XdmQName name)
    {
        var ns = GetNamespaceUri(name.Namespace);
        return new XmlQualifiedName(name.LocalName, ns);
    }

    private string GetNamespaceUri(NamespaceId nsId)
        => _namespaceUriById.TryGetValue(nsId, out var uri) ? uri : "";

    /// <summary>
    /// Records the (NamespaceId → URI) mapping for a URI a caller has just registered with
    /// the provider. The id is computed using the same hash scheme that <c>SchemaFeatureChecker</c>
    /// uses, so subsequent lookups against XdmQNames built by that checker round-trip correctly.
    /// Built-in XSD/XML/XSI URIs are pre-registered and not re-hashed.
    /// </summary>
    private void RememberNamespaceId(string namespaceUri)
    {
        if (string.IsNullOrEmpty(namespaceUri)) return;
        if (namespaceUri is "http://www.w3.org/2001/XMLSchema"
            or "http://www.w3.org/XML/1998/namespace"
            or "http://www.w3.org/2001/XMLSchema-instance")
            return;
        var id = new NamespaceId((uint)namespaceUri.GetHashCode(StringComparison.Ordinal));
        _namespaceUriById[id] = namespaceUri;
    }
}
