#!/usr/bin/env bash
# Prints the next beta version X.Y.Z-beta.N.
# Argument: the next release version X.Y.Z.
# Stdin: the tag names, one per line (git tag --list). N is the number of vX.Y.Z-beta.* tags + 1.
set -euo pipefail

version=$1
count=$({ grep -cE "^v${version//./\\.}-beta\.[0-9]+$" || true; })
echo "$version-beta.$((count + 1))"
