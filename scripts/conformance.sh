#!/usr/bin/env bash
# Runs the W3C QT3 suite against THIS repo's XQuery source and gates every test-set
# individually against a committed baseline.
#
#   ./scripts/conformance.sh                  # run the suite and check the baseline
#   CONFORMANCE_CONFIG=Debug ./scripts/...    # Release is the default and what ships
#   CONFORMANCE_UPDATE_BASELINE=1 ./scripts/  # rewrite the baseline from this run
#
# WHY THIS EXISTS AT ALL
#
# QT3 used to be measured only from phoenixmldb-xslt, whose conformance job builds the
# PINNED PhoenixmlDb.XQuery package. So changes on this repo's main were not measured
# against QT3 by anything, at any point, until a release moved that pin. The engine had
# 1,689 unit tests and no conformance floor.
#
# That is also why a 28-case QT3 difference could sit unexplained: nothing in the XSLT
# repo's output states WHICH XQuery it measured, and the answer was "a sibling checkout
# that had been left three days behind". A suite that gates one repo while building
# another's source cannot answer "did my change break anything".
#
# PER-SET GATING, NOT A TOTAL
#
# A total cannot express a regression: one set can lose cases while another gains more,
# and the aggregate goes UP. That hides a loss exactly when you are least suspicious.
# Every test-set's passing count is recorded and compared individually, and ANY set going
# down fails the run whatever the total did.
#
# RAISES ARE HAND-EDITED, DELIBERATELY
#
# CONFORMANCE_UPDATE_BASELINE=1 OVERWRITES — it will happily lower a set that regressed.
# A raise should be max(old, min(two runs)) so a flickering set cannot enshrine a lucky
# run, and a genuine loss stays visible instead of being absorbed. The script cannot
# enforce that; a reviewer can. A raise whose PR has no accompanying summary.txt from a
# CLEAN CONFIRMING RUN — one that is not either of the runs the raise was computed from —
# is incomplete.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJ="$ROOT/tests/PhoenixmlDb.XQuery.Conformance.Tests"
SUITES="$PROJ/TestData"
# QT3 is ~31,400 cases and takes minutes, not seconds. Under a short default it was killed
# and reported "TIMEOUT", which reads as a hang; it is not hung, it is big.
TIMEOUT="${CONFORMANCE_TIMEOUT:-3600}"
OUT="${CONFORMANCE_OUT:-$ROOT/conformance-results}"
CONFIG="${CONFORMANCE_CONFIG:-Release}"

if [ ! -e "$SUITES/qt3tests/catalog.xml" ]; then
  echo "error: QT3 suite missing at $SUITES/qt3tests — run ./scripts/fetch-conformance-suites.sh" >&2
  exit 1
fi

# The fixture reads the suite from here rather than from a path beside the test assembly —
# that older layout is what tied a conformance run to one machine.
export QT3_TEST_SUITE="$SUITES/qt3tests"
# ICU-based globalization is required for correct collation and normalize-unicode(); invariant
# mode silently changes results rather than failing loudly. Note this does NOT pin the current
# CULTURE — a runner with LANG=C still gets the invariant culture, which is a different thing
# and cost fn:default-language a real defect that only CI could see.
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=0

mkdir -p "$OUT"
: > "$OUT/summary.txt"

echo "building once (then run with --no-build)..."
if ! dotnet build "$PROJ/PhoenixmlDb.XQuery.Conformance.Tests.csproj" -c "$CONFIG" -f net10.0 \
      > "$OUT/build.log" 2>&1; then
  tail -20 "$OUT/build.log" >&2
  echo "error: build failed — see $OUT/build.log" >&2
  exit 1
fi

rev=$(git -C "$SUITES/qt3tests" rev-parse --short HEAD 2>/dev/null || echo "unpinned")
echo "qt3tests @ $rev" | tee -a "$OUT/summary.txt"
# The engine under test is this working tree, and saying so is the point: the XSLT repo's
# equivalent measures a pinned package, and confusing the two cost a day.
echo "engine    @ $(git -C "$ROOT" rev-parse --short HEAD 2>/dev/null || echo unknown)$([ -n "$(git -C "$ROOT" status --porcelain 2>/dev/null)" ] && echo ' (dirty)')" |
  tee -a "$OUT/summary.txt"
echo | tee -a "$OUT/summary.txt"

failed=0
started=$SECONDS
# verbosity=detailed is not noise, it IS the result. A test-set reports "Passed" as long as
# the fixture ran, so cases failing inside a set are invisible at default verbosity. The
# per-set tallies this gate reads exist only in the detailed output.
timeout "$TIMEOUT" dotnet test "$PROJ/PhoenixmlDb.XQuery.Conformance.Tests.csproj" \
    -c "$CONFIG" -f net10.0 --no-build \
    --logger "console;verbosity=detailed" \
    --logger "trx;LogFileName=qt3.trx" --results-directory "$OUT" > "$OUT/qt3.log" 2>&1
rc=$?
d=$((SECONDS - started))

if [ $rc -eq 124 ]; then
  line="TIMEOUT after ${TIMEOUT}s"
  failed=1
