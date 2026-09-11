# XQuery 3.1 Conformance Report — SUPERSEDED (historical, 2026-04-20)

> # ⚠ SUPERSEDED — DO NOT QUOTE THESE NUMBERS
>
> **This is a historical record from 2026-04-20, not a current measurement.** The 99.72% below
> is not the engine's conformance rate and must not be cited anywhere.
>
> Two things are wrong with it:
>
> 1. **It measured a smaller corpus.** It reports 26,730 cases. The QT3 suite as run today has
>    **31,414**. Roughly 4,700 cases were not being executed at all, so the denominator — and
>    therefore the rate — describes a different, easier suite.
> 2. **It predates the harness audit.** In September 2026 the runner was found to score an
>    expected-error test as PASSING whenever the query threw *anything at all*: the corpus writes
>    the expected code as an attribute and the runner read element text, so the comparison was
>    always against an empty string. Every conformance figure taken before that fix is inflated
>    by an unknown amount. See `phoenixmldb-xslt/BUGS.md` #28.
>
> **The current figure is not yet published**, and deliberately so rather than being estimated.
> Re-running QT3 requires more than the harness's present 60-minute cap — an attempt on
> 2026-09-10 completed only 9 test-sets before the timeout — so no honest number is available to
> replace this one. Raising that cap and publishing a measured rate is tracked as open work.
>
> For a conformance figure that IS current and measured, see `phoenixmldb-xslt/README.md`, which
> reports XSLT 3.0 at 10,082/10,630 (94.8%) measured 2026-09-10 in a Release build.
>
> The breakdown below is retained because the FAILURE ANALYSIS is still useful — the categories
> of remaining defect are largely unchanged. The counts and the rate are not.


**Suite**: W3C QT3 (XQuery Test Suite 3.1)
**Date**: 2026-04-20
**Result as recorded then**: 99.72% (26,656 / 26,730)  ← **not current, see banner**

## Suite Breakdown

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
