#!/usr/bin/env bash
# Fails when a PhoenixmlDb.* package pin is BEHIND the latest published version.
#
# Why this exists. Engines and the tools that carry them live in different repos and talk to
# each other through NuGet pins, and nothing in the build notices when a pin goes stale. It went
# unnoticed for nine minor versions: PhoenixmlDb.Xslt.Cli shipped an engine from June while
# calling itself current. The same mechanism one level up put XQuery 1.6.12 inside the published
# Xslt 1.6.13, because that pack predated the XQuery 1.6.13 release.
#
# A pin AHEAD of what is published is fine — that is the normal state mid-release, when the
# engine package has not been pushed yet. Only "behind" is a failure.
#
# Unreachable NuGet is also a failure, deliberately. A check that quietly passes when it cannot
# verify anything is the failure mode this project has spent a week removing from its test
# harness; it will not be reintroduced here. Set ALLOW_OFFLINE_PIN_CHECK=1 to skip on purpose.
set -uo pipefail

props="${1:-Directory.Packages.props}"
[ -f "$props" ] || { echo "check-pins: no $props here"; exit 2; }

if [ "${ALLOW_OFFLINE_PIN_CHECK:-0}" = "1" ]; then
  echo "check-pins: skipped (ALLOW_OFFLINE_PIN_CHECK=1)"; exit 0
fi

fail=0; checked=0
while read -r id ver; do
  lower=$(echo "$id" | tr '[:upper:]' '[:lower:]')
  latest=$(curl -sS --max-time 20 "https://api.nuget.org/v3-flatcontainer/$lower/index.json" 2>/dev/null \
           | python3 -c "import sys,json;print(json.load(sys.stdin)['versions'][-1])" 2>/dev/null)
  if [ -z "$latest" ]; then
    echo "FAIL  $id: could not reach nuget.org (cannot verify — not treating that as a pass)"
    fail=1; continue
  fi
  checked=$((checked+1))
  newest=$(printf '%s\n%s\n' "$ver" "$latest" | sort -V | tail -1)
  # A pin may declare that it trails deliberately. PhoenixmlDb.XQuery's Xslt pin does: the
  # `xquery` tool depends on Xslt, but that repo publishes library and CLI at one version, so it
  # cannot pin an Xslt that does not exist yet — it trails by exactly one release, every train.
  # Enforce that stated policy rather than exempting the pin outright: trailing by ONE is fine,
  # trailing by more means a train was skipped, which is the drift this check is for.
  allow=$(grep -B6 "Include=\"$id\"" "$props" | grep -oE 'check-pins: trails-by-([0-9]+)' | grep -oE '[0-9]+$' | tail -1)
  allow=${allow:-0}
  if [ "$ver" = "$latest" ]; then
    echo "ok    $id $ver"
  elif [ "$newest" = "$ver" ]; then
    echo "ahead $id $ver (published $latest) — expected mid-release"
  else
    behind=$(curl -sS --max-time 20 "https://api.nuget.org/v3-flatcontainer/$lower/index.json" 2>/dev/null \
             | python3 -c "
import sys,json
vs=json.load(sys.stdin)['versions']
try: print(len(vs)-1-vs.index('$ver'))
except ValueError: print(999)
" 2>/dev/null)
    behind=${behind:-999}
    if [ "$allow" -gt 0 ] && [ "$behind" -le "$allow" ]; then
      echo "ok    $id $ver (trails published $latest by $behind, policy allows $allow)"
    else
      echo "FAIL  $id $ver is BEHIND published $latest by $behind release(s), policy allows $allow"
      fail=1
    fi
  fi
done < <(grep -oE '<PackageVersion Include="(PhoenixmlDb[^"]*)" Version="([^"]+)"' "$props" \
         | sed -E 's/.*Include="([^"]+)" Version="([^"]+)".*/\1 \2/')

[ "$checked" -eq 0 ] && [ "$fail" -eq 0 ] && { echo "check-pins: no PhoenixmlDb pins found in $props"; exit 2; }
exit $fail