else
  passed=$(awk '/^ Results: /{split($2,a,"/"); p+=a[1]; t+=a[2]} END{print p"/"t}' "$OUT/qt3.log")
  cases=${passed#*/}; ok=${passed%/*}
  if [ -z "$cases" ] || [ "$cases" = "0" ]; then
    line="NO CASES REPORTED — refusing to call that a pass"
    failed=1
  else
    pct=$(awk -v a="$ok" -v b="$cases" 'BEGIN{printf "%.1f", 100*a/b}')
    line="$ok/$cases cases ${pct}%, $((cases - ok)) failed"
    [ $rc -ne 0 ] && failed=1
  fi
fi
printf '%-7s %4ds  %s\n' qt3 "$d" "$line" | tee -a "$OUT/summary.txt"

# ---- per-set regression gate -------------------------------------------------
BASELINE="${CONFORMANCE_BASELINE:-$ROOT/scripts/conformance-baseline.tsv}"
CURRENT="$OUT/per-set-current.tsv"
awk '/^ Running [0-9]+ tests from /{set=$NF}
     /^ Results: /{split($2,a,"/"); if(set!=""){print set"\t"a[1]"\t"a[2]; set=""}}' \
  "$OUT/qt3.log" > "$CURRENT"
sort -o "$CURRENT" "$CURRENT"

if [ "${CONFORMANCE_UPDATE_BASELINE:-0}" = "1" ]; then
  if [ -f "$BASELINE" ]; then
    # Carry each set's existing tolerance across the refresh — re-baselining must not
    # silently reset a tolerance someone set deliberately.
    awk -F'\t' 'NR==FNR{tol[$1]=$4; next}
                {t=($1 in tol && tol[$1]!="")?tol[$1]:""; print $1"\t"$2"\t"$3 (t==""?"":"\t" t)}' \
      "$BASELINE" "$CURRENT" > "$CURRENT.tol"
    awk -F'\t' 'NR==FNR{seen[$1]=1; next} !($1 in seen)' "$CURRENT" "$BASELINE" > "$BASELINE.keep"
    cat "$CURRENT.tol" "$BASELINE.keep" | sort -o "$BASELINE"
    rm -f "$BASELINE.keep" "$CURRENT.tol"
  else
    cp "$CURRENT" "$BASELINE"
  fi
  echo "per-set baseline updated: $BASELINE" | tee -a "$OUT/summary.txt"
elif [ -f "$BASELINE" ]; then
  # Optional 4th baseline column: cases a set may lose without failing the run. Use it only
  # for a set measured to flicker, and say why in the commit that adds it.
  regressed="$(awk -F'\t' '
    NR==FNR { base[$1]=$2; tol[$1]=($4==""?0:$4); next }
    ($1 in base) && $2 < base[$1] - tol[$1] {
      printf "  %s: %d -> %d  (-%d, tolerance %d)\n", $1, base[$1], $2, base[$1]-$2, tol[$1] }
  ' "$BASELINE" "$CURRENT")"
  gained="$(awk -F'\t' '
    NR==FNR { base[$1]=$2; next }
    ($1 in base) && $2 > base[$1] { printf "  %s: %d -> %d  (+%d)\n", $1, base[$1], $2, $2-base[$1] }
  ' "$BASELINE" "$CURRENT")"
  newsets="$(awk -F'\t' 'NR==FNR{base[$1]=1; next} !($1 in base){printf "  %s (%d/%d)\n", $1, $2, $3}' \
              "$BASELINE" "$CURRENT")"
  # A baselined set that reported NO result. The checks above iterate CURRENT, so a set that
  # never ran is never visited — and a crashed test host makes sets vanish silently. A set
  # with no verdict is worse news than one with a lower count, so it fails the run too.
  noresult="$(awk -F'\t' 'NR==FNR{cur[$1]=1; next}
                          !($1 in cur){printf "  %s: baselined %d, NO RESULT this run\n", $1, $2}' \
                "$CURRENT" "$BASELINE")"
  [ -n "$gained" ]  && { echo "per-set GAINS:"    | tee -a "$OUT/summary.txt"; echo "$gained"  | tee -a "$OUT/summary.txt"; }
  [ -n "$newsets" ] && { echo "per-set NEW sets:" | tee -a "$OUT/summary.txt"; echo "$newsets" | tee -a "$OUT/summary.txt"; }
  if [ -n "$regressed" ]; then
    echo "PER-SET REGRESSION — these test-sets lost cases:" | tee -a "$OUT/summary.txt"
    echo "$regressed" | tee -a "$OUT/summary.txt"
    echo "If the drop is intended, re-run with CONFORMANCE_UPDATE_BASELINE=1 to re-baseline." |
      tee -a "$OUT/summary.txt"
    failed=1
  fi
  if [ -n "$noresult" ]; then
    echo "PER-SET NO RESULT — baselined test-sets did not report:" | tee -a "$OUT/summary.txt"
    echo "$noresult" | tee -a "$OUT/summary.txt"
    failed=1
  fi
else
  echo "no per-set baseline at $BASELINE — create it with CONFORMANCE_UPDATE_BASELINE=1" |
    tee -a "$OUT/summary.txt"
fi

total=$((SECONDS - started))
printf '\nQT3 in %dm%02ds — logs in %s\n' $((total / 60)) $((total % 60)) "$OUT" |
  tee -a "$OUT/summary.txt"
[ $failed -eq 0 ] || echo "conformance run failed" | tee -a "$OUT/summary.txt"
exit $failed
