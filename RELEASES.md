# Release History

## Unreleased

### QT3 Conformance: 82.3% → 99.6% (+17.3pp, ~4,500 tests)

26,064 of 26,175 tests passing (99.58%). Remaining ~111 failures are in parser leniency (ANTLR grammar), XSD type hierarchy wrappers, XPath 4.0 xs:numeric, negative-year dates, and schema-aware features.

### Features
- **format-number with decimal-format**: Full rewrite with custom decimal-separator, grouping-separator, digit, pattern-separator, infinity, NaN, minus-sign, percent, per-mille, zero-digit, exponent-separator. Wire `declare decimal-format` prolog through optimizer to runtime.
- **Direct PI and comment constructors**: `<?target data?>` and `<!-- comment -->` now valid as primary expressions and inside element content. CDATA sections `<![CDATA[...]]>` supported.
- **Pragma/extension expressions**: `(# pragma-name content #) { expr }` parsed and evaluated (pragmas ignored, body returned).
- **validate expression**: `validate strict/lax/type { expr }` parsed; raises XQST0075 since we're not schema-aware.
- **fn:outermost / fn:innermost**: Proper ancestor/descendant filtering (was returning input unchanged).
- **fn:lang**: Ancestor xml:lang attribute traversal (was returning false unconditionally).
- **map:merge#2**: 2-argument form with duplicates option (use-first, use-last, combine, reject).
- **instance-of integer subtypes**: Range checking for xs:int, xs:short, xs:byte, xs:long, xs:unsignedInt, etc.
- **Annotations on inline functions and function types**: `%public function(){}`, `%ann function(*)`.
- **parse-ietf-date timezone handling**: GMT/UT/UTC/EST/EDT/CST/CDT/MST/MDT/PST/PDT timezone suffix parsing.
- **Recursion depth as security boundary**: Configurable via `QueryExecutionLimits.MaxRecursionDepth` (default 1000). Documented as deliberate DoS protection, not conformance gap.
- **Grammar audit**: GRAMMAR-AUDIT.md documents complete XQuery 3.1 coverage with 14 identified 4.0 extensions.

### Fixes (recent)
- **xs:error type support**: Full XSD 1.1 empty union type — `instance of`, `cast as`, `xs:error()` constructor. No value is ever an instance.
- **Function return type checking**: User-declared functions now enforce return type via function coercion rules. Comment/PI nodes correctly atomize to `xs:string` (not `xs:untypedAtomic`), preventing invalid coercion. Element name constraints (`element(foo)`) validated.
- **Function argument cardinality**: Empty sequence passed to `ExactlyOne`/`OneOrMore` parameter now raises XPTY0004.
- **AST walker completeness**: `XQueryExpressionWalker` now walks text, comment, PI, computed element/attribute, document, and namespace constructors. Previously function bodies containing constructors were not checked for undefined function references (XPST0017).
- **Computed attribute auto-prefix**: Attributes with namespace URI but no prefix now generate `ns0`, `ns1`, etc. prefixes automatically.
- **EQName function namespace resolution**: `StaticAnalyzer` XQST0045 check now resolves `ExpandedNamespace` first for EQName function declarations.
- **xs:unsignedLong BigInteger**: Values > `long.MaxValue` (e.g., 18446744073709551615) now use `BigInteger` instead of overflowing.
- **WhereClause EBV**: Multi-item sequences starting with non-node values now correctly raise FORG0006.
- **Conformance test safety limit**: Raised from 100K to 1M items for large-map tests.

