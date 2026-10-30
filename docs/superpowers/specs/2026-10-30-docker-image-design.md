# v5 Docker image — design

## Intent

Whisparr ships its own container image from this repo's CI, replacing hotio's image as the way
container users run Whisparr. hotio's image process is solid, reliable and widely adopted, so the
image is a **drop-in** for it: a user changes only the image reference in their compose file, and
everything else (volumes, environment, ports, file ownership) keeps working.

The image also enables the forced upgrade later. When v5 is ready, hotio copies our image into his
existing tags, so users on his tags receive v5 without editing anything. That handover is gated (see
"Out of scope") and is not part of this work.

## Goals

- Every push to `develop` that passes CI publishes `ghcr.io/whisparr/whisparr:v5-develop`
  (multi-arch: linux/amd64 + linux/arm64).
- The runtime behaviour matches hotio's image exactly:
  - `PUID` / `PGID` / `UMASK` / `TZ`;
  - `/config` as the AppData folder (`--data=/config`), with hotio's layout, so a swapped tag finds
    the legacy `whisparr2.db` / `whisparr3.db` and `config.xml`;
  - port 6969 (`WEBUI_PORTS=6969/tcp`);
  - the optional WireGuard VPN and the rest of his base image's features.
- The in-app updater is hidden (`package_info` with `UpdateMethod=Docker`), as in hotio's image.
- New hotio base releases reach every published channel automatically as a container-only OS
  update: the image is rebuilt around the same app build, tested, and pushed. No repo commit, no
  new app version, no release.
- Fix: v5 releases are actually created on `develop`.

## Non-goals (for now)

- The `:v5` tag (release branch). It comes later, the same way, from the release branch.
- Bare `:develop` / `:release` / `:nightly` aliases. They are added when v2 users are force-migrated,
  not now.
- Docker Hub or any registry other than ghcr.io.
- 32-bit ARM (hotio's image doesn't build it either).
- Asking hotio to retag. That is a later, separate, human step.

## Anti-goals

The work fails, even if an image is published, if:
- a user's existing compose file (hotio's env vars, `/config` volume, port) behaves differently on
  our image;
- files written into `/config` end up owned by the wrong user;
- a legacy `/config` boots into a fresh empty install instead of the migration halt screen;
- the in-app updater is shown and tries to update inside the container;
- a base-image change reaches users without passing the image tests.

## Decisions

From plz12345:
- Image name `ghcr.io/whisparr/whisparr`, deliberately the same name as v2's repo, as a
  user-facing UX choice. The tag scheme mirrors hotio's (`v3-develop` / `v3`): `v5-develop` now,
  `v5` later.
- Follow hotio's image process. No reason to deviate.
- The repo goes public so Actions are free. Actions are currently disabled and the repo is private;
  enabling them is the first step.
- No base pin. The image builds on hotio's moving `alpinevpn` tag. A new hotio base triggers an
  automatic, tested, container-only rebuild of every channel, the equivalent of hotio's own
  hourly re-pin with no human step. Base updates never cut an app release.
- Release signing uses the org-level `GPG_PRIVATE_KEY` / `GPG_PASSPHRASE`. If they aren't there,
  create a new key at org level.
- The org's container packages today are only the Skyhook containers, which stay private. The
  `whisparr` package is public. (Package visibility is per package, and public can't be undone.)

The builder's calls, made by me and open to correction:
- **Base:** build `FROM ghcr.io/hotio/base:alpinevpn`, the same base hotio's whisparr image uses,
  without his pin. Every image records the exact base digest it was built from in a label
  (e.g. `org.opencontainers.image.base.digest`). Copy his whisparr layer: the per-arch Dockerfile,
  the `root/etc/s6-overlay` start-up scripts and the `package_info` line. Both his repos are GPL-3.0, the same as ours; keep
  the attribution in the copied files.
- **Binary source:** the image uses the `linux-musl-x64` / `linux-musl-arm64` package tarballs this
  workflow already builds (the `release-<runtime>` artifacts), not a download from the GitHub
  release. That avoids depending on release timing or visibility.
- **Package link:** the image's `org.opencontainers.image.source` label points at the whisparr-next
  repo, so the workflow's own `GITHUB_TOKEN` can push. The name users pull is unchanged.
- **package_info:** `PackageVersion=<version>`, `PackageAuthor=[Whisparr](https://github.com/Whisparr)`,
  `UpdateMethod=Docker`, `Branch=develop`.
- **Tags per build:**
  - `v5-develop` (moving);
  - `v5-develop-<version>` (moves only when a base update rebuilds the same version);
  - `v5-develop-<short sha>` (never moves; the tag to pin to).

  These mirror hotio's branch / version / sha tags.
- **Base updates:** a scheduled workflow runs hourly, like hotio's.
  - It compares the current `ghcr.io/hotio/base:alpinevpn` digest with the base-digest label on
    each channel's moving tag (`v5-develop` now, `v5` later).
  - For each channel whose base is out of date, it rebuilds both architectures around that
    channel's current release tarball (the newest develop prerelease, or the newest stable
    release), runs the image tests, and pushes the channel tags.
  - It makes no repo commit, and the app version is unchanged.
  - A failing test pushes nothing and leaves the run red.
