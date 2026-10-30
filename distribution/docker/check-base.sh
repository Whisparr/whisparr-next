#!/usr/bin/env bash
# Decide whether the published image needs rebuilding on a new base image.
#
# Usage: check-base.sh <image:moving-tag> <release-tag-or-empty>
# Prints one of: rebuild, current, no_release.
# Exits non-zero (printing nothing) when the base or published image cannot be
# read for a reason other than the published image being absent or unreadable.
set -u

base="ghcr.io/hotio/base:alpinevpn"
label="org.opencontainers.image.base.digest"

image="${1:?usage: check-base.sh <image:moving-tag> <release-tag-or-empty>}"
release="${2:-}"

if [ -z "$release" ]; then
    echo no_release
    exit 0
fi

base_json="$(docker buildx imagetools inspect "$base" --format '{{json .Manifest.Digest}}')" || exit 1
base_digest="$(printf '%s' "$base_json" | jq -r '.')" || exit 1
if [ -z "$base_digest" ] || [ "$base_digest" = "null" ]; then
    exit 1
fi

# A missing, denied or unknown image means there is no readable image: rebuild.
# Any other failure (timeout, 5xx) is a registry problem: exit non-zero, print nothing.
errfile="$(mktemp)"
trap 'rm -f "$errfile"' EXIT
if ! image_json="$(docker buildx imagetools inspect "$image" --format '{{json .Image}}' 2>"$errfile")"; then
    if grep -qiE 'not found|denied|unknown' "$errfile"; then
        echo rebuild
        exit 0
    fi
    cat "$errfile" >&2
    exit 1
fi

published="$(printf '%s' "$image_json" | jq -r --arg l "$label" '."linux/amd64".config.Labels[$l] // empty' 2>/dev/null)" || published=""
version="$(printf '%s' "$image_json" | jq -r '."linux/amd64".config.Labels["org.opencontainers.image.version"] // empty' 2>/dev/null)" || version=""

# A stale version rebuilds even when the base digest matches.
if [ "$published" = "$base_digest" ] && [ "$version" = "${release#v}" ]; then
    echo current
else
    echo rebuild
fi
