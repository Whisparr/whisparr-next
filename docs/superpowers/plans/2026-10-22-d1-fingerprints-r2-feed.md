# D1: Publish Whisparr.Fingerprints to the R2 feed — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A `v*` tag on `Whisparr/fingerprints` publishes the package to a static NuGet v3 feed at `https://nuget.whisparr.com/index.json`, restorable with no credentials.

**Architecture:** The workflow's existing `pack` job output feeds a new tag-only `publish` job that pushes to the R2 bucket with Sleet (a static-feed tool for S3-compatible storage) and then proves a clean restore from the public URL. The nuget.org step goes.

**Tech Stack:** GitHub Actions, .NET 10 SDK, Sleet (dotnet tool), Cloudflare R2 (S3 API).

**Spec:** `docs/superpowers/specs/2026-10-22-fingerprints-design.md` (Part 1)

## Global Constraints

- Repo `~/Git/Fingerprints`, branch `ffmpeg-build` (open PR #1 → `main`). Commit there; push only when plz12345 confirms. No new PR.
- R2 values come only from secrets: `R2_ACCOUNT_ID`, `R2_ACCESS_KEY_ID`, `R2_SECRET_ACCESS_KEY`, `R2_BUCKET` (plz12345 adds `R2_BUCKET` = the bucket name before Task 2). The bucket name never appears in the repo.
- The only repo-visible feed detail is the public URL `https://nuget.whisparr.com/` (service index `https://nuget.whisparr.com/index.json`).
- R2 S3 endpoint: `https://${R2_ACCOUNT_ID}.r2.cloudflarestorage.com`, region `auto`.
- Publish only on `refs/tags/v*`; pushes to `main` and PRs build, test and pack only.
- Pin every tool version exactly (Sleet included). No AI attribution. File edits with Edit/Write only.

## Review Focus

1. Re-running a tag (or pushing a second version) must not fail or corrupt the feed — `sleet init` on an existing feed and pushing an existing version both need defined behaviour.
2. A secret printed to the log (e.g. `set -x`, echoing a sleet config) — nothing derived from `R2_*` may be echoed.
3. R2 rejecting an S3 feature Sleet uses (ACL headers, chunked uploads) — the job must fail loudly, not report success with an empty feed.
4. A restore that "passes" from a local cache instead of the public feed — the verify step must use a clean `NUGET_PACKAGES` folder and only the public source.
5. The feed's `baseURI` pointing at the S3 endpoint instead of `https://nuget.whisparr.com/` — clients would get private-endpoint URLs.

---

### Task 1: Tag-only publish job with Sleet, plus restore check

**Files:**
- Modify: `.github/workflows/build.yml:216-224` (drop "Publish to nuget.org"; add `publish` job after `pack`)
- Modify: `README.md` (install section: the feed URL and a `nuget.config` snippet with package source mapping for `Whisparr.*`)

**Interfaces:**
- Consumes: the `nupkg` artifact uploaded by `pack`; secrets above.
- Produces: feed at `https://nuget.whisparr.com/index.json` containing `Whisparr.Fingerprints <tag version>` (D3 consumes it).

- [ ] **Step 1: Add the `publish` job**
  - `needs: [pack]`, `if: startsWith(github.ref, 'refs/tags/v')`, `runs-on: ubuntu-24.04`, `permissions: contents: read`.
  - Steps: checkout; setup-dotnet from `global.json`; download artifact `nupkg` to `artifacts/nupkg`; `dotnet tool install --global sleet --version <latest stable, pinned>`.
  - Configure Sleet without writing secrets to disk in the workspace log: either Sleet's `SLEET_FEED_*` environment variables (type `s3`, bucket, region `auto`, service URL, access key, secret, `baseURI` `https://nuget.whisparr.com/`) or a `sleet.json` written to `$RUNNER_TEMP` from env vars. Use whichever Sleet's docs support for S3-compatible endpoints; say which in the report.
  - `sleet init` must be safe when the feed exists (check and skip, or rely on documented idempotency — verify, don't assume).
  - `sleet push artifacts/nupkg --skip-existing`, then `sleet validate`.
- [ ] **Step 2: Add the restore check (same job, after push)**
  - Create a throwaway console project in `$RUNNER_TEMP`, a `nuget.config` with `<clear/>` and only `https://nuget.whisparr.com/index.json`, a fresh `NUGET_PACKAGES` dir, then `dotnet add package Whisparr.Fingerprints --version <tag version>` and `dotnet restore`. Fail the job if restore fails.
  - Also `curl -fsS https://nuget.whisparr.com/index.json` and fail if it isn't JSON with `resources`.
- [ ] **Step 3: Remove the nuget.org step** (`build.yml:222-224`).
- [ ] **Step 4: README install section** with the feed URL and this mapping (exact):

```xml
<packageSources>
  <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  <add key="whisparr" value="https://nuget.whisparr.com/index.json" />
</packageSources>
<packageSourceMapping>
  <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  <packageSource key="whisparr"><package pattern="Whisparr.*" /></packageSource>
</packageSourceMapping>
```

- [ ] **Step 5: Lint the workflow** — `actionlint .github/workflows/build.yml` if installed (else `yq` parse). Expected: no errors.
- [ ] **Step 6: Commit** — `git commit -m "New: Publish tagged releases to the Whisparr NuGet feed on R2"`. Do not push.

### Task 2: Live publish (controller + plz12345)

Outward-facing: needs plz12345's go-ahead for the push and the tag.

- [ ] **Step 1:** Confirm `R2_BUCKET` exists: `gh secret list -R Whisparr/fingerprints` shows it.
- [ ] **Step 2:** Push `ffmpeg-build`; PR #1 checks green.
- [ ] **Step 3:** Tag `v0.1.0-alpha.1` on the branch head and push the tag. Watch the run; `publish` must be green.
- [ ] **Step 4:** Locally: `curl -fsS https://nuget.whisparr.com/index.json` returns the service index; a scratch project restores `Whisparr.Fingerprints 0.1.0-alpha.1` from that source only, and the restored package contains `runtimes/*/native/ffmpeg*` for every RID the `pack` job staged.
- [ ] **Step 5 (only if Sleet fails against R2):** replace Sleet with a workflow-generated static v3 feed uploaded via `aws s3 cp --endpoint-url` (service index, flat container `index.json` + `.nupkg` + `.nuspec`, registration index); same restore check. Record the switch.
