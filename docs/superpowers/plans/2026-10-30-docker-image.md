# v5 Docker Image Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every passing `develop` build publishes a tested, multi-arch, hotio-compatible `ghcr.io/whisparr/whisparr:v5-develop`, and new hotio base releases rebuild it automatically without an app release.

**Architecture:** `distribution/docker/` holds hotio's whisparr layer, adapted to take the package tarball from the build context, plus a test script runnable locally and in CI. A reusable workflow (`image.yml`) resolves the base digest, builds and tests each architecture natively, and publishes a manifest. `deploy.yml` calls it after the release job; an hourly `base-update.yml` calls it when the base digest has moved.

**Tech Stack:** Docker Buildx, GitHub Actions, GHCR, s6-overlay (from `ghcr.io/hotio/base:alpinevpn`), bash.

**Spec:** `docs/superpowers/specs/2026-10-30-docker-image-design.md`

## Global Constraints

- Image name `ghcr.io/whisparr/whisparr`; channel `v5-develop`. No `:v5`, `:develop`, `:release` or `:nightly` tags in this work.
- Platforms linux/amd64 and linux/arm64 only; both built and tested natively (`ubuntu-latest`, `ubuntu-24.04-arm`).
- Base `ghcr.io/hotio/base:alpinevpn`, no pin in the repo. Every build resolves the index digest once, builds `FROM ghcr.io/hotio/base@<digest>`, and records it in `org.opencontainers.image.base.digest`.
- Runtime matches hotio: `PUID`/`PGID`/`UMASK`/`TZ`, `--data=/config`, port 6969, `WEBUI_PORTS=6969/tcp`.
- `package_info` is exactly: `PackageVersion=<version>`, `PackageAuthor=[Whisparr](https://github.com/Whisparr)`, `UpdateMethod=Docker`, `Branch=develop`. `Whisparr.Update` is removed from the image.
- `org.opencontainers.image.source=https://github.com/Whisparr/whisparr-next`.
- A failed test pushes nothing. Pull requests never push. The moving tag is published last.
- Base updates make no repo commit, cut no release, and never move the per-commit tag.
- Edits via Edit/Write tools only: no sed, heredocs or redirection to edit files; one plain command per Bash call. No AI attribution. plz12345 handle, no infrastructure details. Never push; never touch whatever is on port 6969.
- Attribution: files copied from hotio/whisparr (GPL-3.0) carry a comment naming the source.

## Review Focus

- A default `/config` (`BindAddress` unset, so `*`) must keep answering past the first few requests; the dev build has hung that way before. Test: `test_keeps_answering`, Task 1.
- Booting on a legacy `/config` must not change the legacy files. Test: `test_legacy_files_unchanged`, Task 1.
- A legacy `/config` with files owned by another user, started with `PUID=1234`, must still boot to the halt screen and write as 1234. Test: `test_legacy_with_puid`, Task 1.
- The very first hourly check, with no image and/or no release yet, must not fail the run. Tests: `no_release`, `no_image`, Task 2.
- One architecture failing must leave every tag where it was. Check: `publish` needs the whole `build` matrix, Task 4.

---

### Task 1: Image definition and test script

**Files:**
- Create: `distribution/docker/Dockerfile`, `distribution/docker/test-image.sh`, `distribution/docker/root/etc/s6-overlay/...` (copied)
- Modify: `.gitignore` (add `distribution/docker/app.tar.gz`)

**Interfaces:**
- Produces:
  - Build args `BASE_DIGEST`, `VERSION`, `BRANCH`, `REVISION`; the context is `distribution/docker` with the package at `distribution/docker/app.tar.gz`.
  - `distribution/docker/test-image.sh <image-ref>`: exit 0 only if every test passes.

