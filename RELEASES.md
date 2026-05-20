# Release History

## 1.3.14 (2026-05-20)

### `fn:load-xquery-module`: transitively-imported functions now resolve (Martin Honnen)

When `fn:load-xquery-module` loads a module M2 that itself imports M1, calling
M2's `f2:bar` from the outer engine (e.g. XSLT) used to crash with
`Placeholder for declared function foo invoked at runtime` because the inline
function body was evaluated against the *caller's* execution context — and the
caller had no record of M1. `InlineFunctionItem.InvokeAsync` now executes the
function body in the *captured* static context (the sub-engine context where
both M1 and M2 are registered), per XPath/XQuery 3.1 §3.1.5.1.

Verified on Martin's `load-module-with-import1.xsl` repro: now returns the
expected `Hello World: John Doe` instead of throwing.

### Cross-module declaration visibility (#57)

Three related fixes that let library modules expose what the spec says they
should — and only what the spec says they should:

* **Decimal-format scoping (XQuery 3.1 §4.18).** Library-module `declare
  decimal-format lib:euro …` was silently dropped by `VisitLibraryModule`, so
  `format-number(…, "lib:euro")` *inside* a lib:* function raised `FODF1280`
  when called from an importing module. The library-module parser now invokes
  the shared `ProcessDecimalFormatDecls` helper used by main-module parsing,
  and the static analyzer re-keys each imported format to its EQName form
  (`Q{uri}local`) so the runtime can resolve it from `format-number`'s
  prefix-expansion path. The optimizer also adds `import module` prefixes
  to runtime `PrefixNamespaceBindings` so that expansion succeeds.

* **Nested-import base URI.** When module M imports module N which itself
  imports another file by relative path, the relative URI now resolves
  against N's own location instead of the outer importer's.

* **Private function injection.** `%private` functions from imported modules
  are visible to their own module's public functions when those public
  functions are called from the importer.

### General comparison: `untypedAtomic` → `xs:QName` cast uses in-scope namespaces (QT3 GenCompEq-22)

`xs:untypedAtomic('z:local') = (<z:local/>)/node-name(.)` returned false
because the implicit `QName` cast synthesized a fresh `NamespaceId` from a
hash instead of resolving the `z` prefix against the in-scope namespace
context. Now uses `PrefixNamespaceBindings` for the cast and surfaces
`FONS0004` when the prefix is unbound.

### `element()` / `attribute()` kind tests preserve EQName URI (QT3 K2-DirectConElemNamespace-78)

`element(Q{uri}local)` and `element(prefix:local)` kind tests dropped the URI
from `NameTest` during AST construction, so the runtime matcher fell back to
local-name comparison and matched the wrong elements. The parser now preserves
`qn.ExpandedNamespace`, and the runtime kind-test matcher resolves prefixes
against the active `NamespaceResolver`.

### CPM bump: PhoenixmlDb.Xslt 1.3.18 → 1.3.20

Picks up the XSLT CLI `--timing` memory output and the `fn:transform`
source-location URI option in the bundled XSLT used by the `xquery4` CLI.

## 1.3.13 (2026-05-19)

### CPM bump: PhoenixmlDb.Xslt 1.3.17 → 1.3.18

Pulls in the `fn:transform` raw-delivery node re-anchoring fix so the
`xquery4` CLI's bundled XSLT now hands back subtrees navigable by the
caller. Library code unchanged from 1.3.12.

## 1.3.12 (2026-05-19)

### Namespace ID collision corrupted direct element constructor serialization (Martin Honnen Schematron repro)

`XdmDocumentStore` used two independent NamespaceId allocators — one in
the static analyzer (`NamespaceContext`, `100 + Count`) and one at runtime
(`_nextNamespaceId++` from 100). Both could land on the same numeric ID
for different URIs. The analyzer then called `RegisterNamespace(uri, id)`
which populated `_reverseNamespaces`; subsequent runtime allocations of
the same ID to a *different* URI made the forward and reverse dictionaries
disagree. Serialization (via `ResolveNamespaceUri` → reverse lookup)
emitted the wrong URI.

Symptom: a direct element constructor like
`<xsl:stylesheet xmlns:mf="http://example.com/mf">...</xsl:stylesheet>`
serialized to text with `xmlns:mf="http://www.w3.org/1999/XSL/Transform"`
because `mf` URI interned at ID 108 but the reverse map already had
`108 → XSLT URI`. Downstream `fn:transform` then tripped `XTSE0080`
("function `mf:evaluate` in reserved namespace") on a stylesheet that
should have been valid.

