using System.Numerics;
using System.Text;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.Xdm.Serialization;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Functions;
using PhoenixmlDb.XQuery.Optimizer;

namespace PhoenixmlDb.XQuery.Execution;

/// <summary>
/// Element constructor operator — creates an XdmElement node with attributes and content.
/// Used for both direct element constructors (&lt;elem&gt;...&lt;/elem&gt;) and computed
/// element constructors (element name { content }).
/// </summary>
public sealed class ElementConstructorOperator : PhysicalOperator
{
    public required QName Name { get; init; }
    public required IReadOnlyList<PhysicalOperator> AttributeOperators { get; init; }
    public required IReadOnlyList<PhysicalOperator> ContentOperators { get; init; }

    /// <summary>
    /// When true, this element constructor is a direct child of another element constructor
    /// (not inside an enclosed expression). Direct child constructors are NOT subject to
    /// copy-namespaces semantics because they define their own namespace declarations as part
    /// of the construction, rather than copying an existing node.
    /// </summary>
    public bool IsDirectChild { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        var store = context.NodeStore as INodeBuilder;
        if (store == null)
        {
            // This used to serialize the element to an xs:string instead. There is no correct
            // result to return here — an element constructor without a node builder cannot
            // produce an element — so the string stood in for one and the wrong answer surfaced
            // two steps later as "axis step used when the context item is not a node", pointing
            // at the path expression rather than at the host's node store. Reported by the
            // phoenixml engine repo, whose PersistentNodeProvider implemented INodeStore but not
            // INodeBuilder, so EVERY element constructor on their spanning path silently became
            // a string.
            throw new InvalidOperationException(
                "An element constructor requires a node store implementing INodeBuilder, but the "
                + $"host supplied {context.NodeStore?.GetType().Name ?? "no node store"}. "
                + "Without one no element node can be constructed.");
        }

        // Resolve namespace: either use the static analysis ID or intern a new one from the URI
        // Use a local copy of the element name so prefix renaming (for namespace conflicts)
        // can update it without mutating the operator.
        var elemName = Name;
        var nsId = elemName.Namespace;
        if (elemName.ResolvedNamespace != null)
        {
            nsId = store.InternNamespace(elemName.ResolvedNamespace, nsId);
        }

        // Evaluate attributes
        var attrIds = new List<NodeId>();
        var elemId = store.AllocateId();
        var constructedDocId = new DocumentId(0);

        // Track attribute (namespace, localName) for XQDY0025 duplicate detection
        var seenAttrs = new HashSet<(NamespaceId, string)>();
        void CheckDuplicateAttr(XdmAttribute a)
        {
            if (!seenAttrs.Add((a.Namespace, a.LocalName)))
                throw new XQueryRuntimeException("XQDY0025",
                    $"Duplicate attribute in element constructor: {a.LocalName}");
            if (a.Prefix == "xml")
            {
                if (a.LocalName == "base")
                {
                    // FORG0001: malformed percent-escape in xml:base URI
                    var bv = a.Value ?? string.Empty;
                    for (int i = 0; i < bv.Length; i++)
                    {
                        if (bv[i] == '%')
                        {
                            if (i + 2 >= bv.Length || !Uri.IsHexDigit(bv[i + 1]) || !Uri.IsHexDigit(bv[i + 2]))
                                throw new XQueryRuntimeException("FORG0001",
                                    $"Invalid xml:base URI: '{bv}'");
                            i += 2;
                        }
                    }
                }
                if (a.LocalName == "id")
                {
                    // XQDY0091: xml:id value must be a valid NCName after whitespace normalization
                    var norm = System.Text.RegularExpressions.Regex.Replace((a.Value ?? string.Empty).Trim(), "\\s+", " ");
                    if (norm.Length == 0 || !System.Xml.XmlConvert.IsNCNameChar(norm[0]) || norm.Any(c => !System.Xml.XmlConvert.IsNCNameChar(c)))
                        throw new XQueryRuntimeException("XQDY0091", $"Invalid xml:id value: '{a.Value}'");
                }
                else if (a.LocalName == "space")
                {
                    // XQDY0092: xml:space must be 'preserve' or 'default'
                    var v = a.Value ?? string.Empty;
                    if (v != "preserve" && v != "default")
                        throw new XQueryRuntimeException("XQDY0092", $"xml:space value must be 'preserve' or 'default', got '{v}'");
                }
            }
        }

        foreach (var attrOp in AttributeOperators)
        {
            await foreach (var attrResult in attrOp.ExecuteAsync(context))
            {
                if (attrResult is XdmAttribute attr)
                {
                    CheckDuplicateAttr(attr);
                    // Deep-copy attribute into constructed tree with new parent
                    var newAttrId = store.AllocateId();
                    var newAttr = new XdmAttribute
                    {
                        Id = newAttrId,
                        Document = constructedDocId,
                        Namespace = attr.Namespace,
                        LocalName = attr.LocalName,
                        Prefix = attr.Prefix,
                        Value = attr.Value,
                        TypeAnnotation = attr.TypeAnnotation,
                        IsId = attr.IsId
                    };
                    newAttr.Parent = elemId;
                    store.RegisterNode(newAttr);
                    attrIds.Add(newAttrId);
                }
            }
        }

        // Propagate namespace bindings from this element's xmlns: attributes
        // to the execution context, so computed constructors in content can resolve prefixes.
        // Save old bindings to restore after content evaluation.
        var oldBindings = context.PrefixNamespaceBindings;
        var oldConstructorBindings = context.EnclosingConstructorBindings;
        Dictionary<string, string>? newBindings = null;
        // Separately, accumulate ONLY the bindings contributed by constructor syntax
        // (xmlns:* attrs + element's own prefix binding). Prolog declarations are NOT
        // added here — they only become part of an element's in-scope namespaces if used.
        Dictionary<string, string>? newConstructorBindings = null;
        foreach (var attrId in attrIds)
        {
            if (store.GetNode(attrId) is XdmAttribute nsAttr)
            {
                if (nsAttr.Prefix == "xmlns")
                {
                    newBindings ??= oldBindings != null
                        ? new Dictionary<string, string>(oldBindings)
                        : new Dictionary<string, string>();
                    newBindings[nsAttr.LocalName] = nsAttr.Value;
                    newConstructorBindings ??= oldConstructorBindings != null
                        ? new Dictionary<string, string>(oldConstructorBindings)
                        : new Dictionary<string, string>();
                    newConstructorBindings[nsAttr.LocalName] = nsAttr.Value;
                }
                else if (string.IsNullOrEmpty(nsAttr.Prefix) && nsAttr.LocalName == "xmlns")
                {
                    newBindings ??= oldBindings != null
                        ? new Dictionary<string, string>(oldBindings)
                        : new Dictionary<string, string>();
                    newBindings[""] = nsAttr.Value;
                    newConstructorBindings ??= oldConstructorBindings != null
                        ? new Dictionary<string, string>(oldConstructorBindings)
                        : new Dictionary<string, string>();
                    newConstructorBindings[""] = nsAttr.Value;
                }
            }
        }
        // Add the element's own prefix binding: `<foo:e>` makes `foo` in-scope for the element,
        // even if `foo` was only declared at the prolog level.
        if (!string.IsNullOrEmpty(elemName.Prefix) && elemName.ResolvedNamespace != null)
        {
            newConstructorBindings ??= oldConstructorBindings != null
                ? new Dictionary<string, string>(oldConstructorBindings)
                : new Dictionary<string, string>();
            if (!newConstructorBindings.ContainsKey(elemName.Prefix))
                newConstructorBindings[elemName.Prefix] = elemName.ResolvedNamespace;
        }
        if (newBindings != null)
            context.PrefixNamespaceBindings = newBindings;
        if (newConstructorBindings != null)
            context.EnclosingConstructorBindings = newConstructorBindings;

        // Evaluate content — also collect namespace nodes from computed namespace constructors
        var childIds = new List<NodeId>();
        var contentNsDecls = new List<NamespaceBinding>();
        StringBuilder? pendingText = null;

        void FlushPendingText()
        {
            if (pendingText != null && pendingText.Length > 0)
            {
                var textValue = pendingText.ToString();
                // Boundary whitespace stripping is handled at compile time in the
                // optimizer's FilterBoundaryWhitespace. No runtime stripping needed.
                var textId = store.AllocateId();
                var textNode = new XdmText
                {
                    Id = textId,
                    Document = constructedDocId,
                    Value = textValue
                };
                textNode.Parent = elemId;
                store.RegisterNode(textNode);
                childIds.Add(textId);
                pendingText.Clear();
            }
        }

        foreach (var contentOp in ContentOperators)
        {
            // Direct child constructors produce freshly constructed elements — copy-namespaces
            // mode (preserve/no-preserve) only applies when copying a pre-existing node
            // (e.g. from a variable reference in an enclosed expression), not when constructing
            // a new element inline.  See XQuery 3.1 §3.7.1: "The copy-namespaces declaration
            // controls the namespace bindings that are assigned when an existing element node
            // is copied by an element constructor."
            bool isDirectChildConstructor = contentOp is ElementConstructorOperator { IsDirectChild: true };
            bool isFirstAtomicInOp = true;
            await foreach (var contentResult in contentOp.ExecuteAsync(context))
            {
                if (contentResult is XdmElement childElem)
                {
                    FlushPendingText();
                    // Deep-copy the element into the constructed tree
                    var copyId = DeepCopyNode(childElem, store, constructedDocId, elemId);
                    if (!isDirectChildConstructor)
                        ApplyCopyNamespacesMode(copyId, store, context.CopyNamespacesMode, context.EnclosingConstructorBindings);
                    if (context.ConstructionMode == Analysis.ConstructionMode.Strip)
                        StripTypeAnnotations(copyId, store);
                    childIds.Add(copyId);
                    // Reset so the next atomic value after a node doesn't get a leading space.
                    // Per XQuery §3.7.1.3, spaces only separate *adjacent* atomic values.
                    isFirstAtomicInOp = true;
                }
                else if (contentResult is XdmDocument doc)
                {
                    // Document node: unwrap children. Per XQuery spec §3.7.1, document nodes in
                    // element content are replaced by their children. Text children merge with
                    // pending text to ensure adjacent text nodes are properly concatenated.
                    foreach (var docChildId in doc.Children)
                    {
                        var docChild = store.GetNode(docChildId);
                        if (docChild is XdmText docText)
                        {
                            // Merge text from nested doc into pending text
                            pendingText ??= new StringBuilder();
                            pendingText.Append(docText.Value);
                        }
                        else if (docChild != null)
                        {
                            FlushPendingText();
                            var copyId = DeepCopyNode(docChild, store, constructedDocId, elemId);
                            ApplyCopyNamespacesMode(copyId, store, context.CopyNamespacesMode, context.EnclosingConstructorBindings);
                            if (context.ConstructionMode == Analysis.ConstructionMode.Strip)
                                StripTypeAnnotations(copyId, store);
                            childIds.Add(copyId);
                        }
                    }
                    isFirstAtomicInOp = true;
                }
                else if (contentResult is XdmText text)
                {
                    // Merge adjacent text
                    pendingText ??= new StringBuilder();
                    pendingText.Append(text.Value);
                    isFirstAtomicInOp = true;
                }
                else if (contentResult is XdmNamespace nsNode)
                {
                    // Namespace constructor: add to element's namespace declarations.
                    // XQDY0102: if the same prefix is declared more than once with different
                    // URIs within a single element constructor, raise an error.
                    var nsAttrId = store.InternNamespace(nsNode.Uri);
                    foreach (var existing in contentNsDecls)
                    {
                        if (existing.Prefix == (nsNode.Prefix ?? "") && existing.Namespace != nsAttrId)
                            throw new XQueryRuntimeException("XQDY0102",
                                $"Namespace constructor: prefix '{nsNode.Prefix}' bound to multiple URIs within an element constructor");
                    }
                    contentNsDecls.Add(new NamespaceBinding(nsNode.Prefix ?? "", nsAttrId));
                }
                else if (contentResult is XdmComment || contentResult is XdmProcessingInstruction)
                {
                    FlushPendingText();
                    var copyId = DeepCopyNode((XdmNode)contentResult, store, constructedDocId, elemId);
                    childIds.Add(copyId);
                    isFirstAtomicInOp = true;
                }
                else if (contentResult is XdmAttribute)
                {
                    // XQTY0024: attribute must not appear after a non-attribute child in content
                    if (childIds.Count > 0 || (pendingText != null && pendingText.Length > 0))
                        throw new XQueryRuntimeException("XQTY0024",
                            "Attribute node in element content must precede all other nodes");
                    var contentAttr = (XdmAttribute)contentResult;
                    // XQuery 3.1 §3.9.1.3: when construction=preserve, an attribute with a
                    // namespace-sensitive type annotation (xs:QName or xs:NOTATION, or a type
                    // derived from either) must raise XQTY0086.  In strip mode the type is
                    // erased to xs:untypedAtomic before use, so the constraint does not apply.
                    if (context.ConstructionMode == Analysis.ConstructionMode.Preserve
                        && IsNamespaceSensitiveType(contentAttr.TypeAnnotation))
                        throw new XQueryRuntimeException("XQTY0086",
                            $"Element content contains an attribute ('{contentAttr.LocalName}') whose " +
                            $"type annotation ({contentAttr.TypeAnnotation}) is namespace-sensitive " +
                            "(xs:QName or xs:NOTATION); this is not permitted when construction=preserve " +
                            "because the namespace prefix may not be in scope in the new context.");
                    CheckDuplicateAttr(contentAttr);
                    var newAttrId = store.AllocateId();
                    // XQuery 3.1 §3.9.1.2: preserve → retain type annotation; strip → xs:untypedAtomic
                    var attrTypeAnnotation = context.ConstructionMode == Analysis.ConstructionMode.Strip
                        ? Xdm.XdmTypeName.UntypedAtomic
                        : contentAttr.TypeAnnotation;
                    var newAttr = new XdmAttribute
                    {
                        Id = newAttrId,
                        Document = constructedDocId,
                        Namespace = contentAttr.Namespace,
                        LocalName = contentAttr.LocalName,
                        Prefix = contentAttr.Prefix,
                        Value = contentAttr.Value,
                        TypeAnnotation = attrTypeAnnotation,
                        IsId = contentAttr.IsId
                    };
                    newAttr.Parent = elemId;
                    store.RegisterNode(newAttr);
                    attrIds.Add(newAttrId);
                }
                else if (contentResult is List<object?> arrayItems)
                {
                    // XQuery 3.1 §3.7.3.1: arrays in element content are replaced
                    // by their members (recursively flattened into the content sequence).
                    bool isFirstAtomicInArray = true;
                    foreach (var member in FlattenArrayMembers(arrayItems))
                    {
                        if (member is XdmAttribute arrAttr)
                        {
                            // XQuery 3.1 §3.9.1.3: XQTY0086 check — same rule applies for
                            // attributes delivered through array members.
                            if (context.ConstructionMode == Analysis.ConstructionMode.Preserve
                                && IsNamespaceSensitiveType(arrAttr.TypeAnnotation))
                                throw new XQueryRuntimeException("XQTY0086",
                                    $"Element content contains an attribute ('{arrAttr.LocalName}') whose " +
                                    $"type annotation ({arrAttr.TypeAnnotation}) is namespace-sensitive " +
                                    "(xs:QName or xs:NOTATION); this is not permitted when construction=preserve.");
                            // Attributes from array members are added to the element
                            var newAttrId = store.AllocateId();
                            // XQuery 3.1 §3.9.1.2: preserve → retain; strip → xs:untypedAtomic
                            var arrAttrAnnotation = context.ConstructionMode == Analysis.ConstructionMode.Strip
                                ? Xdm.XdmTypeName.UntypedAtomic
                                : arrAttr.TypeAnnotation;
                            var newAttr = new XdmAttribute
                            {
                                Id = newAttrId,
                                Document = constructedDocId,
                                Namespace = arrAttr.Namespace,
                                LocalName = arrAttr.LocalName,
                                Prefix = arrAttr.Prefix,
                                Value = arrAttr.Value,
                                TypeAnnotation = arrAttrAnnotation,
                                IsId = arrAttr.IsId
                            };
                            newAttr.Parent = elemId;
                            store.RegisterNode(newAttr);
                            attrIds.Add(newAttrId);
                            isFirstAtomicInArray = true;
                        }
                        else if (member is XdmElement arrElem)
                        {
                            FlushPendingText();
                            var copyId = DeepCopyNode(arrElem, store, constructedDocId, elemId);
                            ApplyCopyNamespacesMode(copyId, store, context.CopyNamespacesMode, context.EnclosingConstructorBindings);
                            if (context.ConstructionMode == Analysis.ConstructionMode.Strip)
                                StripTypeAnnotations(copyId, store);
                            childIds.Add(copyId);
                            isFirstAtomicInArray = true; // reset after node
                        }
                        else if (member is XdmText arrText)
                        {
                            pendingText ??= new StringBuilder();
                            pendingText.Append(arrText.Value);
                            isFirstAtomicInArray = true; // reset after node
                        }
                        else if (member is XdmDocument arrDoc)
                        {
                            foreach (var docChildId in arrDoc.Children)
                            {
                                var docChild = store.GetNode(docChildId);
                                if (docChild is XdmText docText)
                                {
                                    pendingText ??= new StringBuilder();
                                    pendingText.Append(docText.Value);
                                }
                                else if (docChild != null)
                                {
                                    FlushPendingText();
                                    var copyId = DeepCopyNode(docChild, store, constructedDocId, elemId);
                                    ApplyCopyNamespacesMode(copyId, store, context.CopyNamespacesMode, context.EnclosingConstructorBindings);
                                    if (context.ConstructionMode == Analysis.ConstructionMode.Strip)
                                        StripTypeAnnotations(copyId, store);
                                    childIds.Add(copyId);
                                }
                            }
                            isFirstAtomicInArray = true; // reset after node
                        }
                        else if (member is XdmComment || member is XdmProcessingInstruction)
                        {
                            FlushPendingText();
                            var copyId = DeepCopyNode((XdmNode)member, store, constructedDocId, elemId);
                            childIds.Add(copyId);
                            isFirstAtomicInArray = true; // reset after node
                        }
                        else if (member != null)
                        {
                            pendingText ??= new StringBuilder();
                            var atomized = context.AtomizeWithNodes(member);
                            var atomicText = Functions.ConcatFunction.XQueryStringValue(atomized);
                            if (!isFirstAtomicInArray)
                                pendingText.Append(' ');
                            pendingText.Append(atomicText);
                            isFirstAtomicInArray = false;
                        }
                    }
                    isFirstAtomicInOp = false;
                }
                else if (contentResult != null)
                {
                    // Atomic values become text nodes; merge adjacent text
                    // Per XQuery 3.1 §3.7.1.3: adjacent atomic values from the SAME
                    // expression are separated by spaces. Values from different
                    // content expressions concatenate without a separator.
                    pendingText ??= new StringBuilder();
                    var atomized = context.AtomizeWithNodes(contentResult);
                    var atomicText = Functions.ConcatFunction.XQueryStringValue(atomized);
                    if (!isFirstAtomicInOp)
                        pendingText.Append(' ');
                    pendingText.Append(atomicText);
                    isFirstAtomicInOp = false;
                }
            }
        }

        FlushPendingText();

        // Restore old namespace bindings
        if (newBindings != null)
            context.PrefixNamespaceBindings = oldBindings;
        if (newConstructorBindings != null)
            context.EnclosingConstructorBindings = oldConstructorBindings;

        // Merge content namespace declarations (from computed namespace constructors)
        // These are added to the element's namespace declarations below.

        // Build namespace declarations from element name + xmlns: attributes
        var nsDecls = new List<NamespaceBinding>();
        var nsPrefixesSeen = new HashSet<string>();
        if (nsId != NamespaceId.None)
        {
            var prefix = elemName.Prefix ?? "";
            nsDecls.Add(new NamespaceBinding(prefix, nsId));
            nsPrefixesSeen.Add(prefix);
        }

        // Extract namespace declarations from xmlns: attributes
        // These were processed as regular attributes but need to be in NamespaceDeclarations
        var nonNsAttrIds = new List<NodeId>();
        foreach (var attrId in attrIds)
        {
            if (store.GetNode(attrId) is XdmAttribute attr)
            {
                // xmlns:prefix="uri" → namespace declaration (never a regular attribute)
                if (attr.Prefix == "xmlns")
                {
                    if (!nsPrefixesSeen.Contains(attr.LocalName))
                    {
                        var nsAttrId = store.InternNamespace(attr.Value);
                        nsDecls.Add(new NamespaceBinding(attr.LocalName, nsAttrId));
                        nsPrefixesSeen.Add(attr.LocalName);
                    }
                    continue;
                }
                // xmlns="uri" → default namespace declaration (never a regular attribute)
                if (string.IsNullOrEmpty(attr.Prefix) && attr.LocalName == "xmlns")
                {
                    if (!nsPrefixesSeen.Contains(""))
                    {
                        var nsAttrId = string.IsNullOrEmpty(attr.Value)
                            ? NamespaceId.None
                            : store.InternNamespace(attr.Value);
                        nsDecls.Add(new NamespaceBinding("", nsAttrId));
                        nsPrefixesSeen.Add("");
                    }
                    continue;
                }
            }
            nonNsAttrIds.Add(attrId);
        }

        // Merge in-content namespace constructors. Per XQuery 3.1 §3.9.3, namespace
        // nodes added to an element take precedence — if a content namespace constructor
        // uses a prefix that conflicts with the element's own prefix or an xmlns: attr,
        // the element/attribute is renamed to a fresh prefix.
        int genCounterNs = 1;
        foreach (var contentNs in contentNsDecls)
        {
            if (string.IsNullOrEmpty(contentNs.Prefix) && contentNs.Namespace != NamespaceId.None && nsId == NamespaceId.None)
            {
                throw new XQueryRuntimeException("XQDY0102",
                    "Namespace constructor: cannot bind default namespace to non-empty URI on element in no namespace");
            }
            if (!nsPrefixesSeen.Contains(contentNs.Prefix))
            {
                nsDecls.Add(contentNs);
                nsPrefixesSeen.Add(contentNs.Prefix);
            }
            else
            {
                // Prefix already bound — check if it's the same URI
                var existingIdx = nsDecls.FindIndex(d => d.Prefix == contentNs.Prefix);
                if (existingIdx >= 0 && nsDecls[existingIdx].Namespace != contentNs.Namespace)
                {
                    // Conflict: content ns constructor takes precedence.
                    // If the conflicting binding came from the element's own prefix,
                    // rename the element to use a fresh prefix.
                    var oldBinding = nsDecls[existingIdx];
                    var elemPrefix = elemName.Prefix ?? "";
                    if (oldBinding.Prefix == elemPrefix && oldBinding.Namespace == nsId)
                    {
                        // Rename the element's prefix
                        string newElemPrefix;
                        var allPrefixes = new HashSet<string>(nsPrefixesSeen);
                        do { newElemPrefix = $"ns{genCounterNs++}"; } while (allPrefixes.Contains(newElemPrefix));

                        // Replace the old binding with the element's new prefix
                        nsDecls[existingIdx] = new NamespaceBinding(newElemPrefix, nsId);
                        nsPrefixesSeen.Add(newElemPrefix);

                        // Update the element name to use the new prefix
                        elemName = new QName(elemName.Namespace, elemName.LocalName, newElemPrefix)
                        {
                            ExpandedNamespace = elemName.ExpandedNamespace,
                            RuntimeNamespace = elemName.RuntimeNamespace
                        };
                    }
                    // Now add the content namespace binding
                    nsDecls.Add(contentNs);
                }
            }
        }

        // Build a prefix→NamespaceId map from the current nsDecls so we can detect
        // prefix/URI conflicts for prefixed attributes being added from copied nodes.
        var prefixToNs = new Dictionary<string, NamespaceId>(StringComparer.Ordinal);
        foreach (var d in nsDecls) prefixToNs[d.Prefix] = d.Namespace;

        // For each prefixed attribute, ensure the element has a namespace binding that
        // maps its prefix to the attribute's namespace URI. If the prefix is already
        // bound to a different URI, generate a fresh prefix and rewrite the attribute.
        int genCounter = 1;
        for (int i = 0; i < nonNsAttrIds.Count; i++)
        {
            var attrId = nonNsAttrIds[i];
            if (store.GetNode(attrId) is not XdmAttribute attr2) continue;
            if (string.IsNullOrEmpty(attr2.Prefix) || attr2.Prefix == "xmlns") continue;
            // "xml" prefix is implicit and must not be declared explicitly.
            if (attr2.Prefix == "xml") continue;
            // Skip attrs that are not actually in a namespace (shouldn't happen for prefixed).
            if (attr2.Namespace == NamespaceId.None) continue;

            if (prefixToNs.TryGetValue(attr2.Prefix, out var existingNs))
            {
                if (existingNs == attr2.Namespace)
                    continue; // already bound correctly
                // Conflict: same prefix already bound to a different URI. Generate a
                // fresh prefix and rewrite the attribute to use it.
                string newPrefix;
                do { newPrefix = $"ns{genCounter++}"; } while (prefixToNs.ContainsKey(newPrefix));
                var renamed = new XdmAttribute
                {
                    Id = attr2.Id,
                    Document = attr2.Document,
                    Namespace = attr2.Namespace,
                    LocalName = attr2.LocalName,
                    Prefix = newPrefix,
                    Value = attr2.Value,
                    TypeAnnotation = attr2.TypeAnnotation,
                    IsId = attr2.IsId
                };
                renamed.Parent = attr2.Parent;
                store.RegisterNode(renamed);
                nsDecls.Add(new NamespaceBinding(newPrefix, attr2.Namespace));
                prefixToNs[newPrefix] = attr2.Namespace;
                nsPrefixesSeen.Add(newPrefix);
            }
            else
            {
                nsDecls.Add(new NamespaceBinding(attr2.Prefix, attr2.Namespace));
                prefixToNs[attr2.Prefix] = attr2.Namespace;
                nsPrefixesSeen.Add(attr2.Prefix);
            }
        }

        // (Content namespace declarations were already merged above, before attribute rename.)

        // Propagate namespace bindings from enclosing direct element constructors.
        // Per XQuery 3.1 §3.7.1: the in-scope namespaces of a constructed element include
        // those inherited from enclosing element constructors. Bindings that come from
        // the prolog (declare namespace) are NOT automatically inherited — only those added
        // by enclosing direct constructors are propagated.
        if (context.PrefixNamespaceBindings != null)
        {
            var prologBindings = context.PrologNamespaceBindings;
            foreach (var (prefix, uri) in context.PrefixNamespaceBindings)
            {
                // Skip internal markers
                if (prefix == "##default-element") continue;

                // Skip prefixes already declared on this element
                if (nsPrefixesSeen.Contains(prefix)) continue;

                // Only propagate bindings that were added by enclosing constructors,
                // not those from the prolog. A binding is "from an enclosing constructor"
                // if it's not in the prolog baseline, or if the URI differs (overridden).
                if (prologBindings != null)
                {
                    if (prologBindings.TryGetValue(prefix, out var prologUri) && prologUri == uri)
                        continue; // Same as prolog — skip (not from an enclosing constructor)
                }

                // Resolve the URI to a NamespaceId
                NamespaceId inheritedNsId;
                if (string.IsNullOrEmpty(uri))
                {
                    inheritedNsId = NamespaceId.None;
                }
                else
                {
                    inheritedNsId = store.InternNamespace(uri);
                }

                nsDecls.Add(new NamespaceBinding(prefix, inheritedNsId));
                nsPrefixesSeen.Add(prefix);
            }
        }

        // If this element is in no namespace but the context has a default namespace
        // in scope (from prolog or enclosing constructor), add an explicit xmlns=""
        // undeclaration so that in-scope-prefixes and serialization work correctly.
        // Without this, the default namespace from the parent would leak through.
        if (nsId == NamespaceId.None && !nsPrefixesSeen.Contains(""))
        {
            bool hasDefaultNs = false;
            if (context.PrefixNamespaceBindings != null)
            {
                if (context.PrefixNamespaceBindings.TryGetValue("", out var defNs) && !string.IsNullOrEmpty(defNs))
                    hasDefaultNs = true;
                else if (context.PrefixNamespaceBindings.TryGetValue("##default-element", out var prologDefNs) && !string.IsNullOrEmpty(prologDefNs))
                    hasDefaultNs = true;
            }
            if (hasDefaultNs)
            {
                nsDecls.Add(new NamespaceBinding("", NamespaceId.None));
                nsPrefixesSeen.Add("");
            }
        }

        var elem = new XdmElement
        {
            Id = elemId,
            Document = constructedDocId,
            Namespace = nsId,
            LocalName = elemName.LocalName,
            Prefix = elemName.Prefix,
            Attributes = nonNsAttrIds,
            Children = childIds,
            NamespaceDeclarations = nsDecls,
            BaseUri = context.StaticBaseUri,
            // XQuery 3.1 §3.9.1.1: directly-constructed elements always have type annotation
            // xs:untyped regardless of construction mode.  ConstructionMode (preserve/strip)
            // controls type annotations on *copied* element subtrees (§3.9.1.2), not on the
            // new element node being constructed here.
            TypeAnnotation = Xdm.XdmTypeName.Untyped
        };
        elem.Parent = null;
        // Compute string value so atomization works on constructed elements
        elem._stringValue = ComputeStringValueFromChildren(childIds, store);
        store.RegisterNode(elem);

        yield return elem;
    }

