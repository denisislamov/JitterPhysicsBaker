#!/usr/bin/env bash
#
# Publishes Packages/com.datasakura.jitter-physics-baker to its standalone repository
# (https://github.com/denisislamov/jitter-physics-baker.git) using `git subtree split`,
# so the package sits at the ROOT of that repo — which is what Unity Package Manager
# expects for "Add package from git URL".
#
# Usage:
#   tools/publish-package.sh              # publish current HEAD to main
#   tools/publish-package.sh v0.1.0       # publish and also push tag v0.1.0
#
set -euo pipefail

PREFIX="Packages/com.datasakura.jitter-physics-baker"
REMOTE_NAME="package"
REMOTE_URL="https://github.com/denisislamov/jitter-physics-baker.git"
BRANCH="main"
TMP_BRANCH="package-publish-tmp"
TAG="${1:-}"

cd "$(git rev-parse --show-toplevel)"

if [[ -n "$(git status --porcelain)" ]]; then
  echo "error: working tree is dirty - commit or stash first." >&2
  exit 1
fi

# A truncated .meta breaks the package once it is installed from a git URL,
# because Library/PackageCache is immutable and Unity cannot repair it there.
echo "==> validating package .meta files"
python3 "$(dirname "$0")/verify-package-meta.py"

MANIFEST_VERSION="$(python3 -c \
  "import json;print(json.load(open('$PREFIX/package.json'))['version'])")"

# The tag and the manifest version must agree, otherwise Package Manager shows one
# version while the git ref serves another.
if [[ -n "$TAG" && "$TAG" != "v$MANIFEST_VERSION" ]]; then
  echo "error: tag '$TAG' does not match package.json version '$MANIFEST_VERSION'." >&2
  echo "       Bump package.json to ${TAG#v} (or tag v$MANIFEST_VERSION) first." >&2
  exit 1
fi

# Never publish a version that is not newer than the highest tag already out there.
git fetch --quiet "$REMOTE_NAME" 'refs/tags/*:refs/tags/published/*' --force 2>/dev/null || true
HIGHEST="$(git tag --list 'published/v*' \
  | sed 's|published/v||' \
  | sort -t. -k1,1n -k2,2n -k3,3n \
  | tail -1)"
if [[ -n "$HIGHEST" ]]; then
  NEWEST="$(printf '%s\n%s\n' "$HIGHEST" "$MANIFEST_VERSION" \
    | sort -t. -k1,1n -k2,2n -k3,3n | tail -1)"
  if [[ "$NEWEST" == "$HIGHEST" && "$MANIFEST_VERSION" != "$HIGHEST" ]]; then
    echo "error: version $MANIFEST_VERSION is older than the published $HIGHEST." >&2
    exit 1
  fi
fi
echo "==> publishing version $MANIFEST_VERSION (highest published: ${HIGHEST:-none})"

if ! git remote get-url "$REMOTE_NAME" >/dev/null 2>&1; then
  echo "==> adding remote '$REMOTE_NAME' -> $REMOTE_URL"
  git remote add "$REMOTE_NAME" "$REMOTE_URL"
fi

echo "==> splitting '$PREFIX' into '$TMP_BRANCH'"
git branch -D "$TMP_BRANCH" >/dev/null 2>&1 || true
git subtree split --prefix="$PREFIX" -b "$TMP_BRANCH"

# The working copy always shows real files, so LFS pointers can only be caught by
# inspecting the blobs that are actually about to be pushed. UPM clones without
# LFS, so a pointer would reach consumers as a ~130-byte text stub.
echo "==> checking pushed blobs for Git LFS pointers"
lfs_pointers=0
while IFS= read -r file; do
  if git cat-file -p "$TMP_BRANCH:$file" 2>/dev/null \
      | head -c 40 | grep -q "git-lfs.github.com"; then
    echo "error: '$file' is a Git LFS pointer - UPM cannot resolve it." >&2
    lfs_pointers=1
  fi
done < <(git ls-tree -r --name-only "$TMP_BRANCH" \
         | grep -iE '\.(dll|so|dylib|bytes|a)$' || true)

if [[ "$lfs_pointers" -ne 0 ]]; then
  echo "       Add an un-LFS rule to $PREFIX/.gitattributes, then re-add the files." >&2
  git branch -D "$TMP_BRANCH" >/dev/null
  exit 1
fi

echo "==> pushing to $REMOTE_NAME/$BRANCH"
git push "$REMOTE_NAME" "$TMP_BRANCH:$BRANCH"

if [[ -n "$TAG" ]]; then
  echo "==> tagging $TAG"
  git tag -f "$TAG" "$TMP_BRANCH"
  git push -f "$REMOTE_NAME" "refs/tags/$TAG"
fi

git branch -D "$TMP_BRANCH"

echo
echo "Done. Install in another project with:"
echo "  https://github.com/denisislamov/jitter-physics-baker.git${TAG:+#$TAG}"
