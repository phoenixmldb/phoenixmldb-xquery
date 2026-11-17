#!/usr/bin/env bash
# Runs the W3C full-text suite (XQFTTS 1.0.4) against THIS repo's XQuery source and gates every
# case against a committed baseline.
#
#   ./scripts/fulltext-conformance.sh                    # run and check the baseline
#   CONFORMANCE_UPDATE_BASELINE=1 ./scripts/fulltext-...  # rewrite the baseline from this run
#
# PER-CASE GATING
#
# The baseline lists every case and its outcome. ANY case that passed in the baseline and does
# not pass now fails the run, whatever the totals did: a group can lose one case while gaining
# another, and a count would hide it. A case that newly passes is listed as a gain; raise the
# baseline in the same PR, from a run that is not the one the gain was first seen in.
#
# "inspect" is the catalog's own verdict for cases a human must compare; such a case does not
# count as a pass until Lucas rules on it.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJ="$ROOT/tests/PhoenixmlDb.XQuery.Conformance.Tests"
SUITE="${XQFTTS_TEST_SUITE:-$PROJ/TestData/xqftts}"
OUT="${CONFORMANCE_OUT:-$ROOT/conformance-results}"
CONFIG="${CONFORMANCE_CONFIG:-Release}"
BASELINE="$ROOT/scripts/fulltext-baseline.tsv"
CASES="$OUT/xqftts-cases.tsv"

if [ ! -e "$SUITE/XQFTTSCatalog.xml" ]; then
  echo "error: XQFTTS missing at $SUITE — run ./scripts/fetch-fulltext-suite.sh" >&2
  exit 1
fi
expected_cases=$(grep -c '<test-case ' "$SUITE/XQFTTSCatalog.xml")

export XQFTTS_TEST_SUITE="$SUITE"
export XQFTTS_RESULTS="$CASES"
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=0
mkdir -p "$OUT"
rm -f "$CASES"

if ! dotnet test "$PROJ/PhoenixmlDb.XQuery.Conformance.Tests.csproj" -c "$CONFIG" -f net10.0 \
      --filter "FullyQualifiedName~XqfttsConformanceTests" > "$OUT/xqftts.log" 2>&1; then
  tail -30 "$OUT/xqftts.log" >&2
  echo "error: the XQFTTS run did not complete" >&2
  exit 1
fi

# Fail closed: no results, or a different number of cases than the catalog, is not a pass.
if [ ! -s "$CASES" ]; then
  echo "error: no results at $CASES — the run wrote nothing (skipped?)" >&2
  exit 1
fi
got_cases=$(wc -l < "$CASES")
if [ "$got_cases" -ne "$expected_cases" ]; then
  echo "error: $got_cases results for $expected_cases catalog cases" >&2
  exit 1
fi

pass=$(awk -F'\t' '$3=="pass"' "$CASES" | wc -l)
fail=$(awk -F'\t' '$3=="fail"' "$CASES" | wc -l)
inspect=$(awk -F'\t' '$3=="inspect"' "$CASES" | wc -l)
# Cases Lucas ruled not applicable (scripts/fulltext-not-applicable.tsv): run and reported, but
# outside the pass rate.
NA="$ROOT/scripts/fulltext-not-applicable.tsv"
na_names=$(grep -v '^#' "$NA" 2>/dev/null | cut -f1 | sort -u)
na_count=$(printf '%s\n' "$na_names" | grep -c . || true)
na_pass=$(printf '%s\n' "$na_names" | grep . | while read -r n; do awk -F'\t' -v n="$n" '$2==n && $3=="pass"' "$CASES"; done | wc -l)
applicable=$((got_cases - na_count))
applicable_pass=$((pass - na_pass))
{
  echo "XQFTTS 1.0.4: $pass/$got_cases pass, $fail fail, $inspect inspect; applicable: $applicable_pass/$applicable ($na_count ruled not applicable)"
  echo
  echo "per group (pass/total):"
  awk -F'\t' '{t[$1]++; if ($3=="pass") p[$1]++} END {for (g in t) printf "  %-80s %4d/%d\n", g, p[g], t[g]}' "$CASES" | sort
} > "$OUT/xqftts-summary.txt"
head -1 "$OUT/xqftts-summary.txt"

if [ "${CONFORMANCE_UPDATE_BASELINE:-0}" = "1" ]; then
  cut -f1-3 "$CASES" | LC_ALL=C sort -t$'\t' -k2,2 > "$BASELINE"
  echo "baseline rewritten: $BASELINE ($pass pass)"
  exit 0
fi

if [ ! -s "$BASELINE" ]; then
  echo "error: no baseline at $BASELINE" >&2
  exit 1
fi

current="$(mktemp)"; trap 'rm -f "$current"' EXIT
cut -f1-3 "$CASES" | LC_ALL=C sort -t$'\t' -k2,2 > "$current"
# Every baseline case must still exist, and every baseline pass must still pass.
lost=$(LC_ALL=C join -t$'\t' -1 2 -2 2 -a 1 -e MISSING -o 1.2,1.3,2.3 "$BASELINE" "$current" \
       | awk -F'\t' '$2=="pass" && $3!="pass" {print}')
gained=$(LC_ALL=C join -t$'\t' -1 2 -2 2 -o 1.2,1.3,2.3 "$BASELINE" "$current" \
       | awk -F'\t' '$2!="pass" && $3=="pass" {print $1}')
baseline_pass=$(awk -F'\t' '$3=="pass"' "$BASELINE" | wc -l)
echo "baseline: $baseline_pass pass; now: $pass pass"
if [ -n "$gained" ]; then
  echo "GAINED ($(echo "$gained" | wc -l)) — raise the baseline in this PR:"
  echo "$gained" | sed 's/^/  /'
fi
if [ -n "$lost" ]; then
  echo "LOST ($(echo "$lost" | wc -l)) — passed in the baseline, not now:" >&2
  echo "$lost" | while IFS=$'\t' read -r name _ now; do
    detail=$(awk -F'\t' -v n="$name" '$2==n {print $4}' "$CASES")
    echo "  $name: $now — $detail" >&2
  done
  exit 1
fi
echo "XQFTTS gate: no case lost"
