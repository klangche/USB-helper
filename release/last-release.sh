#!/usr/bin/env bash
# Prints the last release tag vX.Y.Z (the highest version), or nothing when there is none.
# Stdin: the tag names, one per line (git tag --list). Beta tags vX.Y.Z-beta.N and other tags are ignored.
set -euo pipefail

{ grep -E '^v[0-9]+\.[0-9]+\.[0-9]+$' || true; } | sort -V | tail -n 1
