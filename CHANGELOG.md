# Changelog

## Unreleased

### QT3 Conformance: 82.3% → 85.1% (+2.8pp)

### Features
- format-number with full decimal-format support (custom separators, exponent notation)
- Direct PI/comment constructors at expression level and in element content
- CDATA sections in element content
- Pragma/extension expression parser
- validate expression (raises XQST0075 for schema-aware features)
- fn:outermost/fn:innermost proper implementation
- fn:lang with xml:lang ancestor traversal
- map:merge#2 with duplicates option
- instance-of integer subtype range checking
- parse-ietf-date timezone handling
- Recursion depth configurable as security boundary
- Grammar audit: complete XQuery 3.1 coverage documented (GRAMMAR-AUDIT.md)

### Fixes
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
