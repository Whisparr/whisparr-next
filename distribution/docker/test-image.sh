#!/usr/bin/env bash
# Smoke tests for the Whisparr v5 Docker image.
#
# Usage: test-image.sh <image-ref>
#
# Exits 0 only if every test passes. Containers publish 127.0.0.1::6969 (a random
# host port), so port 6969 on the host is never used. Config lives in named docker
# volumes (not bind mounts), so file ownership is real on every host. Containers,
# volumes and temp dirs are always cleaned up. Needs docker, curl, jq and sqlite3
# on PATH.
#
# BOOT_TIMEOUT (seconds, default 120) bounds how long a container may take to
# answer its readiness URL.

set -euo pipefail

if [[ $# -ne 1 ]]; then
    echo "usage: $0 <image-ref>" >&2
    exit 2
fi

IMAGE="$1"
BOOT_TIMEOUT="${BOOT_TIMEOUT:-120}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
FIXTURE_SQL="${REPO_ROOT}/src/NzbDrone.Core.Test/Files/LegacyV2/v2-fresh-221.sql"
LEGACY_API_KEY="0123456789abcdef0123456789abcdef"

for tool in docker curl jq sqlite3; do
    if ! command -v "${tool}" > /dev/null 2>&1; then
        echo "error: required tool '${tool}' not found on PATH; install it and retry" >&2
        exit 2
    fi
done

if [[ ! -f "${FIXTURE_SQL}" ]]; then
    echo "error: legacy fixture not found at ${FIXTURE_SQL}" >&2
    exit 2
fi

WORK_DIR="$(mktemp -d)"
CONTAINERS=()
VOLUMES=()
FRESH_NAME=""
FRESH_PORT=""
LEGACY_NAME=""
LEGACY_PORT=""
LEGACY_VOLUME=""
LEGACY_HASHES=""
HALT_CHECKED=0
VOL=""
PORT=""

cleanup() {
    local c v
    for c in "${CONTAINERS[@]:-}"; do
        [[ -n "${c}" ]] && { docker rm -f "${c}" > /dev/null 2>&1 || true; }
    done
    for v in "${VOLUMES[@]:-}"; do
        [[ -n "${v}" ]] && { docker volume rm -f "${v}" > /dev/null 2>&1 || true; }
    done
    rm -rf "${WORK_DIR}" 2> /dev/null || true
}
trap cleanup EXIT

log() { echo "    $*"; }

# Creates a volume and sets VOL. Never call it in $(...): the subshell would lose
# the VOLUMES bookkeeping that cleanup relies on.
new_volume() {
    VOL="whisparr-test-vol-$$-${#VOLUMES[@]}"
    docker volume create "${VOL}" > /dev/null
    VOLUMES+=("${VOL}")
}

# Run a shell snippet as root inside a throwaway container with the volume at /config.
in_volume() {
    local volume="$1"
    shift
    docker run --rm --entrypoint sh -v "${volume}:/config" "$@" "${IMAGE}" -c "${SNIPPET}"
}

# start_container <name> <volume> [docker run args...]; sets PORT to the host port.
# Never call it in $(...), for the same reason as new_volume.
start_container() {
    local name="$1" volume="$2"
    shift 2
    CONTAINERS+=("${name}")
    docker run -d --name "${name}" -p 127.0.0.1::6969 -v "${volume}:/config" "$@" "${IMAGE}" > /dev/null
    local mapping="" i
    for i in $(seq 1 30); do
        mapping="$(docker port "${name}" 6969/tcp 2> /dev/null | head -n1 || true)"
        [[ -n "${mapping}" ]] && break
        sleep 1
    done
    PORT="${mapping##*:}"
}

# http_code <curl args...>; prints the status code, 000 on connection failure.
http_code() {
    curl -s -o /dev/null -w '%{http_code}' --max-time 10 "$@" || true
}

# http_get <body-file> <curl args...>; saves the body, prints the status code.
http_get() {
    local body="$1"
    shift
    curl -s -o "${body}" -w '%{http_code}' --max-time 10 "$@" || true
}

# wait_ready <name> <port> <path>
wait_ready() {
    local name="$1" port="$2" path="$3" deadline=$((SECONDS + BOOT_TIMEOUT))
    while ((SECONDS < deadline)); do
        if [[ "$(docker inspect -f '{{.State.Running}}' "${name}" 2> /dev/null)" != "true" ]]; then
            log "container ${name} is not running"
            return 1
        fi
        if [[ "$(http_code "http://127.0.0.1:${port}${path}")" == "200" ]]; then
            return 0
        fi
        sleep 2
    done
    log "no 200 from ${path} within ${BOOT_TIMEOUT}s"
    return 1
}

wait_ping() { wait_ready "$1" "$2" /ping; }

# A legacy install boots into setup mode, where /ping is not served; /setup is.
wait_setup() { wait_ready "$1" "$2" /setup; }

api_key_from() {
    docker exec "$1" cat "$2" | sed -n 's:.*<ApiKey>\(.*\)</ApiKey>.*:\1:p' | head -n1
}

# write_legacy_fixture <volume>: a v2 install owned by 1000:1000.
write_legacy_fixture() {
    local volume="$1" src="${WORK_DIR}/legacy-src"
    rm -rf "${src}"
    mkdir -p "${src}"
    sqlite3 "${src}/whisparr2.db" < "${FIXTURE_SQL}"
    cat > "${src}/config.xml" << EOF
<Config>
  <Port>6969</Port>
  <ApiKey>${LEGACY_API_KEY}</ApiKey>
  <AuthenticationMethod>Forms</AuthenticationMethod>
  <AuthenticationRequired>DisabledForLocalAddresses</AuthenticationRequired>
</Config>
EOF
    SNIPPET='cp /src/whisparr2.db /src/config.xml /config/ && chown -R 1000:1000 /config && chmod 644 /config/whisparr2.db /config/config.xml' \
        in_volume "${volume}" -v "${src}:/src:ro"
}

hash_legacy() {
    SNIPPET='cd /config && sha256sum whisparr2.db config.xml' in_volume "$1"
}

# --- tests -----------------------------------------------------------------

test_boots() {
    new_volume
    FRESH_NAME="whisparr-test-fresh-$$"
    start_container "${FRESH_NAME}" "${VOL}"
    FRESH_PORT="${PORT}"
    wait_ping "${FRESH_NAME}" "${FRESH_PORT}" || return 1
    local code
    code="$(http_code -L "http://127.0.0.1:${FRESH_PORT}/system/status")"
    [[ "${code}" == "200" ]] || { log "/system/status returned ${code}"; return 1; }
}

test_keeps_answering() {
    [[ -n "${FRESH_PORT}" ]] || { log "no fresh container (test_boots failed)"; return 1; }
    local i code
    for i in $(seq 1 30); do
        code="$(http_code "http://127.0.0.1:${FRESH_PORT}/ping")"
        [[ "${code}" == "200" ]] || { log "request ${i} returned ${code}"; return 1; }
    done
}

test_puid() {
    local name
    new_volume
    name="whisparr-test-puid-$$"
    start_container "${name}" "${VOL}" -e PUID=1234 -e PGID=1234
    wait_ping "${name}" "${PORT}" || return 1
    local f owner
    for f in config5.xml whisparr5.db; do
        owner="$(docker exec "${name}" stat -c '%u:%g' "/config/${f}" 2>&1)" || { log "${f}: ${owner}"; return 1; }
        [[ "${owner}" == "1234:1234" ]] || { log "${f} owned by ${owner}"; return 1; }
    done
}

test_ffmpeg_health() {
    [[ -n "${FRESH_PORT}" ]] || { log "no fresh container (test_boots failed)"; return 1; }
    local key body code
    key="$(api_key_from "${FRESH_NAME}" /config/config5.xml)"
    [[ -n "${key}" ]] || { log "no ApiKey in config5.xml"; return 1; }
    body="${WORK_DIR}/health.json"
    code="$(http_get "${body}" -H "X-Api-Key: ${key}" "http://127.0.0.1:${FRESH_PORT}/api/v5/health")"
    [[ "${code}" == "200" ]] || { log "/api/v5/health returned ${code}"; return 1; }
    local ffmpeg
    ffmpeg="$(jq -c '[.[] | select(.source == "FFmpegCheck")]' "${body}")" || { log "health body is not a JSON array"; return 1; }
    [[ "${ffmpeg}" == "[]" ]] || { log "health reports FFmpegCheck: ${ffmpeg}"; return 1; }
}

test_halt_screen() {
    new_volume
    LEGACY_VOLUME="${VOL}"
    write_legacy_fixture "${LEGACY_VOLUME}"
    LEGACY_HASHES="$(hash_legacy "${LEGACY_VOLUME}")"
    LEGACY_NAME="whisparr-test-legacy-$$"
    start_container "${LEGACY_NAME}" "${LEGACY_VOLUME}"
    LEGACY_PORT="${PORT}"
    wait_setup "${LEGACY_NAME}" "${LEGACY_PORT}" || return 1

    local body="${WORK_DIR}/setup-status.json" code kind
    code="$(http_get "${body}" -H "X-Api-Key: ${LEGACY_API_KEY}" "http://127.0.0.1:${LEGACY_PORT}/api/setup/status")"
    [[ "${code}" == "200" ]] || { log "/api/setup/status returned ${code}"; return 1; }
    kind="$(jq -r '.installs[0].kind' "${body}" | tr '[:upper:]' '[:lower:]')"
    [[ "${kind}" == "v2" ]] || { log "installs[0].kind was '${kind}'"; return 1; }

    local errors
    errors="$(docker exec "${LEGACY_NAME}" grep -E '\|(Error|Fatal)\|' /config/logs/whisparr.txt 2>&1 || true)"
    [[ -z "${errors}" ]] || { log "log has error lines:"; echo "${errors}" | head -n 5 | sed 's/^/        /'; return 1; }
    HALT_CHECKED=1
}

test_legacy_files_unchanged() {
    [[ "${HALT_CHECKED}" == "1" && -n "${LEGACY_VOLUME}" && -n "${LEGACY_HASHES}" ]] || {
        log "no checked legacy container (test_halt_screen failed)"
        return 1
    }
    local after
    after="$(hash_legacy "${LEGACY_VOLUME}")"
    [[ "${after}" == "${LEGACY_HASHES}" ]] || { log "legacy files changed:"; diff <(echo "${LEGACY_HASHES}") <(echo "${after}") || true; return 1; }
}

test_legacy_with_puid() {
    local name
    new_volume
    write_legacy_fixture "${VOL}"
    name="whisparr-test-legacy-puid-$$"
    start_container "${name}" "${VOL}" -e PUID=1234 -e PGID=1234
    wait_setup "${name}" "${PORT}" || return 1
    local owner
    owner="$(docker exec "${name}" stat -c '%u:%g' /config/config5.xml 2>&1)" || { log "config5.xml: ${owner}"; return 1; }
    [[ "${owner}" == "1234:1234" ]] || { log "config5.xml owned by ${owner}"; return 1; }
}

test_package_info() {
    [[ -n "${FRESH_NAME}" ]] || { log "no fresh container (test_boots failed)"; return 1; }
    local app_dir
    app_dir="$(docker exec "${FRESH_NAME}" printenv APP_DIR 2>&1)" || { log "APP_DIR: ${app_dir}"; return 1; }
    docker exec "${FRESH_NAME}" grep -q '^UpdateMethod=Docker$' "${app_dir}/package_info" || { log "package_info lacks UpdateMethod=Docker"; return 1; }
    if docker exec "${FRESH_NAME}" test -e "${app_dir}/bin/Whisparr.Update"; then
        log "${app_dir}/bin/Whisparr.Update exists"
        return 1
    fi
}

# No log assertion: a setup-mode restart logs two known NREs.
test_restart() {
    [[ -n "${FRESH_NAME}" ]] || { log "no fresh container (test_boots failed)"; return 1; }
    docker restart "${FRESH_NAME}" > /dev/null
    local mapping
    mapping="$(docker port "${FRESH_NAME}" 6969/tcp | head -n1)"
    wait_ping "${FRESH_NAME}" "${mapping##*:}"
}

# --- runner ----------------------------------------------------------------

FAILED=0
run_test() {
    local t="$1"
    echo "RUN  ${t}"
    if "${t}"; then
        echo "PASS ${t}"
    else
        echo "FAIL ${t}"
        FAILED=$((FAILED + 1))
    fi
}

echo "Testing image: ${IMAGE}"
run_test test_boots
run_test test_keeps_answering
run_test test_puid
run_test test_ffmpeg_health
run_test test_halt_screen
run_test test_legacy_files_unchanged
run_test test_legacy_with_puid
run_test test_package_info
run_test test_restart

if ((FAILED > 0)); then
    echo "${FAILED} test(s) failed"
    exit 1
fi
echo "All tests passed"
