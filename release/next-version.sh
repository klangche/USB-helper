#!/usr/bin/env bash
# Prints the next release version X.Y.Z.
# Argument: the last release tag (vX.Y.Z, never a beta tag), or empty when there is none.
# Stdin: the titles of the pull requests merged since that tag, one per line.
# "Fixed:" and "Security:" bump PATCH, "Added:", "Changed:" and "Removed:" bump MINOR,
# a "!" before the colon bumps MAJOR, but only from 1.0.0 on: in 0.x (initial development)
# it bumps MINOR. Only a title starting with "Release 1.0.0:" leaves 0.x.
# The first release is 0.1.0. Prints nothing when no title asks for a release.
set -euo pipefail

last="${1:-}"
if [[ -n $last && ! $last =~ ^v[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "next-version.sh: '$last' is not a release tag vX.Y.Z" >&2
  exit 1
fi
bump=0
first_major=0
while IFS= read -r title || [[ -n $title ]]; do
  level=0
  if [[ $title =~ ^Release\ 1\.0\.0: ]]; then
    first_major=1
  elif [[ $title =~ ^(Added|Changed|Removed|Fixed|Security)!: ]]; then
    level=3
  elif [[ $title =~ ^(Added|Changed|Removed): ]]; then
    level=2
  elif [[ $title =~ ^(Fixed|Security): ]]; then
    level=1
  fi
  if ((level > bump)); then
    bump=$level
  fi
done

if [[ -z $last ]]; then
  if ((bump > 0)); then
    echo 0.1.0
  fi
  exit 0
fi

IFS=. read -r major minor patch <<<"${last#v}"
if ((major == 0)); then
  if ((first_major)); then
    echo 1.0.0
    exit 0
  fi
  if ((bump == 3)); then
    bump=2
  fi
fi
if ((bump == 0)); then
  exit 0
fi
case $bump in
  3) echo "$((major + 1)).0.0" ;;
  2) echo "$major.$((minor + 1)).0" ;;
  1) echo "$major.$minor.$((patch + 1))" ;;
esac
