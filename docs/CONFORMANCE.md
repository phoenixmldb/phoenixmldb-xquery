# XQuery / QT3 Conformance Report

**Suite**: W3C QT3 (`w3c/qt3tests` @ `201a6e4`)
**Date**: 2026-09-11
**Build**: Release
**Result**: **93.98%** — 29,524 / 31,414 across all 428 catalog test-sets

## This is the first reproducible figure this project has had

The number above replaces a **99.72%** that stood in this file from 2026-04-20 until today. That
figure was wrong twice over, and both reasons are worth keeping:

1. **It measured a smaller corpus.** It reported 26,730 cases against today's 31,414 — roughly
   4,700 cases were never executed, so the denominator described an easier suite.
2. **It predated the harness audit.** The runner scored an expected-error test as passing whenever
   the query threw *anything at all*, because the corpus writes the expected code as an attribute
   and the runner read element text. Every figure taken before that fix is inflated by an unknown
   amount (`phoenixmldb-xslt/BUGS.md` #28).

Between April and today there was no honest number, and for a period this file said so rather than
substituting a remembered one.

## Why this figure is trustworthy, stated so it can be checked

**Reproducibility was verified, not assumed.** The run was repeated from two checkout paths whose
xunit execution orders differ, and the per-set results are identical.

That check matters because it failed before. When the suite first moved from one opaque
31,414-case test to a per-set theory, all 428 sets shared a single fixture whose runner
accumulated engine, document, schema and URI state — so a set's result depended on what ran
before it, and two checkouts of the same commit disagreed on six sets. Giving each set a fresh
runner fixed it and was worth **+126 cases** on its own: the shared state was a net loss, not
merely a source of variance. See `BUGS.md` #44, incident 12.

**The figure is the minimum across full runs, not the best one seen.** Where two `--all` runs
disagree on a set, the baseline records the lower count. A baseline set to the maximum observed
enshrines a lucky run and then reports a regression every time the suite behaves normally; the
value that is reached *every* time is the one a gate can be built on.

- **Commits**: `phoenixmldb-xslt` `357db01` (baseline) + `phoenixmldb-xquery` `8cece45`.
  Harness provenance: `bce22f5` + `6d3217e`.
- **428 of 428** catalog test-sets executed, ~80 s
- Measured in **Release**. A Debug run understates, almost entirely through per-case timeouts
  (`BUGS.md` #43).

## One caveat, stated rather than buried

This is the sum of the per-set theory. The older whole-suite test
(`Xqts_ShouldRunFullTestSuite`) still exists, runs in catalog order with shared state, and scores
differently. Its 95% assertion is permanently red. **Do not cite the monolith's number**; it is
the arrangement the per-set theory replaced, and it is pending a decision on whether to remove it.

## What changed in the engine to get here

Three defects fixed in `PhoenixmlDb.XQuery` (issues #6, #7, #8), all shipped after 1.7.0:

- `map:put`/`map:remove` copied the whole map, making incremental map building O(n²). Full QT3
  wall clock fell from 1,413 s to 77 s.
- Map keys the comparer called equal could hash apart, so a missed numeric lookup fell back to an
  O(n) scan — 8,746 ms to 472 ms at N=16k.
- A caller's cancellation was not observed in the hot loop, so a timeout could not stop a query.

## Suite Breakdown

> **The table and analysis below are from the superseded 2026-04-20 run and are retained for the
> FAILURE CATEGORIES only.** Its counts (26,656 / 26,730) and its 99.72% describe a smaller corpus
> measured with the fail-open runner — do not quote them. The current figure is 29,524 / 31,414
> at the top of this file. A per-category breakdown of the current run has not been produced yet.



| Category | Passed | Total | Rate | Failures |
|----------|--------|-------|------|----------|
| fn (functions) | 10,320 | 10,320 | 100.00% | 0 |
| op (operators) | 4,261 | 4,275 | 99.67% | 14 |
| prod (productions) | 11,304 | 11,335 | 99.73% | 31 |
| misc | 771 | 800 | 96.38% | 29 |
| **Total** | **26,656** | **26,730** | **99.72%** | **74** |

## Remaining 74 Failures

### UCA Collation (27 failures)

.NET's ICU APIs do not expose the full UCA (Unicode Collation Algorithm) parameter set. Parameters like `reorder`, `maxVariable`, and certain strength/decomposition combinations are not supported by `System.Globalization.SortKey` or `CompareInfo`.

**Impact**: Only affects queries using `declare default collation` with UCA-specific parameters beyond what .NET ICU exposes.

**Tests**: All 27 failures are in the `misc` category — `misc-CombinedErrorCodes` and related UCA collation test sets.

### Schema-Aware Features (12 failures)

PhoenixmlDb does not implement schema-aware processing. Features requiring XSD schema validation, typed attribute access, or schema-element/schema-attribute type checks are not supported.

| Sub-category | Count | Details |
|-------------|-------|---------|
| Construction preserve / nsmode | 5 | `construction preserve` declaration with schema types |
| Context item declaration schema | 4 | `declare context item as schema-element(...)` |
| Type system / instanceof | 3 | Schema type subsumption checks |

### Date/Time Overflow (7 failures)

.NET `DateTime` has a range of 0001-01-01 to 9999-12-31. XQuery allows arbitrary date values (e.g., year 0, negative years, years > 9999). Tests requiring dates outside .NET's range fail with overflow exceptions.

### Module System (7 failures)

Complex module import edge cases that involve:
- Module-scoped decimal format declarations
- Circular or diamond-shaped module dependencies
- Multiple module imports with overlapping namespaces

### Timeouts (5 failures)

Tests that perform extremely large iterations (e.g., 200,000+ map operations) exceed the test timeout threshold. These are performance-bound, not correctness issues.

**Tests**: `op-same-key-010`, `op-same-key-011`, `op-same-key-023`, `op-to` (RangeExpr-409d), and one additional.

### Computed Namespace Constructor (4 failures)

Edge cases in computed namespace constructors involving module scope and namespace node identity.

**Tests**: `nscons-028`, `nscons-036`, `nscons-037`, `nscons-038`.

### Numeric Delimiter (3 failures)

XQuery requires whitespace between numeric literals and keyword operators: `10div 3` should be parsed as `10 div 3`, but ANTLR's longest-match lexer rule produces `10` + `div` only when separated by whitespace. Without a space, `10div` is lexed as an NCName.

**Tests**: `10div`, `10mod`, `10idiv` — these are ANTLR lexer limitations that would require a custom lexer to resolve.

### Namespace Scoping (2 failures)

Default element namespace propagation edge cases in computed constructors.

**Tests**: `K2-DefaultNamespaceProlog-12a`, `MapTest-008`.

### Error Codes (2 failures)

Tests expecting specific error codes that differ from our implementation's error reporting.

**Tests**: `XQST0046_02`, `XQST0048`.

### Parser/Lexer Edge Cases (2 failures)

| Test | Issue |
|------|-------|
| `K-XQueryComment-15` | Unterminated nested XQuery comment — ANTLR grammar produces a different error than expected |
| `K2-Axes-45` | Standalone `/` as a complete expression — parser rejects this edge case |

### Other (3 failures)

| Test | Issue |
|------|-------|
| `GenCompEq-22` | General comparison edge case |
| `MapTest-054` | Map type test with namespace resolution |
| `Serialization-035` | Serialization parameter combination |

## Intractable Categories Summary

Approximately 68 of the 74 failures fall into categories that cannot be fixed without fundamental platform changes:

| Category | Count | Reason |
|----------|-------|--------|
| UCA collation parameters | 27 | .NET ICU API limitations |
| Schema-aware features | 12 | Not implemented (would require full XSD processor) |
| Date/time overflow | 7 | .NET DateTime range limits |
| Module system | 7 | Complex import edge cases |
| Timeouts | 5 | Performance-bound, not correctness |
| Computed namespace constructors | 4 | Module + namespace node identity |
| Numeric delimiter | 3 | ANTLR longest-match lexer limitation |
| Other edge cases | 9 | Various parser/error code/namespace issues |

## Notes

- **100% on fn (functions)**: All 10,320 function tests pass, covering the complete XPath/XQuery Functions and Operators specification.
- **Reference implementation**: Saxon is used as the reference for behavioral comparison beyond the W3C test suite.
- **Test execution**: Tests are run per-class to avoid OOM issues with large test volumes.
- **Raw data**: See [xqts-results.tsv](xqts-results.tsv) (per-test-set pass/total) and [xqts-failures.tsv](xqts-failures.tsv) (individual failing tests with error details).
