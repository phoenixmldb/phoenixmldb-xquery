# Release History

## Unreleased

### Fixes
- **`fn:serialize()` broken for nodes**: returned atomized text value (`StringValue`) instead of XML markup. `serialize(<item>42</item>)` returned `42` instead of `<item>42</item>`. Rewrote to walk the node tree via `INodeProvider` and produce proper XML serialization.
- **`fn:serialize()` broken for maps/arrays**: returned .NET `Dictionary.ToString()` instead of JSON. Added proper JSON serialization for maps and arrays.
- **`sort#2` and `sort#3` not registered**: only `sort#1` (no collation, no key function) existed. Added `Sort2Function` (with collation) and `Sort3Function` (with collation + key function). Very commonly used — `sort($seq, (), $key-fn)` was not available.
- **`namespace-uri()` on EQName-constructed elements**: `element Q{urn:test}foo {}` — `namespace-uri()` returned empty. Two bugs: (1) the computed element constructor's parser discarded the namespace URI from EQNames, keeping only the local name. Fixed by preserving the full `Q{uri}local` form. (2) The `ComputedElementConstructorOperator` atomized the name to string, losing QName namespace info. Fixed to parse `Q{uri}local` string form and set `ExpandedNamespace`. (3) `ResolveNsId` now falls back to `XdmDocumentStore.ResolveNamespaceUri` when no explicit resolver is set.
- **XQuery `group by` with inline variable binding**: `group by $g := expr` gives "Variable $g is not defined".
- **XQuery `otherwise` operator**: parsed but evaluator throws "Unsupported operator".
- **XQuery window clauses**: `for tumbling window` / `for sliding window` not supported.

## 1.1.0 (2026-03-26)

### Features
- **Library module imports**: `import module namespace ns = "uri" at "location"` — parses library modules, resolves relative location hints against query base URI, registers imported functions/variables. Three bugs fixed: missing `VisitFunctionDecl`/`VisitVarDecl` overrides in AST builder (ANTLR base visitor returned null), missing `ModuleImportExpression` optimizer mapping, imported function bodies not injected into execution plan.
- **Separate query/document base URIs in XQueryFacade**: new `queryBaseUri` parameter for module resolution and `fn:static-base-uri()`, independent from `baseUri` (input document URI). Falls back to `baseUri` when not set.
- **`fn:static-base-uri()`**: now returns query file URI (CLI) or caller-provided `queryBaseUri` (API). Was always returning empty sequence because `StaticBaseUri` was never set on the execution context.
- **Function library isolation**: `FunctionLibrary.Copy()` creates per-compilation copies so user-defined function registrations don't leak across queries via the static `FunctionLibrary.Standard` singleton.

### Fixes
- **Element atomization**: `XdmElement.StringValue` was never computed — `_stringValue` now set at parse time (bottom-up via `ComputeStringValue`) and on constructed elements via `ElementConstructorOperator`. Required `InternalsVisibleTo` from Core.
- **NodeId.None collision**: document nodes started at ID 0 which equals `NodeId.None`, breaking parent chain checks. Fixed by starting `_nextNodeIdBase` at 1.
- **file:// URI resolution**: `doc()` and `doc-available()` passed raw `file:///` URIs to `File.Exists()`. Added `ToLocalPath` conversion.
- **Arithmetic identity optimization**: `$el + 0` was optimized to just `$el`, skipping atomization for untyped elements. Restricted to numeric literal operands only.
- **current-dateTime() stale values**: shared static `CurrentDateTimeSnapshot` returned same value across all evaluations. Now reads from per-context `CurrentDateTime`.
- **Boundary whitespace**: `<root> { expr } </root>` preserved whitespace text nodes. Now stripped per XQuery 3.1 §3.7.1.4.
- **Double/float serialization**: CLI `ResultSerializer` used `ToString()` instead of `FormatDoubleXPath`. Added explicit double/float/bool cases.
- **Order by sort keys**: not atomized, causing string comparisons on node references. Fixed with `context.AtomizeWithNodes`.
- **fn:trace()**: only supported arity 2. Added arity 1 support. Output changed from `Debug.WriteLine` (invisible) to stderr.

## 1.0.0 (2026-03-20)

Initial release: XPath/XQuery 4.0 engine with 76 new functions, Full-Text Search (Lucene.NET), Update Facility 3.0, JSON support, and `xquery4` CLI tool.