    /// <summary>
    /// Computes the string value of an element from its children by walking text descendants.
    /// </summary>
    internal static string ComputeStringValueFromChildren(IReadOnlyList<NodeId> childIds, INodeProvider store)
    {
        if (childIds.Count == 0) return "";
        var sb = new System.Text.StringBuilder();
        foreach (var childId in childIds)
        {
            var child = store.GetNode(childId);
            if (child is XdmText text)
                sb.Append(text.Value);
            else if (child is XdmElement childElem)
            {
                // Use pre-computed value if available, otherwise recurse
                var sv = childElem.StringValue;
                if (!string.IsNullOrEmpty(sv))
                    sb.Append(sv);
                else
                    sb.Append(ComputeStringValueFromChildren(childElem.Children, store));
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// XQuery 3.1 §3.9.1.2: under <c>declare construction strip</c>, copied element subtrees
    /// have their TypeAnnotation recursively reset to xs:untyped (elements) and
    /// xs:untypedAtomic (attributes). Called after DeepCopyNode when Strip is in effect.
    /// </summary>
    private static void StripTypeAnnotations(NodeId rootId, INodeBuilder store)
    {
        if (store.GetNode(rootId) is not XdmElement elem) return;
        if (elem.TypeAnnotation != Xdm.XdmTypeName.Untyped)
        {
            var stripped = new XdmElement
            {
                Id = elem.Id,
                Document = elem.Document,
                Namespace = elem.Namespace,
                LocalName = elem.LocalName,
                Prefix = elem.Prefix,
                Attributes = elem.Attributes,
                Children = elem.Children,
                NamespaceDeclarations = elem.NamespaceDeclarations,
                BaseUri = elem.BaseUri,
                TypeAnnotation = Xdm.XdmTypeName.Untyped
            };
            stripped.Parent = elem.Parent;
            stripped._stringValue = elem._stringValue;
            store.RegisterNode(stripped);
            elem = stripped;
        }
        foreach (var attrId in elem.Attributes)
        {
            if (store.GetNode(attrId) is XdmAttribute attr
                && attr.TypeAnnotation != Xdm.XdmTypeName.UntypedAtomic)
            {
                var strippedAttr = new XdmAttribute
                {
                    Id = attr.Id,
                    Document = attr.Document,
                    Namespace = attr.Namespace,
                    LocalName = attr.LocalName,
                    Prefix = attr.Prefix,
                    Value = attr.Value,
                    TypeAnnotation = Xdm.XdmTypeName.UntypedAtomic,
                    IsId = attr.IsId
                };
                strippedAttr.Parent = attr.Parent;
                store.RegisterNode(strippedAttr);
            }
        }
        foreach (var childId in elem.Children)
        {
            StripTypeAnnotations(childId, store);
        }
    }

    private async Task<string> SerializeAsString(QueryExecutionContext context)
    {
        var sb = new StringBuilder();
        var name = !string.IsNullOrEmpty(Name.Prefix) ? $"{Name.Prefix}:{Name.LocalName}" : Name.LocalName;
        sb.Append('<').Append(name);

        // Serialize attributes
        foreach (var attrOp in AttributeOperators)
        {
            await foreach (var attrResult in attrOp.ExecuteAsync(context))
            {
                if (attrResult is XdmAttribute attr)
                {
                    var attrName = !string.IsNullOrEmpty(attr.Prefix) ? $"{attr.Prefix}:{attr.LocalName}" : attr.LocalName;
                    sb.Append(' ').Append(attrName).Append("=\"").Append(CharacterEscaper.EscapeXmlAttribute(attr.Value)).Append('"');
                }
            }
        }

        // Serialize content into a buffer to detect empty elements
        var contentBuf = new StringBuilder();
        foreach (var contentOp in ContentOperators)
        {
            await foreach (var contentResult in contentOp.ExecuteAsync(context))
            {
                if (contentResult is XdmElement contentElem)
                    contentBuf.Append(QueryExecutionContext.ComputeElementStringValue(contentElem, context.NodeProvider));
                else if (contentResult is XdmDocument contentDoc)
                    contentBuf.Append(QueryExecutionContext.ComputeDocumentStringValue(contentDoc, context.NodeProvider));
                else if (contentResult is XdmNode node)
                    contentBuf.Append(node.StringValue);
                else if (contentResult != null)
                {
                    var atomized = context.AtomizeWithNodes(contentResult);
                    contentBuf.Append(ConcatFunction.XQueryStringValue(atomized));
                }
            }
        }

        if (contentBuf.Length == 0)
        {
            sb.Append("/>");
        }
        else
        {
            sb.Append('>').Append(contentBuf).Append("</").Append(name).Append('>');
        }
        return sb.ToString();
    }

    /// <summary>
    /// Sentinel prefix used to mark a copied element as "no-inherit": in-scope-prefixes
    /// stops walking ancestors past an element carrying this marker.
    /// </summary>
    internal const string NoInheritMarkerPrefix = "\u0001no-inherit\u0001";

    /// <summary>
    /// Returns true when <paramref name="typeName"/> is a namespace-sensitive type — that is,
    /// <c>xs:QName</c>, <c>xs:NOTATION</c>, or any type derived from either.  Used to
    /// enforce XQTY0086: an attribute with such a type annotation may not appear in
    /// element content when <c>construction=preserve</c> is in effect.
    /// </summary>
    private static bool IsNamespaceSensitiveType(Xdm.XdmTypeName typeName)
    {
        // The XSD namespace URI is encoded via NamespaceId.Xsd; the same namespace is used
        // for XdmTypeName.QName.  We compare against the two base types directly and also
        // match any type whose local name equals "NOTATION" in the XSD namespace, covering
        // subtypes registered by XML Schema validation (e.g. xs:NOTATION subtypes).
        if (typeName == Xdm.XdmTypeName.QName)
            return true;
        // xs:NOTATION — no pre-built constant, but same XSD namespace as QName.
        if (typeName.Namespace == Xdm.XdmTypeName.QName.Namespace
            && typeName.LocalName == "NOTATION")
            return true;
        return false;
    }

    /// <summary>
    /// Applies copy-namespaces semantics (XQuery 3.1 §3.9.3.1) to a freshly copied
    /// element that is being inserted into a constructed element. The root of the
    /// copy has its NamespaceDeclarations adjusted according to the declared mode.
    /// When the inherit flag is in effect, enclosingBindings is consulted so that the
    /// copy picks up the new parent's visible namespaces. The copy's own bindings
    /// (from the source) win on prefix conflict.
    /// </summary>
    internal static void ApplyCopyNamespacesMode(
        NodeId copyRootId,
        INodeBuilder store,
        Analysis.CopyNamespacesMode mode,
        IReadOnlyDictionary<string, string>? enclosingBindings = null)
    {
        if (store.GetNode(copyRootId) is not XdmElement root)
            return;

        bool preserve = mode == Analysis.CopyNamespacesMode.PreserveInherit
                     || mode == Analysis.CopyNamespacesMode.PreserveNoInherit;
        bool inherit = mode == Analysis.CopyNamespacesMode.PreserveInherit
                    || mode == Analysis.CopyNamespacesMode.NoPreserveInherit;

        if (!preserve)
        {
            // no-preserve: strip unused namespace declarations from ALL elements in the copy
            // (not just the root). Each element keeps only bindings used by itself or its
            // descendants (and xml).
            StripUnusedNamespacesRecursive(root, store);
            root = (XdmElement)store.GetNode(copyRootId)!;
        }

        var current = root.NamespaceDeclarations?.ToList() ?? new List<NamespaceBinding>();
        bool rootModified = false;

        if (inherit && enclosingBindings != null && enclosingBindings.Count > 0)
        {
            // Inherit: add the enclosing constructor's in-scope bindings to the copy root
            // AND all descendant elements. Each element's own bindings win on prefix conflict.
            var presentPrefixes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var nb in current)
                presentPrefixes.Add(nb.Prefix);
            if (!string.IsNullOrEmpty(root.Prefix))
                presentPrefixes.Add(root.Prefix);

            var inheritedBindings = new List<NamespaceBinding>();
            foreach (var (prefix, uri) in enclosingBindings)
            {
                if (prefix == "##default-element") continue;
                if (prefix == "xml") continue;
                if (string.IsNullOrEmpty(prefix) && string.IsNullOrEmpty(uri)) continue;
                if (IsPredefinedBuiltinNamespace(prefix, uri)) continue;

                NamespaceId nsId = string.IsNullOrEmpty(uri)
                    ? NamespaceId.None
                    : store.InternNamespace(uri);

                // Add to root's declarations (via `current`)
                if (!presentPrefixes.Contains(prefix))
                {
                    // When inheriting the default namespace from an enclosing constructor,
                    // if the copied element is in no namespace, add an xmlns="" undeclaration
                    // instead. Without this, the parent's default namespace would leak through
                    // and the serializer would wrongly emit xmlns="..." on the copied element.
                    if (string.IsNullOrEmpty(prefix) && root.Namespace == NamespaceId.None)
                    {
                        current.Add(new NamespaceBinding("", NamespaceId.None));
                    }
                    else
                    {
                        current.Add(new NamespaceBinding(prefix, nsId));
                    }
                    presentPrefixes.Add(prefix);
                    rootModified = true;
                }
                inheritedBindings.Add(new NamespaceBinding(prefix, nsId));
            }

            // Propagate inherited bindings to all descendant elements
            if (inheritedBindings.Count > 0)
            {
                foreach (var childId in root.Children)
                {
                    if (store.GetNode(childId) is XdmElement childElem)
                        PropagateInheritedNamespaces(childElem, store, inheritedBindings);
                }
            }
        }

        if (!inherit)
        {
            // no-inherit: the copy must not see the surrounding constructor's
            // in-scope namespaces. Insert a sentinel that fn:in-scope-prefixes
            // (and any other walker) recognizes as a walk-stop marker.
            current.Add(new NamespaceBinding(NoInheritMarkerPrefix, NamespaceId.None));
            rootModified = true;
        }

        // If no-preserve was applied, StripUnusedNamespacesRecursive already rewrote the root;
        // we need to re-register with the updated declarations if we modified further.
        if (!rootModified && preserve)
            return;

        // Replace the copy root's namespace declarations.
        var newRoot = new XdmElement
        {
            Id = root.Id,
            Document = root.Document,
            Namespace = root.Namespace,
            LocalName = root.LocalName,
            Prefix = root.Prefix,
            Attributes = root.Attributes,
            Children = root.Children,
            NamespaceDeclarations = current,
            TypeAnnotation = root.TypeAnnotation
        };
        newRoot.Parent = root.Parent;
        newRoot._stringValue = root._stringValue;
        store.RegisterNode(newRoot);
    }

    /// <summary>
    /// Propagates inherited namespace bindings to an element and all its descendant elements.
    /// Each element's own bindings win on prefix conflict.
    /// </summary>
    private static void PropagateInheritedNamespaces(
        XdmElement elem, INodeBuilder store, List<NamespaceBinding> inheritedBindings)
    {
        var presentPrefixes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var nb in elem.NamespaceDeclarations)
            presentPrefixes.Add(nb.Prefix);
        if (!string.IsNullOrEmpty(elem.Prefix))
            presentPrefixes.Add(elem.Prefix);

        var newDecls = elem.NamespaceDeclarations.ToList();
        bool modified = false;
        foreach (var nb in inheritedBindings)
        {
            if (!presentPrefixes.Contains(nb.Prefix))
            {
                newDecls.Add(nb);
                presentPrefixes.Add(nb.Prefix);
                modified = true;
            }
        }

        if (modified)
        {
            var newElem = new XdmElement
            {
                Id = elem.Id, Document = elem.Document, Namespace = elem.Namespace,
                LocalName = elem.LocalName, Prefix = elem.Prefix,
                Attributes = elem.Attributes, Children = elem.Children,
                NamespaceDeclarations = newDecls, TypeAnnotation = elem.TypeAnnotation
            };
            newElem.Parent = elem.Parent;
            newElem._stringValue = elem._stringValue;
            store.RegisterNode(newElem);
        }

        // Recurse into child elements
        foreach (var childId in elem.Children)
        {
            if (store.GetNode(childId) is XdmElement childElem)
                PropagateInheritedNamespaces(childElem, store, inheritedBindings);
        }
    }

    /// <summary>
    /// Returns true if the given prefix/URI binding is a predefined built-in namespace
    /// (e.g., fn, xs, xsi, math, map, array, local, err). These are statically known for
    /// name resolution but are not treated as in-scope namespace nodes on element copies.
    /// </summary>
    private static bool IsPredefinedBuiltinNamespace(string prefix, string uri)
    {
        return uri switch
        {
            "http://www.w3.org/2005/xpath-functions" => prefix == "fn",
            "http://www.w3.org/2001/XMLSchema" => prefix == "xs",
            "http://www.w3.org/2001/XMLSchema-instance" => prefix == "xsi",
            "http://www.w3.org/2005/xpath-functions/math" => prefix == "math",
            "http://www.w3.org/2005/xpath-functions/map" => prefix == "map",
            "http://www.w3.org/2005/xpath-functions/array" => prefix == "array",
            "http://www.w3.org/2005/xquery-local-functions" => prefix == "local",
            "http://www.w3.org/2005/xqt-errors" => prefix == "err",
            _ => false,
        };
    }

    /// <summary>
    /// Recursively strips unused namespace declarations from every element in a copy tree.
    /// Per no-preserve semantics, each element retains only bindings whose prefix is used
    /// by the element itself or its attributes (and xml).
    /// </summary>
    private static void StripUnusedNamespacesRecursive(XdmElement elem, INodeBuilder store)
    {
        // First recurse into children
        foreach (var childId in elem.Children)
        {
            if (store.GetNode(childId) is XdmElement childElem)
                StripUnusedNamespacesRecursive(childElem, store);
        }

        // Collect prefixes used by this element and its descendants
        var usedPrefixes = new HashSet<string>();
        CollectUsedPrefixes(elem, store, usedPrefixes);

        var current = elem.NamespaceDeclarations?.ToList();
        if (current == null || current.Count == 0)
            return;

        var filtered = current
            .Where(b => usedPrefixes.Contains(b.Prefix) || b.Prefix == "xml")
            .ToList();

        if (filtered.Count == current.Count)
            return; // Nothing stripped

        var newElem = new XdmElement
        {
            Id = elem.Id,
            Document = elem.Document,
            Namespace = elem.Namespace,
            LocalName = elem.LocalName,
            Prefix = elem.Prefix,
            Attributes = elem.Attributes,
            Children = elem.Children,
            NamespaceDeclarations = filtered,
            TypeAnnotation = elem.TypeAnnotation
        };
        newElem.Parent = elem.Parent;
        newElem._stringValue = elem._stringValue;
        store.RegisterNode(newElem);
    }

    private static void CollectUsedPrefixes(XdmElement elem, INodeProvider store, HashSet<string> used)
    {
        used.Add(elem.Prefix ?? "");
        foreach (var attrId in elem.Attributes)
        {
            if (store.GetNode(attrId) is XdmAttribute a)
            {
                // Only truly-prefixed attributes contribute (unprefixed attrs are in no namespace)
                if (!string.IsNullOrEmpty(a.Prefix))
                    used.Add(a.Prefix);
            }
        }
        foreach (var childId in elem.Children)
        {
            if (store.GetNode(childId) is XdmElement childElem)
                CollectUsedPrefixes(childElem, store, used);
        }
    }

    /// <summary>
    /// Recursively flattens XDM arrays (List&lt;object?&gt;) into individual items.
    /// Per XQuery 3.1 §3.7.3.1: arrays in element content are replaced by their members.
    /// </summary>
    private static IEnumerable<object?> FlattenArrayMembers(List<object?> array)
    {
        foreach (var member in array)
        {
            if (member is List<object?> nested)
            {
                foreach (var item in FlattenArrayMembers(nested))
                    yield return item;
            }
            else if (member is object?[] seqArr)
            {
                foreach (var item in seqArr)
                {
                    if (item is List<object?> innerArr)
                    {
                        foreach (var sub in FlattenArrayMembers(innerArr))
                            yield return sub;
                    }
                    else
                        yield return item;
                }
            }
            else
            {
                yield return member;
            }
        }
    }

    internal static NodeId DeepCopyNode(XdmNode source, INodeBuilder store, DocumentId docId, NodeId? parentId)
        => DeepCopyNode(source, store, docId, parentId, isRoot: true);

    internal static NodeId DeepCopyNode(XdmNode source, INodeBuilder store, DocumentId docId, NodeId? parentId, bool isRoot)
    {
        var newId = store.AllocateId();

        switch (source)
        {
            case XdmElement elem:
            {
                var newAttrs = new List<NodeId>();
                var newChildren = new List<NodeId>();

                // Materialize in-scope namespaces at the copy root so the constructed tree
                // carries bindings that originally came from ancestors of the source.
                // Per XQuery 3.1 copy-namespaces preserve/inherit default.
                IReadOnlyList<NamespaceBinding> nsDeclsCopy = elem.NamespaceDeclarations;
                if (isRoot)
                {
                    var merged = new Dictionary<string, NamespaceId>(StringComparer.Ordinal);
                    // Start with ancestors (walked from element to root), innermost wins — so
                    // we walk FROM the root DOWN. Use a stack.
                    var chain = new List<XdmElement>();
                    XdmNode? cursor = elem;
                    while (cursor != null)
                    {
                        if (cursor is XdmElement ce) chain.Add(ce);
                        cursor = cursor.Parent.HasValue ? store.GetNode(cursor.Parent.Value) as XdmNode : null;
                    }
                    // chain is [self, parent, grandparent, ...] — process from outermost.
                    for (int i = chain.Count - 1; i >= 0; i--)
                    {
                        var ce = chain[i];
                        if (ce.NamespaceDeclarations != null)
                        {
                            foreach (var nd in ce.NamespaceDeclarations)
                            {
                                if (nd.Prefix == NoInheritMarkerPrefix) continue;
                                merged[nd.Prefix] = nd.Namespace;
                            }
                        }
                        if (!string.IsNullOrEmpty(ce.Prefix) && ce.Namespace != NamespaceId.None)
                            merged.TryAdd(ce.Prefix, ce.Namespace);
                        else if (string.IsNullOrEmpty(ce.Prefix) && ce.Namespace != NamespaceId.None)
                            merged.TryAdd("", ce.Namespace);
                    }
                    // Drop "xml" — it's implicit and never serialized.
                    merged.Remove("xml");
                    // Build namespace declarations list from merged bindings.
                    // Keep xmlns="" (default namespace undeclaration) if the element itself
                    // explicitly declared it — the copy may be placed inside a parent with
                    // a default namespace and needs the undeclaration for correct serialization.
                    var list = new List<NamespaceBinding>(merged.Count);
                    foreach (var kv in merged)
                    {
                        // Drop empty-prefix/empty-URI only if it came purely from ancestor
                        // inheritance (the element itself has no such declaration). If the
                        // source element explicitly has xmlns="", keep it.
                        if (string.IsNullOrEmpty(kv.Key) && kv.Value == NamespaceId.None)
                        {
                            // Keep if the source element itself had this undeclaration
                            bool selfHasUndecl = elem.NamespaceDeclarations != null &&
                                elem.NamespaceDeclarations.Any(nd => string.IsNullOrEmpty(nd.Prefix) && nd.Namespace == NamespaceId.None);
                            if (!selfHasUndecl) continue;
                        }
                        list.Add(new NamespaceBinding(kv.Key, kv.Value));
                    }
                    nsDeclsCopy = list;
                }

                var newElem = new XdmElement
                {
                    Id = newId,
                    Document = docId,
                    Namespace = elem.Namespace,
                    LocalName = elem.LocalName,
                    Prefix = elem.Prefix,
                    Attributes = newAttrs,
                    Children = newChildren,
                    NamespaceDeclarations = nsDeclsCopy,
                    TypeAnnotation = elem.TypeAnnotation  // preserve schema-derived type through deep copy
                };
                newElem.Parent = parentId;
                store.RegisterNode(newElem);

                foreach (var attrId in elem.Attributes)
                {
                    if (store.GetNode(attrId) is XdmAttribute attr)
                    {
                        var newAttrId = store.AllocateId();
                        var newAttr = new XdmAttribute
                        {
                            Id = newAttrId,
                            Document = docId,
                            Namespace = attr.Namespace,
                            LocalName = attr.LocalName,
                            Prefix = attr.Prefix,
                            Value = attr.Value,
                            TypeAnnotation = attr.TypeAnnotation,
                            IsId = attr.IsId
                        };
                        newAttr.Parent = newId;
                        store.RegisterNode(newAttr);
                        newAttrs.Add(newAttrId);
                    }
                }

                foreach (var childId in elem.Children)
                {
                    var child = store.GetNode(childId);
                    if (child != null)
                    {
                        var childCopyId = DeepCopyNode(child, store, docId, newId, isRoot: false);
                        newChildren.Add(childCopyId);
                    }
                }

                // Compute string value for the copy
                newElem._stringValue = elem._stringValue ?? ComputeStringValueFromChildren(newChildren, store);

                return newId;
            }

            case XdmText text:
            {
                var newText = new XdmText
                {
                    Id = newId,
                    Document = docId,
                    Value = text.Value
                };
                newText.Parent = parentId;
                store.RegisterNode(newText);
                return newId;
            }

            case XdmComment comment:
            {
                var newComment = new XdmComment
                {
                    Id = newId,
                    Document = docId,
                    Value = comment.Value
                };
                newComment.Parent = parentId;
                store.RegisterNode(newComment);
                return newId;
            }

            case XdmProcessingInstruction pi:
            {
                var newPi = new XdmProcessingInstruction
                {
                    Id = newId,
                    Document = docId,
                    Target = pi.Target,
                    Value = pi.Value
                };
                newPi.Parent = parentId;
                store.RegisterNode(newPi);
                return newId;
            }

            default:
            {
                // Fallback for any other node type: create a text node from its string value
                var newText = new XdmText
                {
                    Id = newId,
                    Document = docId,
                    Value = source.StringValue ?? ""
                };
                newText.Parent = parentId;
                store.RegisterNode(newText);
                return newId;
            }
        }
    }

}
