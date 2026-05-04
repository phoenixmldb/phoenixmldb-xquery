# Changelog

## 1.2.1 (2026-04-29)

### Fixes
- Fix `fn:serialize($input)` (1-arg) and `fn:serialize($input, map { 'method': 'adaptive' })` producing JSON instead of adaptive output. Per XPath/XQuery 3.1 §17.1.3 the default serialization method is `adaptive`. The fallback `SerializeItem` path (used when nodes don't come from `XdmDocumentStore`, including all of XSLT) was hard-coded to JSON. Now routes maps as `map{key:value,…}`, arrays as `[…]`, sequences as `(…)`, atomic types in their constructor form (e.g. `xs:date("2025-01-01")`), and nodes via the existing XML node serializer. The 1-arg form defaults to adaptive; the 2-arg form threads `method=` through to the fallback path. Reported by Martin Honnen.

## Unreleased

### QT3 Conformance: 82.3% → 86.5%+ (+4.2pp)

### Recent gains
- Schema import recognized as namespace binding; validate{} pass-through; validate strict|lax|type X allowed
- User function declared return-type enforced via function conversion rules (atomize, UA cast with whitespace trim, numeric promotion); XPTY0004 on mismatch for atomic targets
- XQST0045 raised for user functions declared in reserved namespaces (xml/xs/xsi/fn/math/map/array)
- gYear/gYearMonth preserve negative years; gMonthDay/gDay/gMonth carry timezone in serialization
- Computed attribute/element constructors: non-singleton + NCName validation (XPTY0004 / XQDY0044 / XQDY0074)
- try/catch matches user-raised error namespace (fn:error with fn:QName)
- Direct element constructor: lexical default-namespace inheritance with sibling save/restore
- format-number returns NaN for non-numeric strings instead of throwing

### Features
- **ISchemaProvider extensibility, no commercial gating.** `XsdSchemaProvider` (System.Xml.Schema-backed; XSD validation, type hierarchy, substitution groups) is now part of the main `PhoenixmlDb.XQuery` package and is auto-registered on every `QueryEngine`. Custom schema languages (RelaxNG, Schematron-derived, in-memory) can be plugged in via `ISchemaProvider` and `schemaProvider:` constructor parameter. Sidecar package `PhoenixmlDb.XQuery.Schema` removed — the previous "free vs commercial" gating (XQST0075 stub when no provider was registered) is gone.
- `validate strict/lax/type {expr}` end-to-end (parser → analysis → optimizer → ValidateOperator) — runs against the default provider out of the box
- `schema-element(Name)` / `schema-attribute(Name)` in steps and sequence types (instance-of, treat-as) — XPST0008 fires when the registered provider lacks the declaration (spec-correct behavior, not a packaging gate). Runtime `instance of schema-element(...)` / `treat as schema-element(...)` now route through the provider so substitution-group members and elements with schema type annotations match correctly.
- ISchemaProvider gains string-URI overloads (`HasElementDeclaration(string namespaceUri, string localName)`, `GetElementType`, `MatchesSchemaElement`, etc.) so callers don't have to round-trip arbitrary namespace URIs through `NamespaceId` (which is lossy for non-built-in namespaces). Default-implemented on the interface for back-compat; XsdSchemaProvider overrides them with direct `XmlQualifiedName` lookups.
- ISchemaProvider gains `ValidateXml(string xmlContent, ValidationMode mode, ...)` for callers that have already-serialized XML in hand (e.g. XSLT's `xsl:result-document validation="strict|lax"`). XsdSchemaProvider implements it via a validating `XmlReader`; default interface implementation throws so custom providers see a clear error if they don't override.
- ISchemaProvider gains `ValidateXmlFragment(string xmlFragment, ValidationMode mode, ..., IReadOnlyDictionary<string, string>? inScopeNamespaces = null)` for fragment-mode validation against the loaded schemas. The optional `inScopeNamespaces` argument carries prefix→URI bindings declared on enclosing elements that the caller can't fold into the fragment text (XSLT's per-element validation needs this when stylesheet-root prefixes aren't repeated on the constructed element). XsdSchemaProvider wires those into an `XmlParserContext` so `XmlReader` resolves them.
- XsdSchemaProvider QName-based lookups (`HasElementDeclaration(XdmQName)` etc.) now correctly resolve user-defined namespaces, not just the four built-in URIs. Internal NamespaceId↔URI map populates when schemas load and when type names are surfaced. URI-string overloads remain the recommended path; the QName overloads are the back-compat surface.
- `import schema` is now wired through `ISchemaProvider.ImportSchema` during static analysis — surfaces real XQST0059 schema-locate errors
- format-number with full decimal-format support (custom separators, exponent notation)
- Direct PI/comment constructors at expression level and in element content
- CDATA sections in element content
- Pragma/extension expression parser
- fn:outermost/fn:innermost proper implementation
- fn:lang with xml:lang ancestor traversal
- map:merge#2 with duplicates option
- instance-of integer subtype range checking
- parse-ietf-date timezone handling
- Recursion depth configurable as security boundary
- Grammar audit: complete XQuery 3.1 coverage documented (GRAMMAR-AUDIT.md)

### Fixes
- CRITICAL: prefixed atomic types in `cast`/`castable`/`instance of` (e.g. `castable as xs:integer`) wrongly raised XPST0051 in XSLT — `XdmSequenceType.UnprefixedTypeName` now only set when the source name was actually unprefixed; new `LocalTypeName` field carries the local-name component used by derived-integer range checks and string-subtype normalization. Reported against DocBook xslTNG and Schxslt2 transpile.xsl.
- Add `XQueryParserFacade.AllowNamespaceAxis` opt-in so XSLT/XPath callers can use the deprecated-but-optional `namespace::` axis without tripping XQuery's XQST0134. XQuery callers retain the strict default (XQST0134 still raised).
- CRITICAL: namespace resolver missing from CreateContext() — broke all namespace-qualified paths
- CRITICAL: boundary whitespace between expressions — space between different {expr} removed
- CRITICAL: XQueryStringValue for atomized values in constructors — correct bool/double/date formatting
- NullRef in empty enclosed expressions (function(){}, try{}, document{}, attribute name{})
- Computed attribute EQName namespace preservation
- adjust-date/dateTime-to-timezone now returns XsDate/XsDateTime (was DateTimeOffset)
- Try-catch namespace matching, $err:code QName namespace
- XDM arrays yield as single items from function calls
- Schema/spec dependency filtering for test runner
- Empty enclosed expressions in attribute values
- Implement FLWOR `for tumbling window` and `for sliding window` clauses with start/end conditions

### Fixes
- Fix FLWOR `group by` not aggregating tuples — was a stub; now properly groups by key and merges non-key variables into sequences
- Fix FLWOR `order by` not sorting with multiple keys — barrier-clause architecture collects all tuples before sorting
- Fix namespace resolution on direct element constructors — `namespace-uri()`, `local-name()`, `name()` now work on `<ns:elem>` constructed elements, and prefixed XPath navigation (`$doc/ns:child`) finds namespaced children
- Fix `fn:serialize()` for nodes (returns XML markup) and maps/arrays (returns JSON)
- Fix `sort#2` and `sort#3` not registered
- Fix `namespace-uri()` on EQName-constructed elements

## 1.1.0 (2026-03-26)

### Features
- **Library module imports**: `import module namespace ns = "uri" at "location"` now fully works — parses library modules, resolves relative location hints against the query base URI, and registers imported functions/variables for execution
- **Separate query/document base URIs**: `XQueryFacade` now accepts `queryBaseUri` (for module resolution and `fn:static-base-uri()`) independently from `baseUri` (for input document URI resolution)
- **`fn:static-base-uri()`**: now returns the query file URI (CLI) or the caller-provided `queryBaseUri` (API) — was previously always empty
- **Function library isolation**: each compilation gets its own function library copy so user-defined registrations don't leak across queries

### Fixes
- Fix element atomization returning empty string — `_stringValue` now computed at parse time and set on constructed elements
- Fix `NodeId.None` collision — document node IDs start at 1 instead of 0
- Fix `file://` URI resolution in `doc()` and `doc-available()`
- Fix arithmetic identity optimization (`x + 0 -> x`) skipping atomization for untyped elements
- Fix `current-dateTime()`/`current-date()`/`current-time()` returning stale cached values across queries
- Fix boundary whitespace stripping in direct element constructors
- Fix double/float serialization in CLI (`FormatDoubleXPath`)
- Fix order by sort keys not being atomized
- Fix `fn:trace()` arity 1 support and output to stderr

## 1.0.0 (2026-03-20)

Initial release with XPath/XQuery 4.0 support, Full-Text Search, Update Facility, and CLI tool.
