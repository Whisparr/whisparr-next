#!/usr/bin/env bash
# Tests for check-base.sh, using a stub `docker` on PATH.
set -u

here="$(cd "$(dirname "$0")" && pwd)"
script="$here/check-base.sh"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

mkdir -p "$work/bin"
cat > "$work/bin/docker" <<'STUB'
#!/usr/bin/env bash
# Stub: `docker buildx imagetools inspect <ref> --format <fmt>`
ref="$4"
fmt="$6"
if [ "$ref" = "ghcr.io/hotio/base:alpinevpn" ]; then
    [ "${STUB_BASE_FAIL:-0}" = "1" ] && exit 1
    case "$fmt" in
        *Manifest.Digest*) echo "\"${STUB_BASE_DIGEST}\"" ;;
        *) exit 2 ;;
    esac
    exit 0
fi
if [ "${STUB_IMAGE_FAIL:-0}" = "1" ]; then
    echo "${STUB_IMAGE_ERR:-}" >&2
    exit 1
fi
case "$fmt" in
    *.Image*) echo "${STUB_IMAGE_JSON}" ;;
    *) exit 2 ;;
esac
STUB
chmod +x "$work/bin/docker"

pass=0
fail=0

# run_case <name> <expected stdout> <expected exit: zero|nonzero> <release arg> [VAR=value ...]
run_case() {
    local name="$1" expected="$2" exitkind="$3" release="$4"
    shift 4
    local out rc
    out="$(env PATH="$work/bin:$PATH" STUB_BASE_DIGEST="sha256:aaa" "$@" "$script" ghcr.io/example/app:latest "$release" 2>/dev/null)"
    rc=$?
    local ok=1
    [ "$out" = "$expected" ] || ok=0
    if [ "$exitkind" = "zero" ]; then [ "$rc" -eq 0 ] || ok=0; else [ "$rc" -ne 0 ] || ok=0; fi
    if [ "$ok" -eq 1 ]; then
        echo "PASS $name"
        pass=$((pass + 1))
    else
        echo "FAIL $name (stdout='$out' rc=$rc)"
        fail=$((fail + 1))
    fi
}

# label_json <base digest> [image version]
label_json() {
    echo "{\"linux/amd64\":{\"config\":{\"Labels\":{\"org.opencontainers.image.base.digest\":\"$1\",\"org.opencontainers.image.version\":\"${2:-1.0.0}\"}}},\"linux/arm64\":{\"config\":{\"Labels\":{}}}}"
}

run_case no_release no_release zero ""
run_case no_image rebuild zero v1.0.0 STUB_IMAGE_FAIL=1 "STUB_IMAGE_ERR=ERROR: ghcr.io/example/app:latest: not found"
run_case image_denied rebuild zero v1.0.0 STUB_IMAGE_FAIL=1 "STUB_IMAGE_ERR=ERROR: denied: permission_denied: read_package"
run_case image_error "" nonzero v1.0.0 STUB_IMAGE_FAIL=1 "STUB_IMAGE_ERR=ERROR: unexpected status 500 Internal Server Error"
run_case same_digest current zero v1.0.0 "STUB_IMAGE_JSON=$(label_json sha256:aaa 1.0.0)"
run_case version_same current zero v1.2.0 "STUB_IMAGE_JSON=$(label_json sha256:aaa 1.2.0)"
run_case version_behind rebuild zero v1.2.0 "STUB_IMAGE_JSON=$(label_json sha256:aaa 1.1.0)"
run_case new_digest rebuild zero v1.0.0 "STUB_IMAGE_JSON=$(label_json sha256:bbb 1.0.0)"
run_case missing_label rebuild zero v1.0.0 'STUB_IMAGE_JSON={"linux/amd64":{"config":{"Labels":{}}}}'
run_case base_unreachable "" nonzero v1.0.0 STUB_BASE_FAIL=1 "STUB_IMAGE_JSON=$(label_json sha256:aaa)"

echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
