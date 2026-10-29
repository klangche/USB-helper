#!/usr/bin/env bash
# Prints the next release version X.Y.Z.
# Argument: the last release tag (vX.Y.Z), or empty when there is none.
# Stdin: the titles of the pull requests merged since that tag, one per line.
# "Fixed:" and "Security:" bump PATCH, "Added:", "Changed:" and "Removed:" bump MINOR,
# a "!" before the colon bumps MAJOR. The first release is 0.1.0.
# Prints nothing when no title asks for a release.
set -euo pipefail

last="${1:-}"
bump=0
while IFS= read -r title; do
  level=0
  if [[ $title =~ ^(Added|Changed|Removed|Fixed|Security)!: ]]; then
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

if ((bump == 0)); then
  exit 0
fi
if [[ -z $last ]]; then
  echo 0.1.0
  exit 0
fi

IFS=. read -r major minor patch <<<"${last#v}"
case $bump in
  3) echo "$((major + 1)).0.0" ;;
  2) echo "$major.$((minor + 1)).0" ;;
  1) echo "$major.$minor.$((patch + 1))" ;;
esac
