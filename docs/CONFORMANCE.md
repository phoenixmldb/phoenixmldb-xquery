# XQuery / QT3 Conformance Report

**Suite**: W3C QT3 (`w3c/qt3tests` @ `201a6e4`)
**Date**: 2026-11-02
**Build**: Release
**Result**: **99.90%** — 31,294 / 31,325 across all 428 catalog test-sets

The figure is the committed per-set baseline (`scripts/conformance-baseline.tsv`). The run that
confirms it is `conformance-results/summary.txt` (engine `b6d7f3f`, 31 failed, 1 of them a
per-test timeout).

## How to reproduce

```bash
./scripts/fetch-conformance-suites.sh   # once: the suite at the pinned revision
./scripts/conformance.sh
```

The suite goes to `tests/PhoenixmlDb.XQuery.Conformance.Tests/TestData/qt3tests`. The script stops
with an error if the suite is not there. It builds
this repo's source in Release, runs each of the 428 test-sets as its own test, and compares each
set's passing count with the baseline. The run fails if any one set passes fewer cases than its
baseline, whatever the total does.

A case that does not finish in 30 s fails with "exceeded the per-test timeout". On a slow machine
these timeouts are machine speed, not engine results. `CONFORMANCE_TIMEOUTS_NONFATAL=1` lists them
separately and does not count them as lost.

## Which cases count

- The engine is an XQuery 4.0 processor. A case applies when its `spec` dependency names XQuery
  3.1, 4.0, or a version followed by `+`. A case that pins exact XQuery 1.0 or 3.0 behaviour
  (`XQ10`, `XQ30` with no `+`) does not apply, because later versions changed that behaviour.
- The processor is XML 1.0 Fifth Edition. A case whose `xml-version` dependency is `1.1` or
  `1.0:4-` does not apply.
- Every other dependency (features, collations, Unicode version) is checked against what the
  engine supports.

## By category

From the baseline:

| Category | Passed | Total | Failed |
|---|---|---|---|
| `app` | 2,153 | 2,156 | 3 |
| `array` | 209 | 209 | 0 |
| `fn` | 10,855 | 10,864 | 9 |
| `map` | 218 | 220 | 2 |
| `math` | 149 | 149 | 0 |
| `misc` | 827 | 829 | 2 |
| `op` | 4,364 | 4,364 | 0 |
| `prod` | 12,021 | 12,036 | 15 |
| `ser` | 337 | 337 | 0 |
| `xs` | 161 | 161 | 0 |
| **Total** | **31,294** | **31,325** | **31** |

## By XQuery version

From the nightly run of 2026-11-11 (engine `d2f4b28`), with the 17 cases that timed out on the slow
nightly host left out. The version is the lowest one that the case's `spec` dependency names.

| Version the case needs | Cases | Failed | Pass |
|---|---|---|---|
| 1.0 / 2.0 | 21,996 | 11 | 99.95% |
| 3.0 | 5,543 | 10 | 99.82% |
| 3.1 | 4,118 | 7 | 99.83% |

These case counts come from the catalog. They are about 330 higher than the runner's total,
because the runner also leaves out cases for features that the engine does not support.

## Failing cases

From the same nightly run (engine `d2f4b28`): 28 cases fail with a result. The table gives the
first line of each failure.

| Test-set | Case | Result |
|---|---|---|
| `app/app-spec-examples` | `fo-test-fn-compare-002`, `-004` | `-1` |
| `fn/fn-codepoints-to-string` | `cbcl-codepoints-to-string-021` | materialization limit of 2,000,000 items |
| `fn/fn-collection` | `collection-006`, `-007` | collection `collection1` not found |
| `fn/fn-data` | `ST-Data001` | XPTY0004 at compile time (static typing) |
| `fn/fn-default-language` | `default-language-006` | `en-US` |
| `fn/fn-function-lookup` | `fn-function-lookup-766b` | fn:transform has no XSLT processor |
| `fn/fn-parse-json` | `fn-parse-json-943` | fallback function raised an error |
| `fn/fn-transform` | `fn-transform-901`, `-902` | fn:transform has no XSLT processor |
| `misc/misc-XMLEdition` | `XML10-4ed-Excluded-char-1-new`, `XML11-1ed-Included-char-1-new` | invalid name character |
| `prod/prod-AxisStep` | `K2-Axes-54` | XQST0134, namespace axis |
| `prod/prod-CastExpr` | `CastAsNamespaceSensitiveType-10` | parameter type mismatch on `xs:QName` |
| `prod/prod-CastExpr.schema` | `CastAs-UnionType-13`, `-14`, `-15` | wrong value |
| `prod/prod-FLWORExpr.static-typing` | `ST-PITest-01` | wrong value |
| `prod/prod-InstanceofExpr` | `instanceof140` | `false` |
| `prod/prod-SchemaImport` | `qischema265` | wrong value |
| `prod/prod-ValidateExpr` | `cbcl-validateexpr-9`, `validate-as-91011`, `-91015`, `-91017` | validation result |
| `prod/prod-WindowClause` | `WindowingUseCase07S`, `11S`, `13S` | wrong value |

fn:transform needs `PhoenixmlDb.Xslt`, which this repo does not reference.

On the slow nightly host, 17 more cases time out: `app-CatalogCheck` (5), `app-Demos` (4),
`app-XMark` (1), `op-same-key` (4), `map-keys-014`, `map-for-each-014` and
`environment-variable-012`. On the CI host only `itunes` times out.
