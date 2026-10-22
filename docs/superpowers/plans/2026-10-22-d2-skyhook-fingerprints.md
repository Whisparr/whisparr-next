# D2: Skyhook scene fingerprints and lookup — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Skyhook keeps the oshash/phash values StashDB and TPDB already send with scenes, and answers `POST fingerprint/lookup` locally first, upstream on a miss.

**Architecture:** Scenes from both sources map to `Episode`; the mappers now fill `Episode.Fingerprints`, and the two scene cache services write those rows to a `scenefingerprint` table whenever a scene is cached. A new lookup service answers exact local hits, otherwise asks upstream once per file (StashDB `findScenesBySceneFingerprints`, TPDB `GET /scenes/hash/{hash}`), caches what comes back, and remembers misses for 12 hours in a `fingerprintmiss` table.

**Tech Stack:** .NET 10, ASP.NET Core, Dapper + FluentMigrator on Postgres, NUnit + Moq.

**Spec:** `docs/superpowers/specs/2026-10-22-fingerprints-design.md` (Part 2)

## Global Constraints

- Repo `~/Git/Whisparr-Eros-Metadata`. Branch `v5-fingerprints` from `origin/v5`; PR to `v5` after a local Docker test; hold the PR for plz12345.
- v5-only migrations are numbered from 100 (see `100_tpdb_scene_caches.cs`); this one is **101**. Creates tables only.
- No extra upstream calls to *collect* hashes: only fields added to requests Skyhook already makes.
- Upstream is asked only on a local miss, at most once per file per request, never when `localOnly` is true.
- Negative cache: **12 hours**, persisted in Postgres.
- phash candidates with Hamming distance **> 8** are dropped. Local matches are exact (distance 0).
- Drop TPDB's `users` field; never store it.
- Wire values: `source` is `"stashDb"` | `"tpdb"` (same strings whisparr-next uses for its scene source); `algorithm` is `"OSHASH"` | `"PHASH"`; `status` is `"matched"` | `"notFound"` | `"tryLater"`.
- Max **100** files per lookup request; more → 400.
- Local Docker builds must not carry `appsettings.Development.json` (`.dockerignore` already excludes it on `v5`; check the image).
- No AI attribution; file edits with Edit/Write only; no hosts/IPs in the repo.

## Review Focus

1. A scene re-cached with fewer hashes than before (upstream deleted a bad submission) — its old rows must be gone, not merged.
2. A hash that matches scenes in *both* sources — a `stashDb` lookup must never return TPDB scene ids, and vice versa.
3. Malformed input: phash that isn't 16-hex (or has `0x`), uppercase hex, empty strings, a file with neither hash — skip that file as `notFound`, never 500.
4. Upstream rate-limited (TPDB `RateLimited`, StashDB queue-full/429) mid-batch — those files return `tryLater`, already-answered files keep their results, and no miss is recorded for them.
5. A popular scene with hundreds of fingerprints — storing it must be one replace per scene, not one round trip per hash.

---

### Task 1: Fingerprint storage (model, migration, repository, service)

**Files:**
- Create: `src/Backend/Model/SceneFingerprint.cs` (`SceneFingerprint`, `FingerprintAlgorithm`)
- Create: `src/Backend/Cache/Fingerprints/CachedSceneFingerprint.cs`, `FingerprintMiss.cs`, `SceneFingerprintRepository.cs`, `FingerprintMissRepository.cs`, `FingerprintStore.cs`
- Create: `src/Backend/Datastore/Migration/101_scene_fingerprints.cs`
- Modify: `src/Backend/Datastore/TableMapping.cs` (map `scenefingerprint`, `fingerprintmiss`), `src/API/Startup.cs` (DI)
- Test: `src/Backend.Test/Cache/FingerprintStoreFixture.cs`

**Interfaces:**
- Produces:
  - `enum FingerprintAlgorithm { Oshash, Phash }` — serialized/parsed as `"OSHASH"`/`"PHASH"` (case-insensitive parse).
  - `enum FingerprintSource { StashDb, Tpdb }`.
  - `class SceneFingerprint { string Hash; FingerprintAlgorithm Algorithm; int? Duration; int Submissions; }` (duration in seconds).
  - `class CachedSceneFingerprint : ModelBase { string Hash; FingerprintAlgorithm Algorithm; int? Duration; int Submissions; FingerprintSource Source; string SceneId; }`
  - `interface IFingerprintStore`:
    - `Task Replace(FingerprintSource source, IEnumerable<Episode> scenes)` — for each scene with a non-empty `ForeignId`, delete its rows for that source and insert its current `Fingerprints` (lowercased hash), in one transaction per call.
    - `Task<IReadOnlyList<CachedSceneFingerprint>> FindExact(FingerprintSource source, IEnumerable<(FingerprintAlgorithm Algorithm, string Hash)> hashes)`.
    - `Task<IReadOnlySet<(FingerprintAlgorithm, string)>> RecentMisses(FingerprintSource source, IEnumerable<(FingerprintAlgorithm, string)> hashes)` — misses whose `expiry` > now.
    - `Task RecordMisses(FingerprintSource source, IEnumerable<(FingerprintAlgorithm, string)> hashes)` — upsert with `expiry = now + 12h`.

