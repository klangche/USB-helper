#!/usr/bin/env bash
# Checks release/next-version.sh. Run from the repository root.
set -euo pipefail

failed=0
check() {
  local expected=$1 last=$2 titles=$3 actual
  actual=$(printf '%s' "$titles" | release/next-version.sh "$last")
  if [[ $actual != "$expected" ]]; then
    echo "FAIL: last='$last' titles='$titles': expected '$expected', got '$actual'"
    failed=1
  fi
}

check "0.1.0" "" $'Added: a\n'
check "0.1.0" "" $'Changed!: a\n'
check "" "" $'Merge something\n'
check "0.1.1" "v0.1.0" $'Fixed: a\n'
check "0.1.1" "v0.1.0" 'Fixed: no final newline'
check "0.2.0" "v0.1.0" $'Fixed: a\nAdded: b\n'
check "0.2.0" "v0.1.3" $'Changed!: a\nFixed: b\n'
check "0.5.0" "v0.4.0" $'Changed!: a\nAdded: b\n'
check "2.0.0" "v1.2.3" $'Changed!: a\n'
check "1.0.0" "v0.9.1" $'Release 1.0.0: first stable version\nFixed: b\n'
check "1.2.4" "v1.2.3" $'Release 1.0.0: again\nFixed: b\n'
check "" "v1.2.3" $'Release 1.0.0: again\n'
check "1.2.4" "v1.2.3" $'Security: a\n'
check "1.3.0" "v1.2.3" $'Removed: a\n'
check "" "v1.2.3" $'docs only\nfixed: lowercase\n'
check "" "v1.2.3" ''

# The last release is never a beta tag: refuse one instead of counting from it.
if printf 'Fixed: a\n' | release/next-version.sh v0.2.0-beta.1 >/dev/null 2>&1; then
  echo "FAIL: last='v0.2.0-beta.1' was accepted"
  failed=1
fi

if ((failed)); then
  exit 1
fi
echo "next-version.sh: all checks passed"
