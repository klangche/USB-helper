#!/usr/bin/env bash
# Puts the hardware database into database/ from the database branch (#288): every build and test run uses the head of
# that branch, in the folder of the format this code reads (v<SchemaVersion>/). DATABASE_REF picks another commit, such
# as a pull request to the database branch; without it the branch head is fetched from origin. database/.source records
# that commit's full sha and date (#290): the app embeds it, to tell whether the database branch has newer files.
set -euo pipefail
cd "$(dirname "$0")/.."
version=$(grep -oE 'SchemaVersion = [0-9]+' src/UsbHelper.Core/Database/HardwareDatabase.cs | grep -oE '[0-9]+$')
if [ -n "${DATABASE_REF:-}" ]; then
  ref="$DATABASE_REF"
else
  git fetch --quiet --depth 1 origin database
  ref=FETCH_HEAD
fi
rm -rf database
mkdir database
git archive "$ref" "v$version" | tar -x --strip-components=1 -C database
if [ ! -f database/base.yaml ]; then
  echo "The database branch has no v$version/base.yaml." >&2
  exit 1
fi
git log -1 --format='%H%n%cI' "$ref" > database/.source
echo "Hardware database format $version from $(git rev-parse --short "$ref")."