- [ ] **Step 1: Migration 101** — `scenefingerprint` (`id` identity PK, `hash` string, `algorithm` int, `duration` int null, `submissions` int, `source` int, `sceneid` string; index `(source, algorithm, hash)`, index `(source, sceneid)`), `fingerprintmiss` (`id`, `source` int, `algorithm` int, `hash` string, `expiry` datetime; unique index `(source, algorithm, hash)`).
- [ ] **Step 2: Failing tests** in `FingerprintStoreFixture` with mocked repositories (no DB in this test project):
  - `replace_deletes_then_inserts_per_scene` — scene `s1` with 2 hashes → repo delete called for `(StashDb, "s1")`, insert called with 2 rows, hashes lowercased.
  - `replace_skips_scenes_without_foreign_id_or_fingerprints` — no repo calls for them; a scene with an empty list still deletes its old rows.
  - `record_misses_sets_twelve_hour_expiry` — expiry within a second of `UtcNow.AddHours(12)`.
- [ ] **Step 3:** Run `dotnet test src/WhisparrApi.Metadata.sln --filter FingerprintStoreFixture` → FAIL.
- [ ] **Step 4:** Implement repositories (Dapper SQL, batch insert per scene list, `ON CONFLICT` upsert for misses) and `FingerprintStore`; register singletons in `Startup.cs`.
- [ ] **Step 5:** Tests → PASS; full suite green.
- [ ] **Step 6:** Commit `New: Scene fingerprint store`.

### Task 2: Collect hashes from scenes Skyhook already fetches

