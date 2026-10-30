#!/usr/bin/env bash
# Decide whether the published image needs rebuilding on a new base image.
#
# Usage: check-base.sh <image:moving-tag> <release-tag-or-empty>
# Prints one of: rebuild, current, no_release.
# Exits non-zero only when the base image cannot be read from the registry.
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

# A missing published image, or one without the label, means rebuild.
if ! image_json="$(docker buildx imagetools inspect "$image" --format '{{json .Image}}' 2>/dev/null)"; then
    echo rebuild
    exit 0
fi

published="$(printf '%s' "$image_json" | jq -r --arg l "$label" '."linux/amd64".config.Labels[$l] // empty' 2>/dev/null)" || published=""

if [ "$published" = "$base_digest" ]; then
    echo current
else
    echo rebuild
fi