- [ ] **Step 1: Write `test-image.sh`** (`set -euo pipefail`; each test is a function printing PASS/FAIL; containers publish `127.0.0.1::6969` so port 6969 on the host is never used; always clean up its containers and temp dirs). Tests:
  - `test_boots`: empty `/config`, `/ping` returns 200 within 120s; `curl -L /system/status` returns 200.
  - `test_keeps_answering`: 30 sequential `/ping` requests all return 200.
  - `test_puid`: empty `/config`, `PUID=1234 PGID=1234`; `config5.xml` and `whisparr5.db` are owned by `1234:1234` (`stat` inside the container).
  - `test_ffmpeg_health`: on the fresh boot, `GET /api/v5/health` (key from `config5.xml`) has no entry with `source == "FFmpegCheck"`.
  - `test_halt_screen`: legacy fixture; `GET /setup` returns 200 with no sign-in; `GET /api/setup/status` with `X-Api-Key` returns `installs[0].kind` equal to `V2` (case-insensitive); `/config/logs/whisparr.txt` has no `|Error|` or `|Fatal|` line.
  - `test_legacy_files_unchanged`: sha256 of `whisparr2.db` and `config.xml` are the same before boot and after `test_halt_screen`'s checks.
  - `test_legacy_with_puid`: the same fixture chowned to `1000:1000`, `PUID=1234 PGID=1234`; `/setup` returns 200, `config5.xml` is owned by `1234:1234`.
  - `test_package_info`: `$APP_DIR/package_info` contains `UpdateMethod=Docker`; `$APP_DIR/bin/Whisparr.Update` doesn't exist.
  - `test_restart`: `docker restart`, `/ping` returns 200 again. No log assertion here: a setup-mode restart logs two known NREs.

  Fixture: `sqlite3 whisparr2.db < src/NzbDrone.Core.Test/Files/LegacyV2/v2-fresh-221.sql` in a temp dir, plus a `config.xml` with `Port` 6969, a fixed `ApiKey`, `AuthenticationMethod=Forms`, `AuthenticationRequired=DisabledForLocalAddresses`, and no `BindAddress`.

- [ ] **Step 2: Verify the script fails usefully**

  Run: `distribution/docker/test-image.sh ghcr.io/hotio/base:alpinevpn`
  Expected: non-zero exit, with `test_boots` reported FAIL (no app in that image).

- [ ] **Step 3: Copy hotio's layer.** From `hotio/whisparr`, branch `v3-develop`, copy `root/` unchanged, including the empty dependency files.

  Write one `Dockerfile` modelled on `linux-amd64.Dockerfile`, with these differences only:
  - `FROM ghcr.io/hotio/base@${BASE_DIGEST}`.
  - Drop `IMAGE_STATS`.
  - Extract `app.tar.gz` from the context into `${APP_DIR}/bin` (`--strip-components=1`) instead of curl.
  - Use the `package_info` values from Global Constraints.
  - Add the four OCI labels: `source`, `version`, `revision`, `base.digest`, plus `base.name=ghcr.io/hotio/base:alpinevpn`.

- [ ] **Step 4: Build a local tarball and image** (arm64 Mac): publish `linux-musl-arm64` with the UI as `package.sh` lays it out (`Whisparr/` at the top of the tar) to `distribution/docker/app.tar.gz`. Resolve the digest with `docker buildx imagetools inspect ghcr.io/hotio/base:alpinevpn --format '{{json .Manifest.Digest}}'`, then build with `--load -t whisparr-local:test`.

- [ ] **Step 5: Run the tests**

  Run: `distribution/docker/test-image.sh whisparr-local:test`
  Expected: all PASS. If `test_ffmpeg_health` fails because the musl package has no ffmpeg, report it rather than weakening the test.

- [ ] **Step 6: Commit**: `New: Add the Docker image definition and its tests`

### Task 2: Base check script

**Files:**
- Create: `distribution/docker/check-base.sh`, `distribution/docker/check-base.test.sh`

