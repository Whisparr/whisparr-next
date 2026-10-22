# Workstream D: fingerprints (design)

Roadmap: `docs/v5-roadmap.md`, section D. This document supersedes the roadmap's D section
where they differ.

## Intent

Identify a scene file by its content when its name doesn't parse, and keep a file linked
when another tool (often StashApp) renames or moves it. StashDB and ThePornDB already hold
community-submitted oshash and phash values for most scenes; Whisparr computes the same
hashes for its files and looks them up.

## Goals

1. Publish the `Whisparr.Fingerprints` package (Part A, `~/Git/Fingerprints`, repo
   `Whisparr/fingerprints`) to a NuGet feed hosted on Cloudflare R2, so whisparr-next can
   restore it with no credentials.
2. Skyhook (`~/Git/Whisparr-Eros-Metadata`, branch `v5`) stores the scene hashes it
   already receives, and answers hash lookups locally first, upstream on a miss.
3. whisparr-next hashes files and uses the hashes as a fallback after the parser, and to
   re-link renamed or moved files.

## Non-goals

- Whisparr never submits hashes to StashDB or TPDB. It is a reader only (plz12345: the
  hash table de-dupes, so our writes add little).
- No Skyhook replica of TPDB hashes; TPDB is only asked about a specific file, on a miss.
- No Hamming-distance search inside Skyhook's local table (see Part 2).
- Flat studio folders (workstream F) are not part of D, though D's re-link logic is what F
  builds on.

## Anti-goals (failure even if it works)

- A hashing problem (no ffmpeg, unreadable file, timeout) rejects, aborts, or delays an
  import.
- A weak match links a file silently. Wrong identification must never be applied without
  the user seeing it.
- phash is recomputed on every scan. It runs at most once per file.
- A library scan turns into a burst of upstream calls to TPDB or StashDB.
- Building whisparr-next needs secrets.

## Facts established in recon (2026-10-22)

- **Package:** id `Whisparr.Fingerprints` (the roadmap's `Whisparr.VideoHashes` is
  outdated), `net10.0`. API: `OsHash.Compute(path)` → 16 zero-padded hex digits;
  `PerceptualHash.ComputeAsync(path, durationSeconds, options?, ct)` → lowercase hex,
  unpadded; `FFmpegLocator.IsBundledAvailable`. Duration must be the container
  `format.duration` rounded to 0.01 s. The package carries a static ffmpeg per runtime:
  linux-x64, linux-arm, linux-arm64, linux-musl-x64, linux-musl-arm64, osx-x64, osx-arm64,
  win-x86, win-x64, win-arm64, freebsd-x64, freebsd-arm64.
- **phash exactness:** equal hashes are only guaranteed with the same ffmpeg build;
  StashDB matches phash by Hamming distance. So a file's phash often won't equal a
  submitted one exactly.
- **CI today:** `.github/workflows/build.yml` builds ffmpeg, tests, packs on every push to
  `main`, and on `v*` tags tries to push to nuget.org (no key; nuget.org is not an option:
  its account flow fails with proton.me addresses).
- **R2:** bucket `whisparr-nuget`, public URL `https://nuget.whisparr.com`. Repo secrets
  exist: `R2_ACCOUNT_ID`, `R2_ACCESS_KEY_ID`, `R2_SECRET_ACCESS_KEY`. plz12345 wants the
  bucket name kept as a secret too (`R2_BUCKET`, to be added). The public URL necessarily
  appears in whisparr-next's `NuGet.config`; that's fine (public domain, like
  `api.whisparr.com`).
- **StashDB:** `Scene.fingerprints { hash algorithm duration submissions }` is available on
  `findScene` and on batch `queryScenes`; Skyhook doesn't request it today. Upstream
  lookup: `findScenesBySceneFingerprints(fingerprints: [[{hash, algorithm}]])` returns, per
  file, the matching scenes. Algorithms seen: OSHASH, PHASH.
- **TPDB:** the normal `GET /scenes/{uuid}` response includes `hashes[]` with `hash`,
  `type` (OSHASH/PHASH), `duration`, `submissions`, `users` (drop `users`). Upstream
  lookup: `GET /scenes/hash/{hash}` returns the full scene; `GET /scenes?hash=&hashType=`
  returns a list.
