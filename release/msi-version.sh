#!/usr/bin/env bash
# Prints the MSI ProductVersion (major.minor.build) for a release version (#124).
# Argument: X.Y.Z, X.Y.Z-beta.N or another pre-release such as 0.0.0-pr.130.7.
# Windows Installer compares only major.minor.build, as numbers (major and minor up to 255, build up to 65535), and
# has no pre-release part. So the patch and the beta share the build field: build = Z * 1000 + N for beta N,
# Z * 1000 + 999 for the release X.Y.Z. Betas of X.Y.Z then sort after X.Y.(Z-1) and before X.Y.Z. Other pre-releases
# (pull request builds) get build Z * 1000 and sort below every beta and release of X.Y.Z.
set -euo pipefail

version=$1
if [[ ! $version =~ ^([0-9]+)\.([0-9]+)\.([0-9]+)(-(.+))?$ ]]; then
  echo "msi-version.sh: '$version' is not a release version" >&2
  exit 1
fi
major=${BASH_REMATCH[1]} minor=${BASH_REMATCH[2]} patch=${BASH_REMATCH[3]} pre=${BASH_REMATCH[5]}
if [[ -z $pre ]]; then
  extra=999
elif [[ $pre =~ ^beta\.([0-9]+)$ ]]; then
  extra=$((10#${BASH_REMATCH[1]}))
  if ((extra < 1 || extra > 998)); then
    echo "msi-version.sh: beta $extra does not fit (1 to 998)" >&2
    exit 1
  fi
else
  extra=0
fi
build=$((10#$patch * 1000 + extra))
if ((10#$major > 255 || 10#$minor > 255 || build > 65535)); then
  echo "msi-version.sh: '$version' does not fit an MSI version (255.255.65535)" >&2
  exit 1
fi
echo "$((10#$major)).$((10#$minor)).$build"