**Interfaces:**
- Produces: `check-base.sh <image:moving-tag> <release-tag-or-empty>`. Prints one of `rebuild`, `current`, `no_release`; exits non-zero only on a registry failure reading the *base*. A missing published image counts as `rebuild`. It reads the `linux/amd64` config's `org.opencontainers.image.base.digest` label.

- [ ] **Step 1: Write `check-base.test.sh`**, with a stub `docker` on `PATH`. Cases and expected output:
  - `no_release`: empty release arg → `no_release`.
  - `no_image`: image inspect fails, release set → `rebuild`.
  - `same_digest`: label equals base digest → `current`.
  - `new_digest`: label differs → `rebuild`.
  - `missing_label`: image has no label → `rebuild`.
  - `base_unreachable`: base inspect fails → non-zero exit, nothing on stdout.

- [ ] **Step 2: Run it.** Expected: FAIL (script missing).
- [ ] **Step 3: Implement `check-base.sh`.**
- [ ] **Step 4: Run it.** Expected: 6 PASS.
- [ ] **Step 5: Commit**: `Chore: Add the check for a new Docker base image`

### Task 3: Make the workflows fit this repo

**Files:**
- Modify: `.github/workflows/deploy.yml`, `.github/workflows/upstream-watch.yml`, `.github/workflows/lock.yml`

- [ ] **Step 1: Fix `deploy.yml`.**
  - `release` `if:` becomes `github.ref_name == 'develop'`.
  - `prerelease:` becomes `${{ inputs.branch == 'develop' }}`.
  - The previous-release lookup uses `/releases/latest` when `inputs.branch != 'develop'`, tolerates finding nothing (no `exit 1`, `gh` failure tolerated), and outputs an empty tag.
  - When the tag is empty, `changelog.md` is the fixed text `First v5 release.`, not `git log` (the full inherited history would exceed the release body limit).

- [ ] **Step 2: Stop jobs that would misfire once Actions are on.** Add `if: github.repository == 'Whisparr/Whisparr-Eros'` to the jobs in `upstream-watch.yml` (it checks out `eros-develop`) and `lock.yml`. `codeql.yml` is already gated. `sonarqube.yml` and `upstream-check.yml` only trigger on `eros*` branches; leave them.

- [ ] **Step 3: Validate.** Run: `actionlint .github/workflows/deploy.yml .github/workflows/upstream-watch.yml .github/workflows/lock.yml` (`brew install actionlint` if missing). Expected: no new findings.

- [ ] **Step 4: Commit**: `Chore: Cut releases from develop and keep Eros-only workflows off this repo`

### Task 4: Reusable image workflow, called from deploy

**Files:**
- Create: `.github/workflows/image.yml`
- Modify: `.github/workflows/deploy.yml`

**Interfaces:**
- Consumes: Task 1's build args and `test-image.sh`.
- Produces: `workflow_call` inputs: `channel` (string), `version` (string), `branch` (string), `source` (`artifact` | `release`), `push` (boolean), `commit_tag` (boolean). `permissions: contents: read, packages: write`.

- [ ] **Step 1: Write `image.yml`** with three jobs:
  - `base`: outputs the index digest of `ghcr.io/hotio/base:alpinevpn`.
  - `build`: matrix `{arch: amd64, runner: ubuntu-latest, runtime: linux-musl-x64}`, `{arch: arm64, runner: ubuntu-24.04-arm, runtime: linux-musl-arm64}`. Steps:
    1. Get the tarball to `distribution/docker/app.tar.gz`: `source=artifact` downloads `release-<runtime>`; `source=release` runs `gh release download v<version> --pattern "*.<runtime>.tar.gz"`.
    2. Install `sqlite3`.
    3. Buildx `--load` with the args from `base`, using one named builder so the push build below hits the same cache.
    4. Run `test-image.sh`.
    5. If `push`: same build with `outputs: type=image,name=ghcr.io/whisparr/whisparr,push-by-digest=true,push=true`, and upload the digest as an artifact.
  - `publish`: `if: inputs.push`, `needs: build`, `concurrency: image-${{ inputs.channel }}` (no cancel). Creates the manifest with `docker buildx imagetools create` in this order: `<channel>-<short sha>` (only if `commit_tag`), `<channel>-<version>`, then `<channel>` last.

