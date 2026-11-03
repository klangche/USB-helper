#!/usr/bin/env bash
# Checks release/msi-version.sh. Run from the repository root.
set -euo pipefail

failed=0
check() {
  local expected=$1 version=$2 actual
  actual=$(release/msi-version.sh "$version" 2>/dev/null || echo "error")
  if [[ $actual != "$expected" ]]; then
    echo "FAIL: '$version': expected '$expected', got '$actual'"
    failed=1
  fi
}

check "0.6.1" "0.6.0-beta.1"
check "0.6.12" "0.6.0-beta.12"
check "0.6.999" "0.6.0"
check "0.6.1001" "0.6.1-beta.1"
check "1.0.999" "1.0.0"
check "0.0.0" "0.0.0-pr.130.7" # pull request builds sort below every release
check "error" "0.6.0-beta.999" # would equal 0.6.0
check "error" "0.6.66-beta.1" # build field above 65535
check "error" "256.0.0" # major field above 255
check "error" "0.6"

# Windows Installer compares major.minor.build as numbers: every version must sort as the release versions do.
ordered=(0.0.0-pr.1.1 0.5.0 0.6.0-beta.1 0.6.0-beta.2 0.6.0-beta.10 0.6.0 0.6.1-beta.1 0.6.1 0.7.0-beta.1 0.7.0 1.0.0-beta.3 1.0.0 1.0.1)
previous=""
for version in "${ordered[@]}"; do
  msi=$(release/msi-version.sh "$version")
  if [[ -n $previous ]] && ! printf '%s\n%s\n' "$previous" "$msi" | sort -C -t. -k1,1n -k2,2n -k3,3n || [[ $previous == "$msi" ]]; then
    echo "FAIL: $version ($msi) does not sort after the version before it ($previous)"
    failed=1
  fi
  previous=$msi
done

if ((failed)); then
  exit 1
fi
echo "msi-version.sh: all checks passed"