Fix: when allocating a new ID, skip any candidate already claimed in
`_reverseNamespaces`; mirror new bindings into the reverse map so future
lookups stay consistent.

### `InMemoryUpdatableNodeStore` started dynamic IDs at 3 instead of 100

Old starting value collided with reserved well-known IDs (Xsd=3, Xsi=4,
Fn=5, Map=6, Array=7, Math=8, Dbxml=9, Xslt=10) on the 8th interned URI.
Now starts at `NamespaceId.FirstUserNamespaceId` (100).

### CLI: `-p`/`--param` external variable binding + memory in `--timing`

```
xquery 'declare variable $n as xs:integer external; $n * 2' -p n=10
```

Also supports `-p:name=value` and `--param:name=value` forms. Repeat
the flag for multiple bindings. `--timing` now adds a `memory:` line
with peak working set and total managed allocations alongside parse /
compile / execute timings.

### External variable binding casts strings to declared atomic types

CLI-supplied bindings arrive as plain strings. Previously
`declare variable $n as xs:integer external` rejected `-p n=10` with
`XPTY0004`. We now follow Saxon's reading of XQuery §2.2.5: treat the
supplied string as `xs:untypedAtomic` and apply function-conversion to
the declared atomic type. Cast failures still surface a clean
`XPTY0004` with the variable name.

## 1.3.6 (2026-05-13)

### Source-location audit Phase D7+D8: RelatedLocations + Length helper + conventions

LSP-foundation polish on top of the 1.3.5 infrastructure.

**`SourceLocation.Length`** (Phase D8): new computed property
`Length = EndIndex - StartIndex + 1` (since `EndIndex` is inclusive,
matching ANTLR `StopIndex`). Returns `0` for degenerate ranges. LSP
adapters use this to size diagnostic squiggles instead of inferring it.

**Documented coordinate conventions** (Phase D8): the `SourceLocation`
class doc now spells out the unit conventions across the codebase —
`Line` is always 1-based; `Column` is 1-based for XSLT-shifted
file-absolute positions (Module set) and 0-based for raw ANTLR-only
positions (Module null); `StartIndex`/`EndIndex` are 0-based input-string
offsets, `EndIndex` inclusive.

**`XQueryException.RelatedLocations`** (Phase D7): new init-only
`IReadOnlyList<SourceLocation>` property (defaults to empty). Lets
future raise sites attach secondary positions to a primary error — e.g.
an `XPTY0004` from a type assertion can carry the offending input
`XdmNode`'s source position alongside the stylesheet position. LSP
surfaces these as related diagnostics so users can jump from the
assertion site to the data position that violated it. Init-only, fully
backwards-compatible additive change.

Five new unit tests in `CurrentLocationTests` cover the boundary cases
(`Length`: typical, single-char, degenerate; `RelatedLocations`: default
empty, populated round-trip).

**Compatibility:** purely additive. Sites that don't read the new
property/properties are unaffected.

## 1.3.5 (2026-05-12)

### Source-location audit: 155 runtime-error sites now carry (module, line, col)

Foundation for upcoming LSP work. Errors raised from the function library
now auto-attach the call-site source location through ambient context
tracking, instead of bubbling up as locationless `XQueryException`s.

**Infrastructure:**
- `QueryExecutionContext.CurrentLocation` + `PushLocation(loc)` returning
  a struct `LocationScope` (struct, not ref-struct, so it survives async-
  iterator state machines).
- `Error(code, msg)` and `Error(code, msg, inner)` factories on the
  context that auto-attach `CurrentLocation`.
- Nullable-receiver extension `Ast.ExecutionContext.Error(...)` so deep
  static helpers can take `Ast.ExecutionContext? context = null` and call
  `context.Error(...)` uniformly — null receiver falls through to a
  locationless exception, preserving prior behavior.
- New `XQueryException(code, msg, inner, location)` 4-arg ctor.

**Wiring:**
- `FunctionCallOperator.ExecuteAsync` wraps its body with
  `PushLocation(this.Location)` so any error raised inside the called
  function inherits the call-site location.
- `QueryOptimizer` now propagates `FunctionCallExpression.Location` onto
  the generated operator (was being dropped).