**Files:**
- Modify: `src/Backend/Model/Episode.cs` (add `[JsonIgnore] public List<SceneFingerprint> Fingerprints { get; set; }` — ignored so scene payloads and cache blobs don't grow; check which serializer the cache and API use and ignore for both)
- Modify: `src/Backend/MetadataSource/StashDb/Requests/GetSceneRequest.cs:9` (append `fingerprints { hash algorithm duration submissions }` to `Selection`), `Resources/StashSceneResource.cs`, `StashDbProxy.cs` scene mapping (~:1140)
- Modify: `src/Backend/MetadataSource/TPDb/Resources/SceneResource.cs` (`Hashes`: `hash`, `type`, `duration`, `submissions`), `TPDbProxy.cs` `MapScene` (~:717)
- Modify: `src/Backend/Cache/CachedSceneService.cs` (StashDB scenes) and `src/Backend/Cache/CachedTpdbSceneService.cs` (TPDB) — after a scene write succeeds, call `IFingerprintStore.Replace(source, scenes)`. Every path that caches a scene must go through one of these (check `CachedEntityService.Set` callers and `CachedTpdbEntityService.Upsert`).
- Test: `src/Backend.Test/SkyHook/SkyHookSceneFixture.cs`, `SkyHookTpdbSceneFixture.cs` (or new `*FingerprintMappingFixture.cs`), `src/Backend.Test/Cache/CachedTpdbSceneServiceFixture.cs`

**Interfaces:**
- Consumes: `IFingerprintStore.Replace`, `SceneFingerprint` (Task 1).
- Produces: `Episode.Fingerprints` populated by both mappers.

- [ ] **Step 1: Failing tests:**
  - `stash_scene_maps_fingerprints` — resource with `{hash:"51ca2cef47a7adaa", algorithm:"OSHASH", duration:888, submissions:7}` → `Episode.Fingerprints` has that one entry (Oshash, 888, 7).
  - `tpdb_scene_maps_hashes_and_drops_users` — `hashes` entry `{hash:"8387209f3f741736", type:"PHASH", duration:2035, submissions:24, users:[1,2]}` → one Phash entry, 2035, 24.
  - `unknown_algorithm_is_skipped` — `algorithm:"MD5"` → not mapped.
  - `caching_a_tpdb_scene_stores_its_fingerprints` — `CachedTpdbSceneService.Upsert` → `IFingerprintStore.Replace(Tpdb, …)` called with that scene; same for the StashDB scene cache write (`StashDb`).
  - `stash_scene_selection_requests_fingerprints` — `GetSceneRequest.Selection` contains `fingerprints { hash algorithm duration submissions }`.
- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement. **Step 4:** Tests → PASS; full suite green.
- [ ] **Step 5:** Note in the report whether TPDB *list* responses (site/performer scene pages) include `hashes`; if not, only single-scene fetches collect TPDB hashes (acceptable; upstream-on-miss covers it).
- [ ] **Step 6:** Commit `New: Keep scene fingerprints from StashDB and TPDB`.

### Task 3: `POST fingerprint/lookup`

**Files:**
- Create: `src/Backend/Fingerprints/FingerprintLookupService.cs`, `PhashDistance.cs`
- Create: `src/Backend/MetadataSource/StashDb/Requests/FindScenesByFingerprintsRequest.cs` (+ response resource)
- Modify: `src/Backend/MetadataSource/StashDb/StashDbProxy.cs` (add `Task<IReadOnlyList<IReadOnlyList<Episode>>> FindScenesByFingerprints(IReadOnlyList<IReadOnlyList<SceneFingerprint>> files)` — one list per file, same order, selection = `GetSceneRequest.Selection`; chunk files at 40 per request), `src/Backend/MetadataSource/TPDb/TPDbProxy.cs` (add `Task<Episode> GetTpdbSceneByHash(string hash)` → `GET scenes/hash/{hash}`, null on 404)
- Create: `src/API/Controllers/FingerprintController.cs`, `src/API/Resources/FingerprintLookup*.cs`
- Modify: `src/API/Startup.cs` (DI)
- Test: `src/Backend.Test/Fingerprints/FingerprintLookupServiceFixture.cs`, `PhashDistanceFixture.cs`, `src/Backend.Test/API/FingerprintControllerFixture.cs`

**Interfaces:**
- Consumes: `IFingerprintStore` (Task 1); scene caches (StashDB `ICachedSceneService` write path, `ICachedTpdbSceneService.Upsert`) so upstream hits are cached and their hashes stored (Task 2 hook).
- Produces (wire, consumed by D3):
  - Request: `{ "source": "stashDb"|"tpdb", "localOnly": bool, "files": [{ "key": string, "oshash": string?, "phash": string?, "duration": int? }] }`
  - Response: `{ "files": [{ "key": string, "status": "matched"|"notFound"|"tryLater", "candidates": [{ "sceneId": string, "algorithm": "OSHASH"|"PHASH", "distance": int, "submissions": int, "duration": int? }] }] }` — candidates sorted by distance asc, then submissions desc; one entry per (scene, algorithm), keeping the best.
  - `static int PhashDistance.Between(string a, string b)` — popcount of XOR of the two 64-bit hex values; throws `FormatException` on non-hex.

- [ ] **Step 1: Failing tests (`FingerprintLookupServiceFixture`, mocks for store/proxies/caches):**
  - `local_exact_hit_returns_matched_without_upstream` — store returns a row → `matched`, distance 0; proxies never called.
  - `local_only_miss_returns_not_found_without_upstream` — `localOnly: true`, no rows → `notFound`; no proxy, no miss recorded.
  - `stash_miss_asks_upstream_once_and_caches` — 3 missing files → one `FindScenesByFingerprints` call with 3 entries; returned scenes written to the scene cache; files with results `matched`; empty ones `notFound` and recorded as misses.
  - `tpdb_miss_tries_oshash_then_phash` — oshash 404, phash hit → one candidate with `algorithm: PHASH` and computed distance; at most 2 calls for that file.
  - `recent_miss_skips_upstream` — `RecentMisses` contains the hash → `notFound`, no proxy call.
  - `rate_limited_returns_try_later_and_records_no_miss` — proxy throws `TPDbApiException` with `RateLimited` (and StashDB queue-full) → `tryLater`; earlier files in the batch keep their results.
  - `phash_candidates_beyond_distance_8_are_dropped`.
  - `sources_do_not_cross` — `stashDb` lookup only queries `FingerprintSource.StashDb` rows.
- [ ] **Step 2: Failing tests (`PhashDistanceFixture`):** `"8387209f3f741736"` vs itself → 0; vs `"8387319f3d741636"` → the popcount of their XOR (compute in the test from `ulong` values); uppercase input equals lowercase; `"0x…"`/non-hex → `FormatException`.
- [ ] **Step 3: Failing tests (`FingerprintControllerFixture`):** 101 files → 400; unknown `source` → 400; a file with neither hash → `notFound` without calling the service for it; malformed phash → that file `notFound`, others unaffected.
- [ ] **Step 4:** Run → FAIL. **Step 5:** Implement (route `fingerprint/lookup`, `[HttpPost]`, no `v5/` prefix in code, like the other v5 routes). **Step 6:** Tests → PASS; full suite green.
- [ ] **Step 7:** Commit `New: Fingerprint lookup with upstream fallback and a 12-hour miss cache`.

### Task 4: Local Docker test and PR (controller)

- [ ] **Step 1:** Build the image from the branch; confirm `/app` holds no `appsettings.Development.json`. Run it against the local `skyhook-v5-pg` (migration 101 applies).
- [ ] **Step 2:** Fetch a known StashDB scene and a TPDB scene through the container; `scenefingerprint` gains their rows (counts by source).
- [ ] **Step 3:** `POST /fingerprint/lookup`:
  - `stashDb` with oshash `51ca2cef47a7adaa` → `matched` scene `161e85b1-0807-487d-a3ca-46aba503660c`.
  - `tpdb` with oshash `0734dc8674f38fdb` → `matched` `6b2be167-bf62-498f-a536-8fbe454b8b78`.
  - An unknown hash twice → first asks upstream, second is answered from `fingerprintmiss` (log shows no second upstream call).
  - `localOnly: true` with an unknown hash → `notFound`, no upstream call.
- [ ] **Step 4:** Delete the test image. Push the branch, open the PR to `v5`, hold.
