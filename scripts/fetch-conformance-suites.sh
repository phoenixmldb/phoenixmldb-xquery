#!/usr/bin/env bash
# Fetches the W3C QT3 conformance suite this repo measures against.
#
# The suites are pinned to exact revisions. That pin is the point: a conformance
# percentage is not reproducible, and two runs are not comparable, unless the suite
# revision is known. Bump a SHA deliberately, in its own commit, and re-baseline.
#
# The suites are never committed (~470 MB, ~30,000 files) and never copied into the
# test output — see ConformanceSuites.cs. Point the tests at them with:
#
#   export QT3_TEST_SUITE=<repo>/tests/PhoenixmlDb.XQuery.Conformance.Tests/TestData/qt3tests
#
# Without those, the fixtures fall back to the same paths beside the test assembly,
# and if neither exists the suites report as unavailable and the tests skip quietly.
set -euo pipefail

QT3_SHA=201a6e466940cdfc727f4babfedcde5332b9f578

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEST="$ROOT/tests/PhoenixmlDb.XQuery.Conformance.Tests/TestData"

fetch() {
  local name=$1 url=$2 sha=$3
  local dir="$DEST/$name"

  if [ -d "$dir/.git" ] && [ "$(git -C "$dir" rev-parse HEAD 2>/dev/null)" = "$sha" ]; then
    echo "$name: already at $sha"
    return
  fi

  echo "$name: fetching $sha"
  mkdir -p "$dir"
  git -C "$dir" init -q
  git -C "$dir" remote add origin "$url" 2>/dev/null || git -C "$dir" remote set-url origin "$url"
  # Fetch the one commit rather than cloning history: these repos are large and no
  # test needs their past.
  git -C "$dir" fetch --depth 1 origin "$sha" -q
  git -C "$dir" checkout -q FETCH_HEAD
  echo "$name: at $(git -C "$dir" rev-parse --short HEAD)"
}

mkdir -p "$DEST"
fetch qt3tests    https://github.com/w3c/qt3tests.git    "$QT3_SHA"

cat <<EOF

Suites ready under $DEST
Run conformance with:
  QT3_TEST_SUITE="$DEST/qt3tests" \\
  dotnet test tests/PhoenixmlDb.XQuery.Conformance.Tests
EOF
