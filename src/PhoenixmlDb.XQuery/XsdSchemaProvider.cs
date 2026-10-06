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
    private readonly XmlSchemaSet _schemas = new() { XmlResolver = new XsdVersionControl.Resolver() };

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
    /// Creates an empty schema provider. Use <see cref="ImportSchema(string, IReadOnlyList{string})"/> or <see cref="Add(string)"/>
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
            CompileSchemas();
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
            var text = reader.ReadToEnd();
            if (XsdVersionControl.Mentions(text))
                text = XsdVersionControl.Apply(text);
            using var xmlReader = XmlReader.Create(new StringReader(text));
            _schemas.Add(targetNamespace, xmlReader);
            CompileSchemas();
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

    /// <summary>
    /// Compiles the schema set, first keeping a single schema for the XML namespace. Two schemas
    /// that each import xml.xsd from a different location add two copies of it, and compiling
    /// fails with "The global attribute 'xml:lang' has already been declared" although the copies
    /// declare the same things. The QT3 Catalog schemas do exactly this.
    /// </summary>
    private void CompileSchemas()
    {
        // Keep the MOST COMPLETE copy, not the first one enumerated. .NET adds its own built-in
        // schema for the namespace when an import names it without a location, and that one
        // declares no xml:id. The set's enumeration order varies by runtime, so keeping "the
        // first" sometimes discarded a host's fuller copy: xml:id became undeclared and the
        // whole import failed (xslt BuiltinXmlNamespaceSchemaTests("id"), on some runtimes).
        var xmlNamespaceSchemas = _schemas.Schemas("http://www.w3.org/XML/1998/namespace").Cast<XmlSchema>()
            .OrderByDescending(schema => schema.Items.Count)
            .ToList();
        foreach (var duplicate in xmlNamespaceSchemas.Skip(1))
            _schemas.Remove(duplicate);
        _schemas.Compile();
    }

    private const string FnNamespace = "http://www.w3.org/2005/xpath-functions";

    /// <summary>Adapts a prefix lookup to System.Xml's resolver interface for ParseValue.</summary>
    private sealed class PrefixResolver(Func<string, string?>? resolve) : IXmlNamespaceResolver
    {
        public IDictionary<string, string> GetNamespacesInScope(XmlNamespaceScope scope) => new Dictionary<string, string>();
        public string? LookupNamespace(string prefix) =>
            prefix == "xml" ? "http://www.w3.org/XML/1998/namespace" : resolve?.Invoke(prefix);
        public string? LookupPrefix(string namespaceName) => null;
    }

    public bool HasSchemaType(string? namespaceUri, string localName) =>
        _schemas.GlobalTypes[new XmlQualifiedName(localName, namespaceUri ?? "")] is XmlSchemaType;

    /// <summary>
    /// The NamespaceId of a schema type's namespace, as type annotations carry it. Shared by the
    /// parser (element(*, T)) and the annotating parse, so the two compare equal.
    /// </summary>
    public static NamespaceId TypeNamespaceId(string namespaceUri) =>
        namespaceUri == "http://www.w3.org/2001/XMLSchema"
            ? NamespaceId.Xsd
            : new NamespaceId((uint)namespaceUri.GetHashCode(StringComparison.Ordinal));

    /// <summary>
    /// Adds the built-in schema for the XML representation of JSON (F&amp;O 3.1 §17.1), the type
    /// system of fn:json-to-xml's result. False if the resource is missing from the build.
    /// </summary>
    internal bool TryAddBuiltInJsonSchema()
    {
        if (HasNamespace(FnNamespace))
            return true;
        using var stream = typeof(XsdSchemaProvider).Assembly.GetManifestResourceStream("PhoenixmlDb.XQuery.schema-for-json.xsd");
        if (stream is null)
            return false;
        using var reader = XmlReader.Create(stream);
        _schemas.Add(FnNamespace, reader);
        CompileSchemas();
        RememberNamespaceId(FnNamespace);
        return true;
    }

    /// <inheritdoc />
    public void ImportSchema(string targetNamespace, IReadOnlyList<string>? locationHints, Security.ResourcePolicy? policy)
    {
        if (policy is null)
        {
            ImportSchema(targetNamespace, locationHints);
            return;
        }
        // Every schema document fetched while importing — the hints and whatever they include
        // or import — goes through the policy. The set is shared, so imports serialise here.
        lock (_schemas)
        {
            _schemas.XmlResolver = new XsdVersionControl.Resolver(policy);
            try
            {
                ImportSchema(targetNamespace, locationHints);
            }
            finally
            {
                _schemas.XmlResolver = new XsdVersionControl.Resolver();
            }
        }
    }

    public void ImportSchema(string targetNamespace, IReadOnlyList<string>? locationHints = null)
    {
        if (HasNamespace(targetNamespace))
            return;

        // Why each hint failed. Without it the error below says "Cannot locate schema" for a
        // schema that was located perfectly well and then failed to COMPILE — which sends the
        // next reader looking for a missing file. Same failure mode as the type-name
        // diagnostics: a message naming a cause it never established.
        List<string>? attempts = null;
        if (locationHints is { Count: > 0 })
        {
            foreach (var hint in locationHints)
            {
                try
                {
                    _schemas.Add(targetNamespace, hint);
                    CompileSchemas();
                    RememberNamespaceId(targetNamespace);
                    return;
                }
                // A hint that cannot be read (missing, refused, unreachable, not XML) is one more
                // failed attempt, reported as XQST0059 below — not a raw I/O exception.
                catch (Exception ex) when (ex is XmlSchemaException or XmlException or Security.ResourceAccessDeniedException
                                               or IOException or UnauthorizedAccessException
                                               or System.Net.Http.HttpRequestException or UriFormatException)
                {
                    (attempts ??= []).Add($"{hint}: {ex.Message}");
                }
            }
        }

        // The fn namespace's schema for fn:json-to-xml output is built in: F&O 3.1 expects the
        // processor to recognize the namespace without a location (QT3 json-to-xml-017b etc.).
        if (attempts is null && targetNamespace == FnNamespace && TryAddBuiltInJsonSchema())
            return;

        throw new SchemaException("XQST0059",
            attempts is null
                ? $"No schema location was given for namespace '{targetNamespace}'"
                : $"Could not load a schema for namespace '{targetNamespace}'. Tried "
                  + string.Join("; ", attempts));
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
            // Two anonymous types both have the empty name; only identity tells them apart.
            if (ReferenceEquals(current, required)
                || (!current.QualifiedName.IsEmpty && current.QualifiedName == required.QualifiedName))
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

    public bool MatchesSchemaElement(string elementNamespaceUri, string elementLocalName, XdmTypeName typeAnnotation,
        string declarationNamespaceUri, string declarationLocalName)
    {
        var declarationName = new XmlQualifiedName(declarationLocalName, declarationNamespaceUri ?? "");
        if (_schemas.GlobalElements[declarationName] is not XmlSchemaElement declaration)
            return false;
        var actualName = new XmlQualifiedName(elementLocalName, elementNamespaceUri ?? "");
        if (actualName != declarationName && !SubstitutesFor(actualName, declarationName))
            return false;
        return AnnotationDerivesFrom(typeAnnotation, declaration.ElementSchemaType);
    }

    public bool MatchesSchemaAttribute(string attributeNamespaceUri, string attributeLocalName, XdmTypeName typeAnnotation,
        string declarationNamespaceUri, string declarationLocalName)
    {
        var declarationName = new XmlQualifiedName(declarationLocalName, declarationNamespaceUri ?? "");
        if (_schemas.GlobalAttributes[declarationName] is not XmlSchemaAttribute declaration)
            return false;
        if (new XmlQualifiedName(attributeLocalName, attributeNamespaceUri ?? "") != declarationName)
            return false;
        return AnnotationDerivesFrom(typeAnnotation, declaration.AttributeSchemaType);
    }

    /// <summary>
    /// Whether a node's type annotation is the declared type or derived from it. An anonymous
    /// declared type is compared under the name <see cref="AnnotationName"/> gives it, which is
    /// what a validated node of that type carries.
    /// </summary>
    private bool AnnotationDerivesFrom(XdmTypeName typeAnnotation, XmlSchemaType? declaredType)
    {
        if (declaredType is null)
            return true;
        return IsSubtypeOf(typeAnnotation, AnnotationName(declaredType));
    }

    /// <summary>
    /// Whether the element declared as <paramref name="member"/> is in the substitution group
    /// headed by <paramref name="head"/>, directly or through other members (XSD 3.3.6).
    /// </summary>
    private bool SubstitutesFor(XmlQualifiedName member, XmlQualifiedName head)
    {
        var current = member;
        for (var depth = 0; depth < 64; depth++)
        {
            if (_schemas.GlobalElements[current] is not XmlSchemaElement declaration
                || declaration.SubstitutionGroup.IsEmpty)
                return false;
            current = declaration.SubstitutionGroup;
            if (current == head)
                return true;
        }
        return false;
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

        // Lax validation skips what has no declaration (XQuery 3.1 §3.21.2), but a node it does
        // assess, by declaration or xsi:type, must still be valid. System.Xml reports an
        // undeclared element or attribute in a namespace it has a schema for as an error, with
        // no code to tell it apart, so that one message is recognised by its wording.
        if (mode == ValidationMode.Lax)
            errors.RemoveAll(IsUndeclaredComponentError);
        if (errors.Count > 0)
        {
            throw new SchemaValidationException("XQDY0027",
                $"Validation failed: {string.Join("; ", errors)}");
        }
    }

    private static bool IsUndeclaredComponentError(string message) =>
        message.EndsWith(" is not declared.", StringComparison.Ordinal);

    /// <summary>
    /// Validates <paramref name="xmlContent"/> against the loaded schemas and returns a
    /// freshly built XDM tree whose elements/attributes carry <c>TypeAnnotation</c>
    /// values from <c>SchemaInfo.SchemaType</c>. Throws <see cref="SchemaValidationException"/>
    /// (XQDY0027) on validation failure.
    /// </summary>
    public Xdm.Nodes.XdmNode? ValidateAndAnnotate(string xmlContent, INodeBuilder builder, ValidationMode mode,
        string? typeNamespaceUri = null, string? typeLocalName = null)
        => ValidateAndAnnotate(xmlContent, builder, mode, typeNamespaceUri, typeLocalName, documentUri: null);

    /// <summary>
    /// <see cref="ValidateAndAnnotate(string, INodeBuilder, ValidationMode, string?, string?)"/>
    /// for a document loaded from <paramref name="documentUri"/>: the annotated tree keeps it as
    /// its document and base URI, as an unvalidated load of the same file does.
    /// </summary>
    public Xdm.Nodes.XdmNode? ValidateAndAnnotate(string xmlContent, INodeBuilder builder, ValidationMode mode,
        string? typeNamespaceUri, string? typeLocalName, string? documentUri)
    {
        ArgumentNullException.ThrowIfNull(xmlContent);
        ArgumentNullException.ThrowIfNull(builder);

        // Phase 1: surface validation errors via the existing throw-on-error path.
        // XmlDocumentParser's schema overload swallows ValidationEventHandler events
        // (a parse-time tree builder shouldn't take a policy stance on schema errors),
        // so we MUST validate up front to preserve the spec contract that strict/type
        // validation raises XQDY0027 on a non-conforming document.
        ValidateXml(xmlContent, mode, typeNamespaceUri, typeLocalName);

        // Phase 2: re-parse through the schema-aware builder so SchemaInfo.SchemaType
        // is captured into XdmElement.TypeAnnotation / XdmAttribute.TypeAnnotation.
        // Each validated tree needs its own document id: with the fixed id 0 every annotated
        // document in a store was the same document to a lookup by id, so `/` from one resolved
        // to whichever was registered last (QT3's harness validates many into one store).
        var docId = builder.AllocateDocumentId();
        var startNodeId = builder.AllocateId();
        var parser = new Xdm.Parsing.XmlDocumentParser(
            docId, startNodeId, builder.InternNamespace, preserveWhitespace: true);
        // Name every type annotation here, not from the store. The store's namespace ids are its
        // own, but element(*, T) and IsSubtypeOf identify a type's namespace by TypeNamespaceId,
        // so an annotation the store named never equalled the type a query named. An anonymous
        // type has no name at all and gets one of ours.
        var recipes = Execution.TypeCastHelper.TypedValueRecipes.GetOrCreateValue(builder);
        parser.SchemaTypeAnnotator = type =>
        {
            if (type.QualifiedName.Namespace == XmlSchema.Namespace && !type.QualifiedName.IsEmpty)
                return null;
            var name = AnnotationName(type);
            if (!recipes.ContainsKey(name) && TypedValueRecipe(type) is { } recipe)
                recipes[name] = recipe;
            return name;
        };

        Xdm.Parsing.ParseResult result;
        try
        {
            using var reader = new System.IO.StringReader(xmlContent);
            result = parser.Parse(reader, documentUri: documentUri, _schemas);
        }
        catch (System.Xml.XmlException ex)
        {
            // Unlikely after the validation pass above succeeded, but stay defensive.
            throw new SchemaValidationException("XQDY0027",
                $"Validation succeeded but annotating parse failed: {ex.Message}", ex);
        }

        // The parser numbered the tree's nodes from startNodeId on; reserve that range, or the
        // next allocation reuses it and a second validated document overwrites this one's nodes.
        var lastId = startNodeId;
        foreach (var node in result.Nodes)
        {
            builder.RegisterNode(node);
            if (node.Id.Value > lastId.Value) lastId = node.Id;
        }
        builder.ReserveIdsThrough(lastId);
        return result.Document;
    }

    /// <summary>The namespace of the names this provider gives anonymous types.</summary>
    private const string AnonymousTypeNamespace = "http://phoenixml.dev/xquery/anonymous-types";

    private readonly Dictionary<XmlSchemaType, XdmTypeName> _anonymousTypeNames = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, XmlSchemaType> _anonymousTypesByName = new(StringComparer.Ordinal);

    /// <summary>
    /// The name a node of this type is annotated with: the type's own, in the id scheme the rest
    /// of the engine uses for type names, or — for an anonymous type, which XDM 3.1 §2.7.1 says
    /// gets an implementation-defined name — one made up here and resolvable by
    /// <see cref="FindSchemaType"/>.
    /// </summary>
    private XdmTypeName AnnotationName(XmlSchemaType type)
    {
        if (!type.QualifiedName.IsEmpty)
            return ToXdmTypeName(type);
        lock (_anonymousTypeNames)
        {
            if (!_anonymousTypeNames.TryGetValue(type, out var name))
            {
                RememberNamespaceId(AnonymousTypeNamespace);
                name = new XdmTypeName(TypeNamespaceId(AnonymousTypeNamespace),
                    "anonymous-type-" + (_anonymousTypeNames.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
                _anonymousTypeNames[type] = name;
                _anonymousTypesByName[name.LocalName] = type;
            }
            return name;
        }
    }

    /// <summary>
    /// How a node of this type atomizes, or null when its typed value stays xs:untypedAtomic
    /// here: mixed and empty content, unions (the annotation normally names the member that
    /// matched instead), and types derived from xs:QName or xs:NOTATION.
    /// </summary>
    private static Func<string, object?>? TypedValueRecipe(XmlSchemaType type)
    {
        if (type is XmlSchemaComplexType complex)
        {
            // Element-only content has no typed value: atomizing such a node is FOTY0012.
            if (complex.ContentType == XmlSchemaContentType.ElementOnly)
                return _ => throw new Execution.XQueryRuntimeException("FOTY0012",
                    "A node whose type has element-only content has no typed value.");
            if (complex.ContentType != XmlSchemaContentType.TextOnly)
                return null;
            // Simple content: the typed value is that of the simple type it extends or restricts.
            XmlSchemaType? t = complex;
            while (t is XmlSchemaComplexType)
                t = t.BaseXmlSchemaType;
            return t is XmlSchemaSimpleType contentType ? TypedValueRecipe(contentType) : null;
        }
        if (type is not XmlSchemaSimpleType simple || simple.Datatype is not { } datatype)
            return null;
        switch (datatype.Variety)
        {
            case XmlSchemaDatatypeVariety.Atomic:
                var builtIn = BuiltInBaseName(simple);
                if (builtIn is "anySimpleType" or "anyAtomicType" or "QName" or "NOTATION")
                    return null;
                return value => Execution.TypeCastHelper.BuiltInTypedValue(builtIn, value)
                    ?? new Xdm.XsUntypedAtomic(value);
            case XmlSchemaDatatypeVariety.List:
                var listType = simple;
                while (listType.Content is XmlSchemaSimpleTypeRestriction && listType.BaseXmlSchemaType is XmlSchemaSimpleType baseList)
                    listType = baseList;
                if (listType.Content is not XmlSchemaSimpleTypeList { BaseItemType: { } itemType }
                    || TypedValueRecipe(itemType) is not { } itemRecipe)
                    return null;
                return value =>
                {
                    var tokens = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    var items = new List<object?>(tokens.Length);
                    foreach (var token in tokens)
                    {
                        if (itemRecipe(token) is object?[] several) items.AddRange(several);
                        else items.Add(itemRecipe(token));
                    }
                    return items.ToArray();
                };
            default:
                return null;
        }
    }

    private static string BuiltInBaseName(XmlSchemaSimpleType type)
    {
        for (XmlSchemaType? t = type; t != null; t = t.BaseXmlSchemaType)
            if (t.QualifiedName.Namespace == XmlSchema.Namespace && !t.QualifiedName.IsEmpty)
                return t.QualifiedName.Name;
        return "anyAtomicType";
    }

    /// <summary>
    /// Validates <paramref name="node"/> with its markup, serialized through
    /// <paramref name="nodeProvider"/> (#40). Returns the node unchanged.
    /// </summary>
    public XdmNode Validate(XdmNode node, INodeProvider nodeProvider, ValidationMode mode,
        string? typeNamespaceUri = null, string? typeLocalName = null)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(nodeProvider);
        ValidateXml(Functions.SerializeFunction.SerializeNodeToXml(node, nodeProvider), mode, typeNamespaceUri, typeLocalName);
        return node;
    }

    public XdmNode Validate(XdmNode node, ValidationMode mode,
        string? typeNamespaceUri = null, string? typeLocalName = null)
    {
        ArgumentNullException.ThrowIfNull(node);
        // Without a node provider this method cannot reach a node's children or attributes, and it
        // used to validate the element's start tag wrapped around its string value — dropping all
        // structure, so valid instances failed and invalid ones passed (#40). Refuse instead of
        // answering a different question; the overload taking an INodeProvider serializes the node.
        if (node is XdmElement { Children.Count: > 0 } or XdmElement { Attributes.Count: > 0 } or XdmDocument { Children.Count: > 0 })
            throw new InvalidOperationException(
                "Validating a node with children or attributes needs the node provider that resolves them: " +
                "call Validate(node, nodeProvider, mode, ...). Without it only the node's text could be validated, " +
                "which is not the node.");
        // Phase 1: Validate the XML against schemas.
        // We serialize the XDM node to XML, run it through a validating XmlReader,
        // and collect any errors. If strict or type mode and errors occur, throw.
        //
        // Phase 2 (future): Return a deep copy with type annotations applied.
        // For now, we return the original node after validation passes —
        // full copy-with-annotations requires deeper node store integration.

        // Not the public getter: under StrictStringValue it throws for a node with neither a cached
        // value nor a resolver, and a childless node's string value is known to be "" (#37).
        var xml = Execution.QueryExecutionContext.CachedOrResolvableStringValue(node)
                  ?? (node is XdmElement { Children.Count: 0 } or XdmDocument { Children.Count: 0 } ? "" : node.StringValue);

        // For elements, we need to reconstruct the XML with proper markup
        if (node is XdmElement elem)
        {
            var ns = GetNamespaceUri(elem.Namespace);
            using var sw = new StringWriter();
            using (var xw = XmlWriter.Create(sw, new XmlWriterSettings { OmitXmlDeclaration = true }))
            {
                xw.WriteStartElement(elem.Prefix ?? "", elem.LocalName, ns);
                xw.WriteString(xml);
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
        if (ns == AnonymousTypeNamespace)
        {
            lock (_anonymousTypeNames)
                return _anonymousTypesByName.GetValueOrDefault(localName);
        }
        var qn = new XmlQualifiedName(localName, ns);
        if (_schemas.GlobalTypes[qn] is XmlSchemaType t)
            return t;
        return XmlSchemaType.GetBuiltInSimpleType(qn)
            ?? (XmlSchemaType?)XmlSchemaType.GetBuiltInComplexType(qn);
    }

    // ──────────────────────────────────────────────
    //  ISchemaProvider.TryCastToSchemaSimpleType
    // ──────────────────────────────────────────────

    /// <summary>
    /// Validates a lexical value against a schema-defined simple type, for cast/castable.
    ///
    /// The facet checking is XmlSchemaDatatype.ParseValue's, not ours: it already enforces
    /// pattern, enumeration, length, bounds and whitespace for every XSD simple type,
    /// including unions and lists. Reimplementing that in the engine would be both large and
    /// worse.
    /// </summary>
    public IEnumerable<string> GetSchemaSimpleTypeNames(string? namespaceUri)
    {
        var ns = namespaceUri ?? "";
        foreach (XmlSchemaType type in _schemas.GlobalTypes.Values)
            if (type is XmlSchemaSimpleType && type.QualifiedName.Namespace == ns)
                yield return type.QualifiedName.Name;
    }

    public SchemaSimpleType? GetSchemaSimpleType(string? namespaceUri, string localName)
    {
        if (FindSchemaTypeByUri(namespaceUri ?? "", localName) is not XmlSchemaSimpleType simple
            || simple.Datatype is not { } datatype)
            return null;
        var variety = datatype.Variety switch
        {
            XmlSchemaDatatypeVariety.List => SchemaSimpleTypeVariety.List,
            XmlSchemaDatatypeVariety.Union => SchemaSimpleTypeVariety.Union,
            _ => SchemaSimpleTypeVariety.Atomic,
        };
        var members = new List<SchemaTypeReference>();
        var isPureUnion = false;
        if (variety == SchemaSimpleTypeVariety.Union)
        {
            isPureUnion = IsPureUnion(simple);
            // A union derived by restriction keeps its base union's members.
            var unionType = simple;
            while (unionType.Content is XmlSchemaSimpleTypeRestriction && unionType.BaseXmlSchemaType is XmlSchemaSimpleType baseSimple)
                unionType = baseSimple;
            if (unionType.Content is XmlSchemaSimpleTypeUnion union && union.BaseMemberTypes is { } memberTypes)
            {
                foreach (var member in memberTypes)
                {
                    var name = member.QualifiedName;
                    var isBuiltIn = name.Namespace == XmlSchema.Namespace;
                    // An anonymous member has no name to refer to; describe it by its nearest
                    // built-in, which is what membership can be decided against.
                    if (name.IsEmpty)
                        members.Add(new SchemaTypeReference(XmlSchema.Namespace, BuiltInNameOf(member), IsBuiltIn: true));
                    else
                        members.Add(new SchemaTypeReference(name.Namespace, name.Name, isBuiltIn));
                }
            }
        }
        return new SchemaSimpleType(namespaceUri, localName, variety, members)
        {
            BuiltInBaseLocalName = variety == SchemaSimpleTypeVariety.Atomic ? BuiltInNameOf(simple) : null,
            IsPureUnion = isPureUnion,
        };

        static bool IsPureUnion(XmlSchemaSimpleType type) =>
            type.Content is XmlSchemaSimpleTypeUnion { BaseMemberTypes: { } memberTypes }
            && memberTypes.All(m => m.Datatype?.Variety switch
            {
                XmlSchemaDatatypeVariety.Atomic => true,
                XmlSchemaDatatypeVariety.Union => IsPureUnion(m),
                _ => false,
            });

        static string BuiltInNameOf(XmlSchemaSimpleType type)
        {
            for (XmlSchemaType? t = type; t != null; t = t.BaseXmlSchemaType)
                if (t.QualifiedName.Namespace == XmlSchema.Namespace && !t.QualifiedName.IsEmpty)
                    return t.QualifiedName.Name;
            return "anyAtomicType";
        }
    }

    public bool TryCastToSchemaSimpleType(string? namespaceUri, string localName, string lexicalValue)
        => TryCastToSchemaSimpleType(namespaceUri, localName, lexicalValue, resolvePrefix: null);

    public bool TryCastToSchemaSimpleType(string? namespaceUri, string localName, string lexicalValue, Func<string, string?>? resolvePrefix)
    {
        var type = FindSchemaTypeByUri(namespaceUri ?? "", localName);

        // "No such type" and "not a simple type" are STATIC errors about the query, so they
        // throw. Only "the value does not satisfy the type" is a false — that is the ordinary
        // castable outcome and must not be reported as a broken query.
        if (type is null)
            throw new SchemaException("XPST0051",
                $"'{{{namespaceUri}}}{localName}' is not a type declared by any imported schema.");
        if (type is not XmlSchemaSimpleType simple)
            throw new SchemaException("XPST0051",
                $"'{{{namespaceUri}}}{localName}' is a complex type; only simple types can be a cast target.");
        if (simple.Datatype is not { } datatype)
            throw new SchemaException("XPST0051",
                $"'{{{namespaceUri}}}{localName}' has no usable value space.");

        try
        {
            // A QName-based type parses its prefix through the resolver; with none, System.Xml
            // dereferenced null (QT3 qname-cast-*, CastAs-UnionType-10..33). An unbound prefix
            // is then an ordinary "not a value of this type".
            datatype.ParseValue(lexicalValue, new NameTable(), new PrefixResolver(resolvePrefix));
            return true;
        }
        catch (XmlSchemaException) { return false; }
        catch (FormatException) { return false; }
        catch (OverflowException) { return false; }
        catch (ArgumentException) { return false; }
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

        var ns = TypeNamespaceId(qn.Namespace);

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