**Sweep — coverage by error code:**
| Code      | Sites | Coverage |
|-----------|-------|----------|
| XPTY0004  | 36/45 | 80%      |
| FOJS0006  | 22/22 | 100%     |
| XPDY0002  | 12/12 | 100%     |
| FODF1310  | 10/10 | 100%     |
| FORG0001  | 8/8   | 100%     |

Total: **155 of 164 runtime raise sites** now flow through `context.Error`
(94.5%). The 9 remaining are intentionally out-of-scope:
4 in `fn:error()` itself (uses object initializer for `ErrorNamespaceUri`/
`ErrorPrefix`/`ErrorValue`), 5 in `XQueryAstBuilder` (parser-side; no
runtime context exists at compile time).

**What this means for callers:** errors raised by built-in functions now
include `[module:line:col]` in the formatted `Message` and populate the
typed `Module`/`Line`/`Column` properties on `XQueryException`. Existing
callers that don't read those properties are unaffected.

**Compatibility:** purely additive. Sites not yet swept retain their
previous (locationless) behavior. No public API removed or changed.

Regression tests:
`CurrentLocationTests` (7 unit tests covering nesting, null-pushes,
restoration, factory output),
`XPTY0004_from_index_of_carries_call_site_location` (end-to-end:
`fn:index-of((1,2,3), ())` → XPTY0004 with `[line 1, col …]`).

## 1.3.4 (2026-05-12)

### Bare `/` with no context item raises XPDY0002 (was: silent empty)

Per XPath 3.1 §3.3.2, a leading `/` requires a context item rooted at a
document. `DocumentRootOperator` previously fell through to "yield
nothing" when the focus was absent — silently returning the empty
sequence instead of the spec-mandated XPDY0002.

Found via QT3 `prod-AxisStep::K2-Axes-45` (`(/, 1)[2]`). The test allows
either `1` (if the engine optimizes the index access early) or
XPDY0002; we returned neither, which is wrong under both readings.

Fix: throw XPDY0002 when `contextItem` is null in `DocumentRootOperator`.

Regression tests:
`RuntimeErrorLocationTests.Bare_slash_with_no_context_raises_XPDY0002`
and `Bare_slash_inside_sequence_raises_XPDY0002`.

### Parse errors now carry the XPST0003 code