- A scene commonly has many hashes (one per release/encode): e.g. 6 oshash + 4 phash on
  StashDB, 21 oshash + 4 phash on TPDB. `submissions` shows which are well confirmed.

## Part 1: publish the package to R2

- On a `v*` tag, the existing `pack` job's output is published to a static NuGet v3 feed in
  the R2 bucket, replacing the nuget.org step. Pushes to `main` keep building and testing
  but publish nothing.
- **Feed format (my call):** [Sleet](https://github.com/emgarten/Sleet), which writes a
  static NuGet v3 feed into S3-compatible storage. R2 is S3-compatible (endpoint
  `https://<account>.r2.cloudflarestorage.com`). The feed's base URI is
  `https://nuget.whisparr.com/`, so the service index is
  `https://nuget.whisparr.com/index.json`.
- **First step is a spike:** prove Sleet init + push + `dotnet restore` against the real
  bucket before writing the workflow. If Sleet and R2 don't get along, the fallback is
  for the workflow itself to generate a static v3 feed with `aws s3` (or `rclone`) uploads
  of `index.json`, the flat container and registration files. Report which was used.
- All four R2 values come from secrets; nothing about the bucket goes in the repo except
  the public URL.
- whisparr-next adds `https://nuget.whisparr.com/index.json` to its `NuGet.config` next to
  nuget.org, with package source mapping so `Whisparr.Fingerprints` only ever comes from
  it (my call; this also rules out a same-named package on nuget.org). whisparr-next has
  no CI yet; `build.sh` and `dotnet msbuild -restore` read `NuGet.config`, so no other
  build path needs touching.

## Part 2: Skyhook fingerprints (Eros-Metadata `v5`, by PR after a local Docker test)

- **Collect:** wherever Skyhook already receives scenes — StashDB single and batch queries,
  TPDB scene responses, hydration, bulk pages, refresh and change loops — it also keeps
  their hashes. StashDB queries add the `fingerprints` field. No extra upstream calls.
- **Store:** one `scenefingerprint` table: `hash`, `algorithm` (OSHASH/PHASH), `duration`,
  `submissions`, `source` (StashDb/Tpdb), `sceneid` (the source's scene UUID). Indexed by
  `(hash, algorithm, source)`. A scene's rows are replaced when the scene is re-cached, so
  the table follows upstream.
- **Lookup:** `POST fingerprint/lookup` with `{ source, localOnly, files: [{ key, oshash?,
  phash?, duration? }] }` (batched; `key` is the caller's file id; `localOnly` skips the
  upstream step, used to verify files the parser already matched). Returns per file the candidate
  scene ids with the matching algorithm, distance (0 for local exact hits), and
  submissions, best first.
  - Local exact match first.
  - On a local miss, ask upstream once per file: StashDB `findScenesBySceneFingerprints`
    (batched), TPDB `GET /scenes/hash/{hash}` (oshash first, then phash). Cache any scenes
    returned (that also stores their hashes, so the next lookup is local).
  - **Negative cache (my call):** an upstream miss for a hash is remembered for 12 hours
    in a database table (not memory, so a redeploy doesn't cause a burst), so rescans
    don't repeat it; a newly released scene still shows up within a day.
  - Upstream calls go through the existing rate limiters; a 429 or limiter queue-full
    answers a per-file "try later" status rather than failing the request.
- **Distance (my call):** Skyhook's local table answers exact matches only. For scenes
  returned by an upstream lookup, Skyhook computes the Hamming distance between the
  requested phash and each of the scene's stored phashes and reports the smallest. TPDB
  reports no distance, so Skyhook always computes it.
- **Duration:** lookups match on hash only. Each candidate carries the stored duration
  (whole seconds upstream) so the app can sanity-check it.
- **No backfill (my call):** already-cached scenes gain hashes as the normal refresh
  cycle re-fetches them; local misses go upstream meanwhile. A one-off backfill would cost
  StashDB thousands of calls.

## Part 3: whisparr-next

- **Package:** `PackageReference Include="Whisparr.Fingerprints"` in Core, restored from
  the R2 feed. The bundled ffmpeg must land in every published runtime (check the publish
  output per RID, as with NetVips).
- **Columns:** a migration (302, the next free) adds `MovieFiles.OsHash` (indexed),
  `MovieFiles.PHash`, `MovieFiles.PHashAttemptedAt`, `MovieFiles.MatchTier`; `ImportFiles`
  gets the three hash columns plus candidate storage (below).
- **`VideoHashService`:** computes hashes and swallows its own failures: it logs and returns
  nulls. Callers never see an exception from it.
- **When:**
  - oshash: inline while the import builds the `MovieFile` row (reads 128 KiB), plus a
    one-off backfill command for existing files.
  - phash: `GenerateVideoHashesCommand` at Low priority, behind a setting (default on, my
    call), only for files where `PHash` and `PHashAttemptedAt` are both null. A failure
    still sets `PHashAttemptedAt`. Only a size change on rescan clears the hashes.
  - Duration: the container duration from the existing media-info probe, rounded to
    0.01 s.
- **Health check:** warn when the bundled ffmpeg isn't available.
- **Name and hash check each other (gykes' suggestion, after namer; agreed by plz12345).**
  Every import runs the parser (`MovieService.FindScene`) *and* an oshash lookup, and the
  combination decides the outcome. Hooked in `ParsingService.GetSceneMovie`,
  `ImportDecisionMaker` and library-import lookup. Lookups go to Skyhook
  `fingerprint/lookup` with the locked scene source, batched.
  - Files the parser matched are looked up **local-only** (Skyhook's table, no upstream
    call), so verification adds no TPDB/StashDB load. Only parser failures may go
    upstream on a miss.
- **Hits never add scenes (plz12345).** Automatic linking only targets scenes already in
  the library. A hit on a scene that isn't in the library is only shown to the user as a
  candidate (title, studio, date), who decides whether to add the scene and link the file.
- **Match tiers** (replaces the earlier matching rules). "Candidate" means an in-library
  scene; phash candidates beyond distance 8 are dropped (my call).

  | Tier | Evidence | Action |
  |---|---|---|
  | Confirmed | parser and hash point to the same scene | link |
  | Hash only (oshash) | no parser match; one oshash candidate; file duration within 5 s of the hash's stored duration | link automatically (oshash covers content + size; the duration check catches a mistaken submission) |
  | Hash only (phash) | no parser match; phash candidate(s) | review ("potential bad phash") |
  | Name only | parser match; no hash data for the file | link, as today (most files have no submitted hashes) |
  | Conflict | parser says scene A, hash says scene B | review, both shown |

  Several oshash candidates, a duration mismatch, or a candidate not in the library also go
  to review.
- **phash comes later.** phash is computed in the background after import, so at import
  time the tiers rest on oshash. A later phash can only *promote* a review item to
  Confirmed, or *flag* an already-linked file as a Conflict for review. It never relinks
  on its own.
- **Stored tier:** `MovieFiles` records the tier it was linked with (new column, e.g.
  `MatchTier`), shown in the file's details, so the user can see why a file was matched.
- **Where review happens (plz12345):** downloads that fail the parser already land in the
  manual import screen; the hash candidates are shown there. Library import and rescans
  use `ImportFiles`. Candidates need storage on `ImportFiles` (e.g. a JSON candidates
  column, or a child table) — builder's choice; the manual-import path can compute them
  per request.
- **Renamed or moved files:** on rescan, when a scene's file is gone from its path and a
  new unknown file's oshash equals that `MovieFiles.OsHash` (oshash includes file size),
  re-point the row instead of marking the scene missing. Only when the old path is
  missing. Nothing on disk is touched.
- **Changed files (my call):** a size or modified-time change on rescan clears both hashes,
  so the file is treated as new. A null oshash (failed read) is retried on the next scan;
  phash still runs at most once per unchanged file.
- **Retry (my call):** a "try later" lookup result leaves the file unmatched; the next
  scheduled pass retries it. No tight retry loop.
- **Settings (my call):** the phash setting is in the UI (Media Management); the oshash
  backfill and phash generation appear as System tasks. The ffmpeg health check only
  fires when phash is enabled.

## Order

Part 1 → Part 2 → Part 3. Part 3 can start on oshash columns/service before Part 2 ships,
but lookups need Part 2 deployed.

## Left to the builder

- Sleet configuration details, or the fallback, per the spike.
- Exact Skyhook request/response field names, as long as the batch shape and per-file
  results above hold.
- How the review UI lists candidates, within the existing `ImportFiles` flow.
- Backfill pacing for large libraries (it must not hog command threads).
