#!/usr/bin/env bash
# Refuses to pack a release whose engine pins are not the release version.
#
# This is the check that would have stopped the artifact Martin Honnen was given. The published
# PhoenixmlDb.Xslt 1.6.13 depends on PhoenixmlDb.XQuery 1.6.12, because the XSLT tag was cut
# before the XQuery 1.6.13 push. Nothing was "wrong" at pack time — the pin was the latest
# published version — so a staleness check cannot catch it. Only an equality check can:
#
#   if this train is 1.6.14, every train-locked pin must read 1.6.14.
#
# Why this repo needs it, and why only on the CLI tag. PhoenixmlDb.XQuery.Cli needs
# PhoenixmlDb.Xslt (for fn:transform) while PhoenixmlDb.Xslt needs PhoenixmlDb.XQuery. The
# library and the CLI therefore have DIFFERENT dependency sets, and publishing them on one tag
# forced the CLI's Xslt pin to point at the previous train — the trails-by-1 policy.
#
# Splitting the tags lets each artifact be checked against what it actually needs:
#
#   v<version>      library only.  Depends on Core, which runs its own cadence. Not train-locked.
#   cli-v<version>  CLI only.      Xslt of this train is published by now, so the pin MUST equal
#                                  the train, and this check enforces it.
#
# trails-by-1 stays on that pin as well, and the two are not redundant. check-pins.sh tolerates
# the window between Xslt publishing and this repo bumping; this check closes that window at the
# only moment it matters, which is packing the CLI. Tolerance between trains, equality at the
# tag.
#
# Mark a pin with "check-pins: train-locked" in the comment above it to opt in. Packages on
# their own cadence (Core) are deliberately not locked; scripts/check-pins.sh still stops those
# from falling behind what is published.
set -uo pipefail

version="${1:-}"
props="${2:-Directory.Packages.props}"
[ -n "$version" ] || { echo "usage: check-release-train.sh <release-version> [props]"; exit 2; }
version="${version#v}"
[ -f "$props" ] || { echo "check-release-train: no $props here"; exit 2; }

fail=0 locked=0
while read -r id ver; do
  if ! grep -B8 "Include=\"$id\"" "$props" | grep -q 'check-pins: train-locked'; then
    echo "free  $id $ver (not train-locked)"
    continue
  fi
  locked=$((locked+1))
  if [ "$ver" = "$version" ]; then
    echo "ok    $id $ver == train $version"
  else
    echo "FAIL  $id is pinned $ver but this train is $version"
    echo "      Packing now ships a tool whose engine is not the one this release tested."
    fail=1
  fi
done < <(grep -oE '<PackageVersion Include="(PhoenixmlDb[^"]*)" Version="([^"]+)"' "$props" \
         | sed -E 's/.*Include="([^"]+)" Version="([^"]+)".*/\1 \2/')

if [ "$locked" -eq 0 ]; then
  echo "check-release-train: no train-locked pins found — mark them with 'check-pins: train-locked'"
  exit 2
fi
exit $fail