- **Reproducibility trade-off, accepted:** rebuilding an old commit later picks up the base current
  at that time; the label still records what each shipped image used.
- **Vulnerability scan:** optional. If added, it reports and does not block.
- **Where the image job runs:** in `deploy.yml`, after `package`, using the
  `release-linux-musl-x64` / `release-linux-musl-arm64` artifacts. The Dockerfile takes the exact
  tarball name (`Whisparr.<branch>.<version>.<runtime>.tar.gz`) as a build arg.
  - **On pull requests:** the image is built and tested but never pushed.
  - **On a `develop` push:** images are pushed only after the `release` job has succeeded, so every
    `v5-develop-<version>` matches a real git tag. The moving `v5-develop` tag is pushed last.
- **arm64:** built and tested natively on GitHub's arm64 runners (`ubuntu-24.04-arm`, free for
  public repos), not under QEMU.
- **Base digest** comes from the registry (`docker buildx imagetools inspect`), not from
  hotio.dev, so there is no third-party dependency.
- **`IMAGE_STATS`:** dropped. It is hotio's own stats value, not ours.
- **Release gating:** `release` runs on `develop` only for now. The future v5 release branch is
  added together with the `:v5` tag. `prerelease` is true for `develop`. The previous-release
  lookup tolerates finding none, so the first v5 release succeeds (the changelog then covers the
  whole history, or a fixed note).
- **v5 runtime compatibility, checked:**
  - `DeploymentInfoProvider` still reads `package_info`, with the same keys.
  - `StartupContext` still takes `--nobrowser` / `--data`.
  - `PackageVersion` is the CI version string (e.g. `5.0.0-develop.123`).
  - The musl packages need hotio's `libintl sqlite-libs icu-libs`; image test 1 catches anything
    missing.

## Image tests

These run in CI on every build, including base-update rebuilds. All must pass before an image is pushed
under a moving tag.

1. The container boots on amd64, and on arm64 under emulation.
2. `/ping` and `/system/status` answer on 6969.
3. With `PUID=1234` and `PGID=1234`, files the app creates in `/config` are owned by 1234:1234.
4. A legacy `/config` boots into the migration halt screen:
   - `GET /setup` returns 200 from a local address;
   - `GET /api/setup/status` (with the fixture's API key) lists one install whose `kind` is the
     legacy kind;
   - the container log has no `|Error|` or `|Fatal|` lines.
5. `package_info` contains `UpdateMethod=Docker`.

Legacy fixture: built at test time with `sqlite3` from the existing v2 dump
`src/NzbDrone.Core.Test/Files/LegacyV2/v2-fresh-221.sql` into `whisparr2.db`, plus a minimal
`config.xml` (port 6969, a fixed API key, `AuthenticationRequired=DisabledForLocalAddresses`).
Detection only needs the DB's tables (`Series` present, `Movies` absent → v2), so this proves the
halt-screen path; the Eros path is the same code. No user data is used.

## Manual setup steps (plz12345, in order)

1. Make the repo public, then enable Actions.
2. Org Settings → Packages: allow public packages; check that Actions may create packages.
3. Check that the org-level `GPG_PRIVATE_KEY` / `GPG_PASSPHRASE` reach this repo. If they don't,
   create a new key at org level.
4. After the first successful `develop` push, open the new `whisparr` package and set it to
   public. This is one-way.

## Acceptance (manual, by plz12345)

On the real Eros install:
1. Stop the container.
2. Zip the whole AppData folder.
3. Change the image reference to `ghcr.io/whisparr/whisparr:v5-develop` and start the container.
4. Expect: the new image pulls, the container comes up, the logs show no failures, and the UI lands
   on the migration halt screen.

## Also in this work

- `deploy.yml`'s `release` job only runs for `eros-develop` / `eros`, branch names left over from
  Eros. On this repo's `develop` it never runs, so no v5 release is created. Gate it on `develop`
  (and the future v5 release branch) instead. Check the rest of the workflows for the same
  leftover names.

## Out of scope / later

- **The forced upgrade.** When v5 is ready for Eros users, plz12345 asks hotio to copy our `v5` /
  `v5-develop` images into his `v3` / `v3-develop` tags. A registry can't redirect a tag across
  repositories, but a copy (`docker buildx imagetools create`, `crane copy`) is one command per tag.
  v2 (`v2` / `v2-develop`) is the same step, later, at which point the bare `:release` and
  `:develop` tags are added. Never assume hotio's tags already point at our image.

## Left to the builder

The workflow structure (shared image build/test steps between `deploy.yml` and the base-update
workflow, for example as a reusable workflow or a composite action), the exact smoke-test harness,
and caching.
