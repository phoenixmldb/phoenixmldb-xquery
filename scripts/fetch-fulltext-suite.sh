#!/usr/bin/env bash
# Fetches the W3C XQuery and XPath Full Text 1.0 test suite (XQFTTS 1.0.4), the suite for
# `contains text`. QT3 has no full-text coverage.
#
# The suite is a zip on dev.w3.org, not a repository, so it is pinned by its SHA-256: a
# changed file fails the fetch instead of moving the baseline silently. It is never committed
# (24 MB) and never copied into the test output. Point the tests at it with:
#
#   export XQFTTS_TEST_SUITE=<repo>/tests/PhoenixmlDb.XQuery.Conformance.Tests/TestData/xqftts
#
# The suite is under the W3C test suite licence; see its Overview.html.
set -euo pipefail

URL=https://dev.w3.org/2007/xpath-full-text-10-test-suite/XQFTTS_1_0_4.zip
SHA256=7f272de59c9fab87c1bbb8e8860855aa9b2c20261e401b2dd3d849b90c8d25e0

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEST="${XQFTTS_TEST_SUITE:-$ROOT/tests/PhoenixmlDb.XQuery.Conformance.Tests/TestData/xqftts}"

if [ -e "$DEST/XQFTTSCatalog.xml" ] && [ "$(cat "$DEST/.sha256" 2>/dev/null)" = "$SHA256" ]; then
  echo "xqftts: already at $SHA256"
  exit 0
fi

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
curl -sSfL -o "$tmp/suite.zip" "$URL"
actual="$(sha256sum "$tmp/suite.zip" | cut -d' ' -f1)"
if [ "$actual" != "$SHA256" ]; then
  echo "error: XQFTTS zip SHA-256 is $actual, expected $SHA256 — refusing to unpack" >&2
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
if [ ! -e "$DEST/XQFTTSCatalog.xml" ]; then
  echo "error: no XQFTTSCatalog.xml in the unpacked zip" >&2
  exit 1
fi
echo "xqftts: $(grep -c '<test-case ' "$DEST/XQFTTSCatalog.xml") cases at $DEST"