`XQueryParseException` formatted error messages plain ("Parse error:
mismatched input ..."). Conformance harnesses and human readers expect
the spec-mandated error code (XPST0003 — "It is a static error if an
expression is not a valid instance of the grammar") to identify the
category at a glance.

Fix: `FormatMessage` prepends `XPST0003:` to the underlying lexer/parser
message unless the message already starts with a different error code
(e.g. `XPST0081:` for unbound prefixes from the AST builder).

Surfaces QT3 `K-XQueryComment-15` (unterminated nested XQuery comment)
and other parse-error tests as XPST0003 instead of generic "Parse error".

## 1.3.3 (2026-05-11)

### Improvement: `XQueryExpression.Location` is now settable

Allows the XSLT compiler to augment a parsed XPath's `Location` after parse —
specifically to stamp the originating XSLT module URI on top of the position info
the XQuery parser produced (which is relative to the inline XPath string, not the
XSLT source file).

This unblocks XSLT errors of the form `[file:///path/to/stylesheet.xsl:47] XPTY0020 ...`
instead of just `[line 2, col 24] XPTY0020 ...`. With real-world stylesheets like
Docbook TNG containing thousands of XPaths, the relative-line prefix is needle-in-haystack;
the module URI is what makes diagnostics actionable.

Source-compatible — initializer-syntax callers continue to work since `set` accepts
everything `init` did. The mutability is intentional and narrowly scoped to post-parse
augmentation; the AST is otherwise treated as immutable by the engine.

## 1.3.2 (2026-05-09)

### Fix: `{{` / `}}` brace escapes in direct attribute value literals

`<element foo="{{.}}"/>` (XQuery 3.1 §3.9.1.1 brace escapes — `{{` for literal
`{`, `}}` for literal `}`) raised
`XPST0003: Unmatched '}' in direct attribute value literal — use '}}' to escape`
instead of producing the attribute value `{.}`.

Root cause: ANTLR's longest-match rule let `ATTR_*_CHAR` greedily swallow the
trailing `.}}` as a single token (the `}}` escape rule had no chance to fire
once `.` started a `CHAR` run). The downstream `DecodeAttrContent` then saw an
apparently unpaired `}` and rejected it. Fix pairs `}}` inside the decoder
itself, so the lexer's token boundary doesn't matter — `}}` collapses to `}`,
bare `}` still errors.

Reported by Martin Honnen while embedding XSLT in XQuery for `fn:transform`
testing — his `xsl:element name="{{$namespaces('')}}"` style escapes now parse
correctly. Minimal repro: `xquery "<element foo='{{.}}'/>"`.

Six regression tests in `AttrBraceEscapeTests`.

### Convenience: `output:` and `err:` namespace prefixes are predeclared

`declare option output:omit-xml-declaration "yes";` (and similar serialization
parameter declarations) now work without a separate `declare namespace
output = "http://www.w3.org/2010/xslt-xquery-serialization";` first. Same
treatment for `err:` (the standard XQuery error namespace), so users can
write `err:FOAR0001` in `try`/`catch` clauses without a prolog declaration.

XQuery 3.1 §2.1.1 allows implementations to add predeclared namespaces;
this matches Saxon's behavior so users porting Saxon queries don't trip on
boilerplate.

### Improvement: runtime errors now carry module / line / column

`XQueryException` (the runtime-error type for everything the executor raises:
XPTY0020, XPDY0050, FOAR0001, etc.) now exposes `Module`, `Line`, and `Column`
properties, and the formatted `Message` is prefixed with
`[<module>:<line>:<col>] ` when the source location is known. Plain
string-based logging surfaces the location without callers needing to inspect
the structured properties.

XPTY0020 ("axis step on non-node") in particular is now diagnosable: the
optimizer threads `SourceLocation` from the `PathExpression` / `StepExpression`
AST nodes into `DocumentRootOperator`, `ContextItemOperator`,
`AxisNavigationOperator`, and `PerNodeStepOperator`, so all four throw sites
carry location info at runtime. The error message also includes the offending
context item's actual XDM type (e.g. `xs:integer 1`, `xs:string "abc"`,
`map`) so callers can see what the engine actually saw, not just that it
wasn't a node.

Originated from a Martin Honnen report: XPTY0020 fired by Docbook TNG
stylesheets had no module / line info, leaving him with no way to find the
offending axis step. With this release, `[file:///stylesheets/docbook/chunk.xsl:42:7]`
appears in the error text and pinpoints the source.

The 2-argument `XQueryException(code, message)` constructor still produces
the bare un-prefixed message — backward-compat for any consumer that
string-matches on it.

## 1.3.1 (2026-05-08)

### Fix: `validate` expression rejected valid documents

`ValidateOperator` was passing `XdmNode.StringValue` (the concatenated text
content of the tree) to the schema validator instead of a proper XML
serialization. For a document like
`<root><item><name>item 1</name><value>15</value></item></root>`, the
validator received the text `"item 115"` wrapped synthetically as
`<root>item 115</root>` — and dutifully rejected it as "cannot contain
text" / "List of possible elements expected: 'item'", neither of which
matched the actual document.

Fix: serialize the node properly via `SerializeFunction.SerializeNodeToXml`
(which has access to the operator's `INodeProvider` and walks the tree)
and route through the existing `ISchemaProvider.ValidateXml(string, ...)`
path. The original node is yielded back post-validation; deep-copy with
type annotations remains a Phase-2 feature.

### Fix: `import schema 'foo' at 'bar.xsd'` now resolves against query base URI

Schema-import location hints were resolved against the application's
process CWD instead of the query's base URI. Module imports already
honored the base URI; schema imports skipped that step entirely. Embedded
hosts that ship query files alongside their schemas — the typical WPF /
Avalonia pattern — got `FileNotFoundException` pointing at the binary's
directory.

Fix: extracted the resolution pattern from the module-import path into a
shared `ResolveLocationHints` helper and called it from the schema-import
case in `StaticAnalyzer` before delegating to `ISchemaProvider.ImportSchema`.

Both fixes reported by Martin Honnen.

## 1.3.0 (2026-05-07)

### Blazor WebAssembly: clearer error for HTTP doc loaders

When `fn:doc()` / `fn:document()` resolves an `http(s)://` URI on Blazor
WebAssembly, the engine now raises a clear exception naming the URI
instead of the runtime's obscure `Cannot wait on monitors on this runtime`.
The underlying cause — synchronous-over-async `HttpClient` waits in
`HttpDocumentClient` — is unchanged for this release; the host should
preload required documents and provide them via a custom resolver, mirroring
the `PhoenixmlDb.Xslt.PreloadedResources` pattern shipped in XSLT 1.3.0.

### `INodeBuilder.InternNamespace(uri, preferredId)` overload

Added an overload that accepts a `preferredId` hint. The default-interface
implementation drops the hint and delegates to the existing single-argument
form, so existing `INodeBuilder` implementations compile and run without
change. `XdmDocumentStore` and the XSLT engine's in-memory store both
honor the hint, so namespace IDs assigned during static analysis can
round-trip through serialization without a separate `RegisterNamespace`
call. Internal sweep deletes ~140 lines of `is XdmDocumentStore` casts
and duplicate fallback paths from the constructor operators.

## 1.2.5 (2026-05-06)

### `fn:doc` / `fn:doc-available` fetch over HTTP(S)

`XdmDocumentStore.ResolveDocument` and `IsDocumentAvailable` now handle
`http://` and `https://` URIs — they're streamed through a shared `HttpClient`
(30 s timeout, `User-Agent: PhoenixmlDb.XQuery`) and parsed into XDM.

A configured `ResourcePolicy` still gates network access via the
`PolicyEnforcingResolver` wrapper, so callers can restrict to specific
hosts or path prefixes.

Reported by Martin Honnen — XQuery scripts that derived a doc URI from
`base-uri(.)` of an HTTPS-loaded source raised FODC0002 before.

## 1.2.4 (2026-05-06)

### `xquery` CLI now bundles XSLT for `fn:transform()`

The `xquery4` global tool now references `PhoenixmlDb.Xslt` 1.2.8 alongside the
XQuery engine, so XQuery scripts that call `fn:transform()` work immediately:

```bash
xquery -f my-script.xq input.xml
```

Previously raised:

```
Error: fn:transform is not available — no XSLT processor has been registered.
```

`PhoenixmlDb.Xslt` 1.2.8 carries a `[ModuleInitializer]` that wires
`XsltTransformProvider` into `TransformFunction.Provider` automatically on assembly
load, so the CLI just needs to reference the package — no explicit registration call.

No library API changes from 1.2.3.

Reported by Martin Honnen.

## 1.2.3 (2026-05-05)

### fn:doc-available accepts xs:untypedAtomic

Fixes `fn:doc-available($uri)` raising XPTY0004 when `$uri` is an `xs:untypedAtomic`
value. Per XPath 3.1 function conversion rules, untyped atomic values are cast to the
declared parameter type (`xs:string?` here). Useful for XSLT params that default to
`xs:untypedAtomic('foo.xml')` and for any path expression that yields untyped attribute
values.

Reported by Martin Honnen.

## 1.2.2 (2026-05-05)

### SourceLocation gains Module field

`SourceLocation` (consumed via `XsltException.Location` and `XQueryRuntimeException.Location`)
now carries an optional `Module` property — the originating module URI / file path. This is
the diagnostic anchor users need when a stylesheet or query is composed of imported or
included modules: without it, an error message can only show line/column, not which file.

Backwards-compatible — `Module` defaults to `null` and existing callers that don't set it
keep working. The XSLT 1.2.5 release populates it from `XElement.BaseUri` automatically.

## 1.2.1 (2026-04-29)

### fn:serialize adaptive method

Fixes `fn:serialize($input)` and `fn:serialize($input, map { 'method': 'adaptive' })`
producing JSON instead of adaptive output per XPath/XQuery 3.1 §17.1.3. The fallback
serialization path (used by XSLT and any caller whose node provider isn't
`XdmDocumentStore`) was hard-coded to JSON; it now honors the requested method and
emits adaptive form for maps (`map{key:value,…}`), arrays (`[…]`), sequences (`(…)`),
atomic types in constructor form, and nodes via the XML serializer. The 1-arg form
defaults to adaptive per spec.

Reported by Martin Honnen.

## 1.2.0 (2026-04-30)

### Schema validation as extension point, not commercial gate

The schema-aware feature surface is now part of the main package and registered by default
on every `QueryEngine`. The previous "free-tier XQST0075 stubs vs commercial
PhoenixmlDb.XQuery.Schema package" split is gone — the sidecar package is removed and
`XsdSchemaProvider` (System.Xml.Schema-backed: XSD validation, type hierarchy, substitution
groups) ships in `PhoenixmlDb.XQuery`. `ISchemaProvider` remains the public extension point
for custom schema languages (RelaxNG, Schematron-derived, in-memory).

New ISchemaProvider methods (default-implemented for back-compat):
- URI-string overloads of all declaration lookups (`HasElementDeclaration(uri, local)` etc.)
  so callers don't have to round-trip arbitrary URIs through `NamespaceId`
- `ValidateXml(string xmlContent, ValidationMode mode, ...)` — document-mode validation of
  already-serialized XML
- `ValidateXmlFragment(string xmlFragment, ValidationMode mode, ..., inScopeNamespaces)` —
  fragment-mode validation, with optional prefix→URI bindings declared on enclosing
  elements that the caller can't fold into the fragment text

Other behavioral changes:
- Runtime `instance of schema-element(...)` / `treat as schema-element(...)` route through
  `ISchemaProvider.MatchesSchemaElement`, picking up substitution-group members and
  schema-derived type annotations.
- `import schema` is wired through `ISchemaProvider.ImportSchema` during static analysis;
  schema-locate failures surface as real XQST0059 errors.
- `XsdSchemaProvider` round-trips user namespaces correctly (not just the four built-in
  URIs) via an internal NamespaceId↔URI map populated when schemas load.

### Critical fixes from real-world stylesheets (Martin Honnen reports)

- Prefixed atomic types in cast/castable/instance-of (e.g. `castable as xs:integer`)
  wrongly raised XPST0051. `XdmSequenceType.UnprefixedTypeName` is now only set when the
  source name was actually unprefixed; new `LocalTypeName` carries the local-name
  component used by derived-integer range checks and string-subtype normalization.
  Reported against DocBook xslTNG and Schxslt2 transpile.xsl.
- `namespace::` axis raised XQST0134 in XPath/XSLT contexts. Added
  `XQueryParserFacade.AllowNamespaceAxis` opt-in (default false to preserve XQuery's
  strict semantics); XSLT side passes true. XPath 3.1 retains the axis as
  deprecated-but-optional.

## Unreleased

### QT3 Conformance: 82.3% → 99.6% (+17.3pp, ~4,500 tests)

26,064 of 26,175 tests passing (99.58%). Remaining ~111 failures are in parser leniency (ANTLR grammar), XSD type hierarchy wrappers, XPath 4.0 xs:numeric, negative-year dates, and schema-aware features.

### Features
- **Schema-awareness via `ISchemaProvider` plugin**: New plugin architecture for XQuery schema features, mirroring Saxon's HE/PE tiering. Free tier defines the contract and gates schema features at static analysis with proper error codes (XQST0075 for `validate`, XPST0008 for `schema-element()`/`schema-attribute()`, XQST0009 for `import schema`). Commercial `PhoenixmlDb.XQuery.Schema` package ships an `XmlSchemaSet`-backed provider with full XSD validation, type hierarchy walking, substitution-group matching, and element/attribute declaration lookups. Wired through all compiler phases: parser → namespace resolver → schema-feature checker → variable binder → function resolver → type inferrer → optimizer → `ValidateOperator` at execution. New AST nodes (`SchemaImportExpression`), item types (`SchemaElement`, `SchemaAttribute`), and `XdmSequenceType` extensions for `schema-element(Name)` / `schema-attribute(Name)`. 30 new public tests cover parsing and feature-gating; 20 commercial tests cover the XSD-backed provider end-to-end.
- **format-number with decimal-format**: Full rewrite with custom decimal-separator, grouping-separator, digit, pattern-separator, infinity, NaN, minus-sign, percent, per-mille, zero-digit, exponent-separator. Wire `declare decimal-format` prolog through optimizer to runtime.
- **Direct PI and comment constructors**: `<?target data?>` and `<!-- comment -->` now valid as primary expressions and inside element content. CDATA sections `<![CDATA[...]]>` supported.
- **Pragma/extension expressions**: `(# pragma-name content #) { expr }` parsed and evaluated (pragmas ignored, body returned).
- **`validate` expression**: `validate strict/lax/type { expr }` parsed and evaluated. Without an `ISchemaProvider` registered, raises XQST0075 at static analysis with a clear "Schema Validation Feature is not available" message; with the commercial provider, runs the validating reader and raises XQDY0027 on validation failure.
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