### Fixes (earlier)
- **Critical: namespace resolver in CreateContext**: `QueryEngine.CreateContext()` was missing the namespace resolver that `ExecuteAsync()` had. This caused ALL namespace-qualified path expressions to fail when using `CreateContext()` + manual execution (the test runner's path). Fixed ~80+ tests.
- **Critical: boundary whitespace between expressions**: `<elem>{1} {2}</elem>` with boundary-space strip produced `1 2` instead of `12`. Atomic values from different enclosed expressions no longer get space-separated.
- **Critical: XQueryStringValue in constructors**: `<elem>{true()}</elem>` produced `<elem>True</elem>` because C# `bool.ToString()` uses PascalCase. All atomized values in element/attribute/PI constructors now use XQuery canonical string representation.
- **NullRef in empty enclosed expressions**: `function(){}`, `try {} catch * {..}`, `document {}`, `attribute name {}` all NullRef'd. Added `VisitEnclosedExprSafe` helper for safe handling throughout.
- **Computed attribute EQName**: `attribute Q{uri}name {}` lost the namespace URI in AST building.
- **adjust-date/dateTime-to-timezone return types**: Was returning `DateTimeOffset` instead of `XsDate`/`XsDateTime`, causing wrong string serialization format.
- **Try-catch namespace matching**: Catch clause now checks namespace prefix/URI; `$err:code` QName uses proper namespace.
- **XDM arrays yield as single items**: `List<object?>` from functions like `parse-json` now correctly yields as a single XDM array item.
- **Schema/spec dependency filtering**: Correctly exclude schema-dependent tests; reject XQ30-only tests (without +).
- **format-integer picture validation**: FODF1310 for empty/invalid pictures.
- **Empty enclosed expressions in attribute values**: `attr="z{}z"` now valid per XQuery 3.1.

### FLWOR window clauses `for tumbling window` and `for sliding window` now fully supported. Includes start/end conditions with `$cur`, `$prev`, `$next`, `$pos` variables, cross-referencing start variables from end conditions (e.g., `end at $epos when $epos - $spos eq 2`), and proper window flushing at end of sequence.

### Fixes
- **FLWOR `group by` not aggregating tuples**: each item produced its own group instead of merging items with equal keys. Root cause: `GroupByClauseOperator.ExecuteAsync()` was a stub that yielded an empty dict, and `FlworOperator.ExecuteClausesAsync()` had no special handling for group-by barriers. Fixed by redesigning clause evaluation to detect barrier clauses (order by, group by) ahead of time, materialize all upstream tuples via `MaterializeUpToAsync`, then apply `GroupTuplesAsync` which groups by key values and aggregates non-key variables into sequences per the XQuery 3.1 spec.
- **FLWOR `order by` not sorting with multiple keys**: items returned in original iteration order because the sort detection happened too late in the recursive clause processing — by the time order by was detected, the for-loop had already distributed iterations one-at-a-time to downstream clauses, so each "sort" operated on a single item. Fixed with the same barrier-clause architecture: tuples from all preceding clauses are fully materialized before `SortTuplesAsync` is applied.
- **Namespace resolution on direct element constructors**: `<ns:child>` in constructed elements had `NamespaceId.None` — `namespace-uri()`, `local-name()`, `name()` returned empty, and prefixed XPath navigation (`$doc/ns:child`) failed. Three bugs: (1) `NamespaceResolver.VisitElementConstructor` validated prefixes but never set the resolved `NamespaceId` on the QName. (2) The `XQueryExpressionRewriter` base class doesn't recursively visit children inside element `Content`, so inner elements were never namespace-resolved. (3) `ElementConstructorOperator` resolved namespace URIs through the store's pool (different IDs from the static analyzer's pool used by XPath NameTests). Fixed by: updating `VisitElementConstructor` to scan `xmlns:` attributes, resolve prefixed names, and manually recurse into children; using the static analyzer's `NamespaceId` on constructed elements; adding `XdmDocumentStore.RegisterNamespace()` to bridge the ID pools for serialization.
- **`fn:serialize()` broken for nodes**: returned atomized text value (`StringValue`) instead of XML markup. `serialize(<item>42</item>)` returned `42` instead of `<item>42</item>`. Rewrote to walk the node tree via `INodeProvider` and produce proper XML serialization.
- **`fn:serialize()` broken for maps/arrays**: returned .NET `Dictionary.ToString()` instead of JSON. Added proper JSON serialization for maps and arrays.
- **`sort#2` and `sort#3` not registered**: only `sort#1` (no collation, no key function) existed. Added `Sort2Function` (with collation) and `Sort3Function` (with collation + key function). Very commonly used — `sort($seq, (), $key-fn)` was not available.
- **`namespace-uri()` on EQName-constructed elements**: `element Q{urn:test}foo {}` — `namespace-uri()` returned empty. Two bugs: (1) the computed element constructor's parser discarded the namespace URI from EQNames, keeping only the local name. Fixed by preserving the full `Q{uri}local` form. (2) The `ComputedElementConstructorOperator` atomized the name to string, losing QName namespace info. Fixed to parse `Q{uri}local` string form and set `ExpandedNamespace`. (3) `ResolveNsId` now falls back to `XdmDocumentStore.ResolveNamespaceUri` when no explicit resolver is set.
- **XQuery `group by` with inline variable binding**: `group by $g := expr` gave "Variable $g is not defined". Fixed: inline binding variables are now properly declared in the group-by scope.
- **XQuery `otherwise` operator**: Both standalone (`expr otherwise expr`) and FLWOR (`for ... return expr otherwise fallback`) forms now work. The standalone binary operator was parsed but threw "Unsupported operator" at runtime because `BinaryOperatorOperator.ExecuteAsync` had no case for it. Fixed with short-circuit evaluation: materialize left sequence, yield it if non-empty, otherwise evaluate and yield right sequence.

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
