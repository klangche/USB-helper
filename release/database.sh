#!/usr/bin/env bash
# Puts the hardware database into database/ from the database branch (#288): every build and test run uses the head of
# that branch. Every format this code reads (#334, the "[N] =" readers in DatabaseFormats.cs) that the branch has is
# copied into database/v<N>/. An older format may be missing; the newest must be there, or this fails. DATABASE_REF
# picks another commit, such as a pull request to the database branch; without it the branch head is fetched from
# origin. database/.source records that commit's full sha and date (#290): the app embeds it, to tell whether the
# database branch has newer files.
set -euo pipefail
cd "$(dirname "$0")/.."
formats=$(grep -oE '^\s*\[[0-9]+\] = ' src/UsbHelper.Core/Database/DatabaseFormats.cs | grep -oE '[0-9]+' | sort -n)
newest=$(echo "$formats" | tail -1)
if [ -n "${DATABASE_REF:-}" ]; then
  ref="$DATABASE_REF"
else
  git fetch --quiet --depth 1 origin database
  ref=FETCH_HEAD
fi
rm -rf database
mkdir database
copied=""
for version in $formats; do
  if git cat-file -e "$ref:v$version/base.yaml" 2>/dev/null; then
    mkdir "database/v$version"
    git archive "$ref" "v$version" | tar -x --strip-components=1 -C "database/v$version"
    copied="$copied v$version"
  fi
done
if [ ! -f "database/v$newest/base.yaml" ]; then
  echo "The database branch has no v$newest/base.yaml, the newest format this code reads." >&2
  exit 1
fi
git log -1 --format='%H%n%cI' "$ref" > database/.source
echo "Hardware database formats$copied from $(git rev-parse --short "$ref")."
