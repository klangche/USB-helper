#!/usr/bin/env bash
# Checks release/last-release.sh and release/beta-version.sh. Run from the repository root.
set -euo pipefail

failed=0
check() {
  local expected=$1 actual=$2 what=$3
  if [[ $actual != "$expected" ]]; then
    echo "FAIL: $what: expected '$expected', got '$actual'"
    failed=1
  fi
}

last() { printf '%s' "$1" | release/last-release.sh; }
beta() { printf '%s' "$2" | release/beta-version.sh "$1"; }

check "" "$(last '')" "last release, no tags"
check "v0.1.0" "$(last $'v0.1.0\n')" "last release, one tag"
check "v0.1.0" "$(last $'v0.1.0\nv0.2.0-beta.1\nv0.2.0-beta.2\n')" "last release ignores beta tags"
check "" "$(last $'v0.1.0-beta.1\n')" "last release, only beta tags"
check "v0.10.0" "$(last $'v0.9.0\nv0.10.0\nv0.2.0\n')" "last release by version, not by text"
check "v1.0.0" "$(last $'v0.9.0\nv1.0.0\nother\nv2\n')" "last release ignores other tags"

check "0.2.0-beta.1" "$(beta 0.2.0 '')" "first beta, no tags"
check "0.2.0-beta.1" "$(beta 0.2.0 $'v0.1.0\nv0.1.1-beta.1\n')" "first beta of a version"
check "0.2.0-beta.3" "$(beta 0.2.0 $'v0.1.0\nv0.2.0-beta.1\nv0.2.0-beta.2\n')" "next beta counts up"
check "0.2.0-beta.2" "$(beta 0.2.0 $'v0.2.0-beta.1\nv0.2.0-beta.1x\nv10.2.0-beta.1\n')" "only exact beta tags count"

if ((failed)); then
  exit 1
fi
echo "last-release.sh, beta-version.sh: all checks passed"
