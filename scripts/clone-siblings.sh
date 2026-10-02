#!/usr/bin/env bash
# Clones the sibling engine repos next to this checkout so CI builds and tests against their
# SOURCE (PHOENIXML_DEV=1; see Directory.Build.targets), not their published NuGet packages.
#
# Why. Building against packages meant a cross-repo defect surfaced only after a release had been
# published: XQuery 2.5.0's xml:id schema bug was found by Xslt CI against the new package, which
# forced 2.5.1 an hour later, and fixes merged to XQuery main did not reach Xslt's tests until the
# next release train. Ordinary builds now track the siblings' source; only the release train
# (a v* tag) switches to package pins, which check-release-train.sh enforces.
#
# Which ref. A branch of the SAME NAME as the one being built, when the sibling has one, so a
# change spanning repos is built and tested together; otherwise the sibling's main.
#
# Fails closed: a clone that fails stops the job. Prints the SHA of every sibling it used, so a
# run records exactly what it was built against.
set -euo pipefail

parent="$(cd "$(dirname "$0")/../.." && pwd)"
branch="${GITHUB_HEAD_REF:-${GITHUB_REF_NAME:-}}"
# Directory.Build.targets refuses dev mode under CI unless this marker exists: it says the
# siblings came from committed refs, and which ones.
marker="$parent/.phoenixml-ci-siblings"
: > "$marker"

for repo in "$@"; do
  url="https://github.com/phoenixmldb/$repo.git"
  dest="$parent/$repo"
  ref=main
  if [ -n "$branch" ] && [ "$branch" != main ] \
     && git ls-remote --exit-code --heads "$url" "$branch" >/dev/null 2>&1; then
    ref="$branch"
  fi
  rm -rf "$dest"
  git clone --quiet --depth 1 --branch "$ref" "$url" "$dest"
  line="sibling $repo @ $ref $(git -C "$dest" rev-parse --short HEAD)"
  echo "$line"
  echo "$line" >> "$marker"
done
