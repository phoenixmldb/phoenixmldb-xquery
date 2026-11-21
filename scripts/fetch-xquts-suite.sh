#!/usr/bin/env bash
# Fetches the W3C XQuery Update Facility 1.0 test suite (XQUTS 1.0.1), the suite for
# insert/delete/replace/rename, copy-modify and fn:put. QT3 has no XQuery Update coverage.
#
# The suite is a zip on dev.w3.org, not a repository, so it is pinned by its SHA-256: a
# changed file fails the fetch instead of moving the baseline silently. It is never committed
# and never copied into the test output. Point the tests at it with:
#
#   export XQUTS_TEST_SUITE=<repo>/tests/PhoenixmlDb.XQuery.Conformance.Tests/TestData/xquts
#
# The suite is under the W3C test suite licence; see its Overview.html.
set -euo pipefail

URL=https://dev.w3.org/2007/xquery-update-10-test-suite/Archive/XQUTS_1_0_1.zip
SHA256=e0e4f374a6cab5c87244f3936967bb81b004a50b4732a109139de1e921451be3

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEST="${XQUTS_TEST_SUITE:-$ROOT/tests/PhoenixmlDb.XQuery.Conformance.Tests/TestData/xquts}"

if [ -e "$DEST/XQUTSCatalog.xml" ] && [ "$(cat "$DEST/.sha256" 2>/dev/null)" = "$SHA256" ]; then
  echo "xquts: already at $SHA256"
  exit 0
fi

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
curl -sSfL -o "$tmp/suite.zip" "$URL"
actual="$(sha256sum "$tmp/suite.zip" | cut -d' ' -f1)"
if [ "$actual" != "$SHA256" ]; then
  echo "error: XQUTS zip SHA-256 is $actual, expected $SHA256 — refusing to unpack" >&2
  exit 1
fi
# Replace only a directory this script made (it holds .sha256) or an empty one: DEST can come
# from the environment, and a wrong value must not delete someone's files.
if [ -d "$DEST" ] && [ -n "$(ls -A "$DEST")" ]; then
  if [ ! -e "$DEST/.sha256" ]; then
    echo "error: $DEST exists, is not empty and was not made by this script — not replacing it" >&2
    exit 1
  fi
  rm -rf "$DEST"
fi
mkdir -p "$DEST"
unzip -q "$tmp/suite.zip" -d "$DEST"
echo "$SHA256" > "$DEST/.sha256"
if [ ! -e "$DEST/XQUTSCatalog.xml" ]; then
  echo "error: no XQUTSCatalog.xml in the unpacked zip" >&2
  exit 1
fi
# Counted by parsing, not grep: the catalog keeps one removed <test-case> inside a comment.
echo "xquts: $(python3 -I -c 'import sys, xml.etree.ElementTree as E; print(sum(1 for _ in E.parse(sys.argv[1]).iter("{http://www.w3.org/2005/02/query-test-update}test-case")))' "$DEST/XQUTSCatalog.xml") cases at $DEST"