- [ ] **Step 2: Wire `deploy.yml`.** Add an `image` job calling it:
  - `needs: [package, release]`;
  - `if: ${{ !cancelled() && needs.package.result == 'success' && (needs.release.result == 'success' || needs.release.result == 'skipped') }}`;
  - `channel: v5-develop`, `source: artifact`, `commit_tag: true`;
  - `push: ${{ github.ref_name == 'develop' }}`, so a PR builds and tests but never pushes.

  Confirm `build_v5.yml`'s `deploy` call grants `packages: write` (add `permissions` if not).

- [ ] **Step 3: Validate.** Run: `actionlint .github/workflows/image.yml .github/workflows/deploy.yml`. Expected: no findings.

- [ ] **Step 4: Commit**: `New: Publish the Docker image from develop builds`

### Task 5: Hourly base update

**Files:**
- Create: `.github/workflows/base-update.yml`

**Interfaces:**
- Consumes: `check-base.sh` (Task 2); `image.yml` (Task 4).

- [ ] **Step 1: Write `base-update.yml`.** `on: schedule: cron '17 * * * *'` and `workflow_dispatch`. Job `check` (`if: github.repository == 'Whisparr/whisparr-next'`):
  - finds the newest non-draft prerelease tag (`gh api repos/$GITHUB_REPOSITORY/releases?per_page=100`, the same `map(select(.prerelease and (.draft | not)))` as `deploy.yml`);
  - runs `check-base.sh ghcr.io/whisparr/whisparr:v5-develop <tag>`;
  - outputs the result and the version.

  Job `rebuild`: `needs: check`, `if: needs.check.outputs.result == 'rebuild'`, calls `image.yml` with `channel: v5-develop`, `version` from the tag, `branch: develop`, `source: release`, `push: true`, `commit_tag: false`. Add a comment showing where the `v5` channel slots in later.

- [ ] **Step 2: Validate.** Run: `actionlint .github/workflows/base-update.yml`. Expected: no findings.

- [ ] **Step 3: Commit**: `New: Rebuild the Docker image when its base image updates`

### Task 6: Going-public audit (read-only)

**Files:** none changed. Report only.

- [ ] **Step 1: Scan the tree and all history** for infrastructure details and personal names: hostnames and private IPs, the owner's real name and home paths, tokens, `appsettings.Development.json`. Use `git grep` for the tree and `git log --all -p -G<pattern>` for history. Exclude inherited upstream Sonarr/Radarr content. Patterns from the memory note `no-infra-details-in-public-repos` come from the controller; never write the values into the report's repo-visible text.

- [ ] **Step 2: Report** each finding as commit, path and kind, to the controller. A history rewrite is plz12345's decision; do not do one.

---

## After the tasks (stop and hand to plz12345)

Nothing above needs Actions on. Pushing and going public are plz12345's calls, in this order:

1. Once Actions are on, open a same-repo PR into `develop` first: it runs every test job and the image build and test on both architectures with no push, release, tag or package.
2. Review the audit; push `develop`.
3. Make the repo public; enable Actions.
4. Check org settings (public packages allowed; Actions may create packages) and that `GPG_PRIVATE_KEY` / `GPG_PASSPHRASE` reach the repo. If they don't, create a new key at org level.
5. Watch the first `develop` run: release `v5.0.0-develop.N`, then all three image tags.
6. Set the `whisparr` package to public (one-way).
7. Run `base-update.yml` by hand: expect `current`.
8. Acceptance: stop the Eros container, zip AppData, switch to `ghcr.io/whisparr/whisparr:v5-develop`, start. Expect a clean pull and boot, no failures in the logs, and the migration halt screen.
