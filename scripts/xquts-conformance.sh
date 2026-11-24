#!/usr/bin/env bash
# Runs the W3C XQuery Update Facility suite (XQUTS 1.0.1) against THIS repo's XQuery source and
# gates every case against a committed baseline.
#
#   ./scripts/xquts-conformance.sh                    # run and check the baseline
#   CONFORMANCE_UPDATE_BASELINE=1 ./scripts/xquts-... # rewrite the baseline from this run
#
# PER-CASE GATING
#
# The baseline lists every case and its outcome. ANY case that passed in the baseline and does
# not pass now fails the run, whatever the totals did: a group can lose one case while gaining
# another, and a count would hide it. A case that newly passes is listed as a gain; raise the
# baseline in the same PR, from a run that is not the one the gain was first seen in.
#
# A case is a sequence of states over the same documents and passes only when every state does
# (see tests/.../Update/XqutsRunner.cs). "inspect" is the catalog's own verdict for states a
# human must compare; such a case does not count as a pass until Lucas rules on it.
#
# XQUF 3.0 OVERRIDES
#
# The suite tests XQUF 1.0; the engine implements XQUF 3.0. A state whose result 3.0 changed is
# judged by its 3.0 result, listed with the rule in tests/.../Update/Xquf30Overrides.cs (Lucas,
# 2026-11-24). The gate and the baseline use the outcome with the overrides; the summary also
# gives the pass count under the suite's own expected results, and lists the overridden cases.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJ="$ROOT/tests/PhoenixmlDb.XQuery.Conformance.Tests"
SUITE="${XQUTS_TEST_SUITE:-$PROJ/TestData/xquts}"
OUT="${CONFORMANCE_OUT:-$ROOT/conformance-results}"
CONFIG="${CONFORMANCE_CONFIG:-Release}"
BASELINE="$ROOT/scripts/xquts-baseline.tsv"
CASES="$OUT/xquts-cases.tsv"

if [ ! -e "$SUITE/XQUTSCatalog.xml" ]; then
  echo "error: XQUTS missing at $SUITE — run ./scripts/fetch-xquts-suite.sh" >&2
  exit 1
fi
# Counted by parsing, not grep: the catalog keeps one removed <test-case> inside a comment, and a
# grep count (816) would never match the runner's (815).
counts=$(python3 -I -c '
import sys, xml.etree.ElementTree as E
ns = "{http://www.w3.org/2005/02/query-test-update}"
root = E.parse(sys.argv[1]).getroot()
print(sum(1 for _ in root.iter(ns + "test-case")), sum(1 for _ in root.iter(ns + "state")))
' "$SUITE/XQUTSCatalog.xml")
expected_cases=${counts% *}
expected_states=${counts#* }
if ! [[ "$expected_cases" =~ ^[0-9]+$ && "$expected_states" =~ ^[0-9]+$ ]] || [ "$expected_cases" -eq 0 ]; then
  echo "error: could not count the catalog's cases (got '$counts')" >&2
  exit 1
fi
echo "catalog: $expected_cases cases, $expected_states states"

export XQUTS_TEST_SUITE="$SUITE"
export XQUTS_RESULTS="$CASES"
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=0
mkdir -p "$OUT"
rm -f "$CASES"

if ! dotnet test "$PROJ/PhoenixmlDb.XQuery.Conformance.Tests.csproj" -c "$CONFIG" -f net10.0 \
      --filter "FullyQualifiedName~XqutsConformanceTests" > "$OUT/xquts.log" 2>&1; then
  tail -30 "$OUT/xquts.log" >&2
  echo "error: the XQUTS run did not complete" >&2
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
# Every line has the outcome under the suite's own expected results (column 6).
bad=$(awk -F'\t' '$6!="pass" && $6!="fail" && $6!="inspect"' "$CASES" | wc -l)
if [ "$bad" -ne 0 ]; then
  echo "error: $bad result lines have no outcome under the suite's expected results (column 6)" >&2
  exit 1
fi
got_states=$(awk -F'\t' '{s += $5} END {print s + 0}' "$CASES")
if [ "$got_states" -ne "$expected_states" ]; then
  echo "error: the results cover $got_states states, the catalog has $expected_states" >&2
  exit 1
fi

pass=$(awk -F'\t' '$3=="pass"' "$CASES" | wc -l)
fail=$(awk -F'\t' '$3=="fail"' "$CASES" | wc -l)
inspect=$(awk -F'\t' '$3=="inspect"' "$CASES" | wc -l)
suite_pass=$(awk -F'\t' '$6=="pass"' "$CASES" | wc -l)
overridden=$(awk -F'\t' '$7=="xquf30"' "$CASES" | wc -l)
{
  echo "XQUTS 1.0.1: $pass/$got_cases pass with the XQUF 3.0 overrides ($overridden cases), $suite_pass/$got_cases under the suite's own (XQUF 1.0) expected results; $fail fail, $inspect inspect ($got_states states)"
  echo
  awk -F'\t' '{k = ($5 == 1 ? "single-state" : "multi-state"); t[k]++; o[k " " $3]++}
       END {for (k in t) printf "%s: %d cases, %d pass, %d fail, %d inspect\n", k, t[k], o[k " pass"], o[k " fail"], o[k " inspect"]}' "$CASES" | sort
  echo "not passing in a depends-on group (optional feature): $(awk -F'\t' '$3!="pass" && $4 ~ /^\[depends-on /' "$CASES" | wc -l)"
  echo
  echo "XQUF 3.0 OVERRIDES ($overridden cases; the rule of each is in Xquf30Overrides.cs): case, under the suite's expected results -> with the override"
  awk -F'\t' '$7=="xquf30" {printf "  %-40s %s -> %s\n", $2, $6, $3}' "$CASES" | sort
  echo
  echo "per group (pass/total):"
  awk -F'\t' '{t[$1]++; if ($3=="pass") p[$1]++} END {for (g in t) printf "  %-60s %4d/%d\n", g, p[g], t[g]}' "$CASES" | sort
} > "$OUT/xquts-summary.txt"
sed -n '1p;3,6p' "$OUT/xquts-summary.txt"
sed -n '/^XQUF 3.0 OVERRIDES/,/^$/p' "$OUT/xquts-summary.txt"

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
echo "XQUTS gate: no case lost"
