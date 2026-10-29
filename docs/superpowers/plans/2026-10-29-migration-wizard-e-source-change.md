# Migration wizard G-e: v2 to StashDB, Eros scene-source change, preflight first — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Lift the two `SetupNotAvailableYet` refusals. A v2 install can migrate to StashDB scenes, and an Eros install can change its scene source (StashDB → TPDB). Both are rebuilt in phase 2 from the user's own data, and nothing is linked on a weak match. The Skyhook preflight moves ahead of every write.

**Architecture:**
- **Phase 1 (setup mode).**
  - `SetupWizardService` decides up front whether the chosen path needs Skyhook, and checks it before the backup.
  - For an Eros source change, a new `SceneSourceSeeder` turns the copied DB's scene, studio and performer rows into seed rows (migration 310). Scene files are detached into `ImportFiles`, and the rows the spec leaves behind are counted in the report.
- **Phase 2 (normal app).** Two new runners plug into the G-d `ILegacyImportRunner` loop:
  - `V2StashImportRunner` shares the v2 pass loop with `V2TpdbImportRunner` through an extracted base class;
  - `ErosRebuildImportRunner` works its seeds in stages: studios, then performers, then scenes, then the G-d movie seeds.
- **Shared pieces.**
  - `ISceneSourceIdMapper` maps studio and performer ids across sources: first through Skyhook's stored id mappings (`ForeignIds`), then by a single exact title match.
  - `LegacySceneMatcher` provides an exact title-and-date match against a catalog.
  - `ILegacyHashMatcher` matches a file to exactly one scene by oshash.

**Tech Stack:** C#/.NET 10, DryIoc auto-wiring, Dapper, FluentMigrator, NUnit + Moq + FluentAssertions, React 19 (the setup page only gains a step label).

**Spec:** `docs/superpowers/specs/2026-10-24-migration-wizard-design.md`. The relevant sections are "Phase 1" step 3 (Skyhook preflight), "Building the v5 DB, by source" (Eros, new scene source), "Data disposition" (the Eros new-source column), and "Phase 2" (v2 scenes on StashDB, Eros rebuild, the confidence bar).

## Recon this plan rests on

- **Cross-source ids exist only for studios and performers.**
  - A StashDB studio or performer from Skyhook carries `ForeignIds.TpdbId`.
  - A TPDB site or performer carries `ForeignIds.StashId`.
  - Both come from Skyhook's `TpdbStudioMapping` and `TpdbPerformerMapping` tables.
  - Eros already stores them on `Studios.TpdbId` and `Performers.TpdbId`.
  - **Scenes have no cross-source id.** A scene can be matched only by fingerprint or by title and date.
- **Skyhook's `studio/mapping` route is StashDB ↔ TMDB** and needs an API key. It is not used here.
- **The fingerprint lookup takes its source from the locked scene source.** `FingerprintLookupClient` reads `IConfigService`, so in phase 2 it already asks the target source.
- **Eros never hashed files.** Detached Eros files have a null `OsHash`, so phase 2 computes it.
- **The G-d fallback for an unmatched file is `ILegacyFileLinker.Park`.** It parks the file in `ImportFiles`. From there, the background `ImportFileMatchService` (D) proposes candidates, including phash ones, for manual review.

## Rulings made while planning (tell plz12345)

1. **The preflight runs before the backup**, not just before the port.
   - It writes nothing, so a Skyhook outage refuses up front.
   - A Retry doesn't write a second backup zip.
   - It is shown as the first step.
2. **v2 → StashDB monitored state is safe by construction.**
   - Catalog scenes are added unmonitored.
   - A scene becomes monitored only when a v2 episode matched it, either through its file or by an exact title and date, and that episode was monitored.
   - Catalog scenes with no v2 twin stay unmonitored, and each studio gets one report row giving the count. This departs from the TPDB path, which applies `MonitorNewItems`. Without a scene-id link, a missed twin under "All" would come back monitored and be grabbed after completion.
3. **A v2 file is identified in this order, and the first single match wins.** Ambiguous or no match: the file is parked.
   1. Its v2 episode's title and air date against the StashDB studio's catalog. This is the structured form of "parser", using v2's own metadata.
   2. `Parser.ParseMoviePath`, then `IMovieService.FindScene`.
   3. An exact oshash.

   phash is left to the D background matcher on parked files, which only proposes candidates. That keeps "phash at most once per file" and never auto-links a phash match during the migration.
4. **The Eros rebuild keeps Eros's scope.**
   - Studios and performers are added without importing their catalogs.
   - Only the scenes the user had are re-identified, by oshash and then by an exact title and date in the mapped studio's catalog.
   - The user's own studio and performer settings, including `Monitored` and `WhisparrMonitorNewItems`, go back on afterwards. Later refreshes then behave as they did in Eros.
5. **Eros rows that the spec doesn't spell out:**
   - Only `Visible` performers are seeded. Hidden ones come back through scene credits.
   - Studio and performer `ImportExclusions` are rewritten in phase 1 from the stored `TpdbId`. Those without one, and every scene exclusion, are deleted and counted.
   - A scene outside every root folder still becomes a seed, but its `MovieFiles` row is dropped. The file stays on disk, and the report lists its path.

## Global Constraints

- **Edits:** use the Edit and Write tools only. Never sed, shell redirection or chained Bash. Run one plain command per Bash call, with absolute paths.
- **Commits:** on `develop` in `~/Git/whisparr-next`. Never push.
- **Attribution and naming:**
  - No AI attribution.
  - Use the plz12345 handle and `~/` paths.
  - No infrastructure details (hosts, proxy layout).
- **Commit prefixes:** `New:`, `Fix:` or `Chore:`.
- **Read-only legacy data.**
  - Phase 1 reads the legacy DB only through the existing read-only connections, and the seeder runs on the new v5 DB only.
  - Phase 2 never opens the legacy DB or `config.xml`.
- **Disk:** no step renames, moves or deletes a file on disk. Detach, link and park write DB rows only.
- **No automatic grabs while `LegacyMigrationState.Completed` is false.** The G-d gate already does this; nothing here adds a search. Every add uses `SearchForMovie = false` and `SearchOnAdd = false`.
- **Confidence bar** (spec, my call): anything weaker than the bar becomes a `NeedsAttention` report row, and its file is parked.
  - **A Skyhook id mapping (`ForeignIds`) links automatically.**
  - **A title match links only when it is a single exact match.**
    - For studios and performers, the titles must be equal after `CleanMovieTitle()`.
    - For scenes, the titles must be equal after `CleanMovieTitle()` and the release date must be the same day.
  - **A hash match links only when the oshash candidates name exactly one scene.**
- **Retries:**
  - Use `LegacyRetryPolicy` and the G-d outage rule unchanged.
  - Transient means `HttpException`, `WebException`, `TimeoutException`, or a fingerprint `TryLater`.
  - Everything else is reported and the item counts as done.
- **Postgres:** v5 `Id` columns are GENERATED ALWAYS. Raw inserts use `RETURNING "Id"` and never name the `Id` column.
- **Migrations:** this plan adds 310 only.
- **New strings** go in `src/NzbDrone.Core/Localization/Core/en.json` (flat, PascalCase, alphabetical).
- **Tests:**
  - NUnit `*Fixture`, 80% coverage on new code.
  - Run each test project separately (Core, Api, Common, Mono, Host).
  - Run Core on SQLite and on Postgres.
  - Frontend: `yarn typecheck`, `yarn lint` and `yarn build`, run from the repo root.

## Review Focus

1. **Kill mid-rebuild, between the studio stage and the scene stage.**
   - On restart, no studio is added twice (`FindByForeignId` comes before the add, and resolved seeds are skipped).
   - The scenes then run against the studios already added.
   - Task 8 test.
2. **Two Eros scenes resolve to the same TPDB scene** (StashDB duplicates).
   - The first links.
   - The second gets `SceneHasOtherFile`: a `NeedsAttention` row, and its file stays parked.
   - Task 8 test.
3. **Two StashDB studios map to one TPDB site.**
   - The second seed reuses the studio already added.
   - The first seed's settings win, and the report says the two were merged.
   - Task 8 test.
4. **A file can't be hashed** (permission denied, or the file is gone).
   - The hash is null and there is no hash match. Matching falls through to the catalog step or to park.
   - No exception reaches the pass.
   - Task 5 test.
5. **A v2 oshash names a StashDB scene outside the series' studio.** The scene is added through `AddMovies` (exact hash) with the episode's monitored state, and the file links. Task 7 test.

---

### Task 1: Skyhook preflight before any write

**Files:**
- Modify: `src/NzbDrone.Core/Datastore/Migration/Setup/SetupProgress.cs`: move `SkyhookPreflight` to be the first `SetupStep` member.
- Modify: `src/NzbDrone.Core/Datastore/Migration/Setup/SetupWizardService.cs`
- Test: `src/NzbDrone.Core.Test/Datastore/Migration/Setup/SetupWizardServiceFixture.cs`

**Interfaces:**
- **Produces** `internal static bool NeedsSkyhook(SetupChoice choice, LegacyInstall install)` on `SetupWizardService`:
  - v2: `install.Studios > 0`;
  - Eros, when `choice.SceneSource != install.PreviousSceneSource`: `true`;
  - otherwise: `install.HasMixedMovies`.
- **`Run`** checks Skyhook first, before `Backup`, when `NeedsSkyhook` is true. Otherwise it marks the step `Skipped`.
  - The old check after the port is removed.
  - A failure keeps today's mapping: `SkyhookUnavailable` with `CanRetry = true`.

- [ ] **Step 1:** Write the failing tests:
  - `preflight_failure_stops_before_backup`: the preflight throws `SetupStepException`. Assert:
    - `_backup` was never called;
    - `ErrorKey == "SkyhookUnavailable"` and `CanRetry`;
    - `_runState.Begin` was never called.
  - `preflight_skipped_for_eros_same_source_without_mixed_movies`: the step is `skipped` and `Check` is never called.
  - `preflight_runs_for_v2_with_studios`
  - `needs_skyhook_table`: a `[TestCase]` per branch above, plus v2 with `Studios = 0` → false.
  - `preflight_is_first_step`: `Progress.Steps[0].Step == SetupStep.SkyhookPreflight`.
- [ ] **Step 2:** Run `dotnet test src/NzbDrone.Core.Test/Whisparr.Core.Test.csproj --filter "FullyQualifiedName~SetupWizardServiceFixture"`. Expected: the new tests FAIL.
- [ ] **Step 3:** Implement. Existing tests that assert the old step order or the after-port preflight are updated to the new order, not deleted.
- [ ] **Step 4:** Run the same filter. Expected: PASS.
- [ ] **Step 5:** Commit `Fix: Check the metadata service before the migration backs anything up`.

### Task 2: Seed tables for the Eros scene-source change

**Files:**
- Create: `src/NzbDrone.Core/Datastore/Migration/310_add_legacy_scene_source_seeds.cs`
- Create: `src/NzbDrone.Core/LegacyMigration/LegacyStudioSeed.cs`, `LegacyPerformerSeed.cs`, `LegacySceneSeed.cs`
- Modify: `src/NzbDrone.Core/LegacyMigration/LegacyMigrationState.cs` (add `Rebuild`), `src/NzbDrone.Core/Datastore/TableMapping.cs`, and `src/NzbDrone.Core/LegacyMigration/LegacyV2Repositories.cs` (add the three repositories next to the existing ones)
- Test: `src/NzbDrone.Core.Test/LegacyMigration/LegacySceneSourceSeedRepositoryFixture.cs` (a `DbTest`)

**Interfaces:**
- **Migration 310:**
  - `LegacyMigrationState.Rebuild`: bool, default false.
  - **`LegacyStudioSeeds`:**

    | Column | Type |
    |---|---|
    | `LegacyForeignId` | string, indexed |
    | `LegacyTpdbId` | string, nullable |
    | `Title` | string |
    | `Monitored` | bool |
    | `MoviesMonitored` | bool |
    | `WhisparrMonitorNewItems` | bool |
    | `AfterDate` | DateTime, nullable |
    | `RootFolderPath` | string |
    | `QualityProfileId` | int |
    | `Tags` | string, JSON |
    | `NewForeignId` | string, nullable |
    | `Resolved` | bool |
    | `Attempts` | int, default 0 |
    | `LastAttemptAt` | DateTime, nullable |

  - **`LegacyPerformerSeeds`:** the same columns, with `Name` in place of `Title`.
  - **`LegacySceneSeeds`:**

    | Column | Type |
    |---|---|
    | `LegacyForeignId` | string |
    | `Title` | string |
    | `ReleaseDate` | DateTime, nullable |
    | `LegacyStudioForeignId` | string, nullable |
    | `Monitored` | bool |
    | `QualityProfileId` | int |
    | `Path` | string |
    | `RootFolderPath` | string, nullable |
    | `Tags` | string, JSON |
    | `ImportFileId` | int, nullable |
    | `FileSize` | long |
    | `FileQuality` | string, JSON |
    | `FileLanguages` | string, JSON |
    | `FileMediaInfo` | string, JSON |
    | `FileReleaseGroup` | string |
    | `FileSceneName` | string |
    | `FileOriginalFilePath` | string |
    | `FileIndexerFlags` | int |
    | `FileEdition` | string |
    | `FileDateAdded` | DateTime, nullable |
    | `Resolved` | bool |
    | `Attempts` | int, default 0 |
    | `LastAttemptAt` | DateTime, nullable |

    The `File*` columns are null or 0 when the scene had no file.
- **Produces:**
  - `ILegacyStudioSeedRepository : IBasicRepository<LegacyStudioSeed>`
  - `ILegacyPerformerSeedRepository : IBasicRepository<LegacyPerformerSeed>`
  - `ILegacySceneSeedRepository : IBasicRepository<LegacySceneSeed>`
- Models use these property names exactly. `Tags` and the `File*` JSON columns are plain strings on the model, like `LegacyMovieSeed.Tags`.

- [ ] **Step 1:** Write the failing tests:
  - `seed_rows_round_trip`: one row of each type with every column set, read back equal.
  - `state_rebuild_defaults_false`
- [ ] **Step 2:** Run with `--filter "FullyQualifiedName~LegacySceneSourceSeedRepositoryFixture"`. Expected: FAIL.
- [ ] **Step 3:** Implement, following the 307/309 migration and `TableMapping` entries.
- [ ] **Step 4:** Run on SQLite and Postgres. Expected: PASS.
- [ ] **Step 5:** Commit `New: Seed tables for rebuilding an Eros library from a new scene source`.

### Task 3: Phase 1 seeder for an Eros scene-source change

**Files:**
- Create: `src/NzbDrone.Core/Datastore/Migration/Setup/LegacyFileDetacher.cs`. This is `MixedMovieResolver.Detach` plus its path helpers (`FindRoot`, `IsUnder`, `TrimEnd`, `Join`, `FileNameWithoutExtension`), moved as `internal static`. `MixedMovieResolver` calls it, with no behavior change.
- Create: `src/NzbDrone.Core/Datastore/Migration/Setup/SceneSourceSeeder.cs`
- Modify:
  - `SetupProgress.cs`: add `SetupStep.SceneSeeds` after `MixedMovies`.
  - `SetupWizardService.cs`
  - `V5DatabaseFinisher.cs`, only if `Rebuild` must be passed through.
  - `en.json`: `"SetupStepSceneSeeds": "Prepare your scenes for the new scene source"`.
- Test: `src/NzbDrone.Core.Test/Datastore/Migration/Setup/SceneSourceSeederFixture.cs`. Build the v5 DB with the existing copy-and-migrate test helpers that `MixedMovieResolverFixture` uses, then seed rows with SQL.

**Interfaces:**
- **Produces:**
  - `ISceneSourceSeeder { SceneSeedResult Seed(IDbConnection v5Connection, IDbTransaction transaction); }`
  - `SceneSeedResult { int Studios; int Performers; int Scenes; int Detached; int Dropped; bool NeedsPhase2 => Studios + Performers + Scenes > 0; }`. `Dropped` counts files outside every root folder.
- **Seed in this order, in the one transaction:**
  1. **Scenes:** every `Movies` row whose `MovieMetadata.ItemType = 1` (Scene) becomes a `LegacySceneSeeds` row.
     - Take `ReleaseDate` from `MovieMetadata.ReleaseDateUtc`.
     - With a file under a root folder: `LegacyFileDetacher.Detach` gives `ImportFileId`, and the `File*` columns come from the `MovieFiles` row before it is deleted.
     - With a file outside every root folder: the `MovieFiles` row is deleted. A `NeedsAttention` report row goes in, with Area `Scenes`, Subject the file path, and Detail `"Outside every root folder; not linked"`.
  2. **Studios:** every `Studios` row becomes a `LegacyStudioSeeds` row, with `LegacyTpdbId = Studios.TpdbId`.
  3. **Performers:** every `Performers` row with `Visibility = 1` becomes a `LegacyPerformerSeeds` row.
  4. **`ImportExclusions`:**
     - Type `Studio` or `Performer` with a known `TpdbId`: `ForeignId` is rewritten to that `TpdbId`.
     - Every other studio or performer exclusion, and every `Scene` exclusion: deleted.
     - One `LeftBehind` row per type, with the count.
  5. **`ImportListMovies`:** deleted, with one `LeftBehind` row and the count.
  6. **`LeftBehind` counts (rows not deleted):** `ExtraFiles`, `SubtitleFiles`, `MetadataFiles`, `History` and `Blocklist` rows whose `MovieId` is a seeded scene. A table with a count of 0 gets no row.
  7. **Delete:**
     - scene `Credits`, `AlternativeTitles`, `Movies` and `MovieMetadata`, in the `MixedMovieResolver.DeleteMovie` order;
     - every `Studios` row;
     - every `Performers` row.
- **Report rows** are inserted with the same SQL shape `V2Porter.WriteReport` uses for `LegacyMigrationReportItems`.
- **`SetupWizardService.Run`, Eros branch.** When `choice.SceneSource != install.PreviousSceneSource`:
  - run the seeder after `ResolveMixedMovies`, in its own transaction, under `SetupStep.SceneSeeds`;
  - `needsPhase2 = mixed.NeedsPhase2 || seeded.NeedsPhase2`;
  - the state row gets `Rebuild = true`.

  Otherwise `SceneSeeds` is `Skipped`.

- [ ] **Step 1:** Write the failing tests:
  - `scene_with_file_becomes_seed_and_import_file`: the seed fields, `File*` columns, `ImportFiles.Path`, and the `MovieFiles`, `Movies` and `MovieMetadata` rows gone.
  - `scene_without_file_becomes_seed_without_import_file`
  - `scene_file_outside_root_is_dropped_and_reported`
  - `movies_are_untouched`: an `ItemType = 0` row and its file survive.
  - `studios_and_visible_performers_become_seeds`: a hidden performer is not seeded, and all `Studios` and `Performers` rows are gone.
  - `exclusions_rewritten_or_counted`
  - `import_list_movies_dropped_and_counted`
  - `left_behind_counts_only_scene_rows`
  - `mixed_movie_resolver_unchanged`: run the existing `MixedMovieResolverFixture`, which must stay green.
  - In `SetupWizardServiceFixture`, the Eros source-change refusal test becomes `eros_source_change_runs_seeder_and_sets_rebuild`. It asserts that the seeder is called, the state row has `Rebuild = true`, and the phase is `Phase2Pending`. This task removes the Eros `SetupNotAvailableYet` return in `Validate`. Develop stays unpushed until Task 8 adds the runner.
- [ ] **Step 2:** Run with `--filter "FullyQualifiedName~SceneSourceSeederFixture|FullyQualifiedName~MixedMovieResolverFixture|FullyQualifiedName~SetupWizardServiceFixture"`. Expected: the new tests FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run on SQLite and Postgres. Expected: PASS.
- [ ] **Step 5:** Commit `New: Turn an Eros library into seeds when its scene source changes`.

### Task 4: Cross-source studio and performer id mapper

**Files:**
- Create: `src/NzbDrone.Core/MetadataSource/SkyHook/SceneSourceIdMapper.cs`
- Test: `src/NzbDrone.Core.Test/MetadataSource/SkyHook/SceneSourceIdMapperFixture.cs`, with a mocked `IHttpClient`, like `TpdbIdResolverFixture`.

**Interfaces:**
- **Produces:**
  - `public enum IdMapMethod { None, Mapping, Title }`
  - `public sealed record IdMapResult(string Id, IdMapMethod Method)`. `Id` is null when `Method == None`.
  - `ISceneSourceIdMapper` with two methods:
    - `IdMapResult MapStudio(SceneMetadataType from, string fromId, string title)`
    - `IdMapResult MapPerformer(SceneMetadataType from, string fromId, string name)`

    The target source is the other one.
- **Wire:**
  - Build requests like `TpdbIdResolver` does: its own `HttpRequestBuilder` from `WhisparrMetadata`, falling back to `https://api.whisparr.com/v5/{route}`. Don't go through `SkyHookProxy`, because its routes follow the locked source.
  - **Fetch by id:**
    - StashDB: `site/{id}` and `performer/{id}`.
    - TPDB: `tpdb/site/{id}` and `tpdb/performer/{id}`.
    - Read `ForeignIds.TpdbId` or `ForeignIds.StashId` from the existing `SkyHook/Resource` studio and performer resources.
  - **Title fallback:**
    - Use the target source's search route: `site/search` or `tpdb/site/search`, and `performer/search` or `tpdb/performer/search`.
    - Copy the query-parameter name from `SkyHookProxy.SearchForNewStudio` and `SearchForNewPerformer`.
    - Keep only results whose `CleanMovieTitle()` equals the input's. Exactly one distinct id → `Title`; otherwise `None`.
  - **Errors:**
    - A 404 on the fetch by id moves on to the title fallback.
    - Any other non-2xx, or an exception, is thrown as `HttpException`, so it is transient.
    - A null or empty `fromId` skips straight to the title fallback.

- [ ] **Step 1:** Write the failing tests:
  - `studio_mapped_by_foreign_ids` (StashDB → TPDB)
  - `performer_mapped_by_foreign_ids` (TPDB → StashDB)
  - `falls_back_to_single_exact_title`
  - `two_exact_titles_is_none`
  - `near_title_is_none`
  - `not_found_id_falls_back_to_title`
  - `server_error_throws_http_exception`
  - `routes_follow_from_source`: assert the request URLs.
- [ ] **Step 2:** Run with `--filter "FullyQualifiedName~SceneSourceIdMapperFixture"`. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `New: Map studios and performers between StashDB and TPDB for the migration`.

### Task 5: Scene matchers (title and date, oshash)

**Files:**
- Create: `src/NzbDrone.Core/LegacyMigration/LegacySceneMatcher.cs`
- Create: `src/NzbDrone.Core/LegacyMigration/LegacyHashMatcher.cs`
- Test: `src/NzbDrone.Core.Test/LegacyMigration/LegacySceneMatcherFixture.cs` and `LegacyHashMatcherFixture.cs`

**Interfaces:**
- **`public static class LegacySceneMatcher`:**
  - `string FindExact(IEnumerable<SceneKey> catalog, string title, DateTime? releaseDate)`, where `public sealed record SceneKey(string ForeignId, string Title, DateTime? ReleaseDate)`.
  - It returns the single distinct `ForeignId` whose `CleanMovieTitle()` equals the title's and whose date is the same day.
  - It returns null when the title is blank, the date is null, or there are 0 or 2+ matches.
- **`ILegacyHashMatcher`:**
  - `Dictionary<string, HashMatch> Match(IReadOnlyCollection<HashQuery> files)`, where:
    - `public sealed record HashQuery(string Key, string Path, string KnownOsHash)`
    - `public sealed record HashMatch(string SceneId, string OsHash, bool TryLater)`
  - Every input key gets an entry.
  - **Computing the hash:**
    - A null `KnownOsHash` is computed with `IVideoHashService.ComputeOsHash(Path)`.
    - A throw or null gives `HashMatch(null, null, false)`, and the error is logged at Debug.
  - **Lookup:** one `IFingerprintLookupClient.Lookup(…, localOnly: false)` call for the hashed files. The client batches internally.
  - **Matching:**
    - Only candidates with `Algorithm` equal to `"OSHASH"` (case-insensitive) count.
    - Exactly one distinct `SceneId` → a match.
    - `TryLater` → `HashMatch(null, osHash, true)`.

- [ ] **Step 1:** Write the failing tests.
  - **Matcher:**
    - `exact_title_and_date_matches`
    - `title_differs_only_in_punctuation_matches` ("Scene: One!" vs "Scene One")
    - `same_title_other_day_is_null`
    - `duplicate_catalog_entries_same_id_match`
    - `two_ids_is_null`
    - `null_date_is_null`
  - **Hash:**
    - `single_oshash_candidate_matches`
    - `two_oshash_scenes_is_no_match`
    - `phash_only_candidate_is_no_match`
    - `known_hash_is_not_recomputed`
    - `hash_failure_is_no_match_without_throwing` (Review Focus 4)
    - `try_later_is_flagged`
- [ ] **Step 2:** Run with `--filter "FullyQualifiedName~LegacySceneMatcherFixture|FullyQualifiedName~LegacyHashMatcherFixture"`. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `New: Exact title-and-date and oshash matchers for the migration`.

### Task 6: Extract the shared v2 pass loop

**Files:**
- Create: `src/NzbDrone.Core/LegacyMigration/V2SeriesImportRunnerBase.cs`
- Modify: `src/NzbDrone.Core/LegacyMigration/V2TpdbImportRunner.cs`
- Test: the existing `src/NzbDrone.Core.Test/LegacyMigration/V2TpdbImportRunnerFixture.cs` must pass **unchanged**.

**Interfaces:**
- **Produces** `public abstract class V2SeriesImportRunnerBase : ILegacyImportRunner`.
- **Moved into the base, unchanged:**
  - `RunPass`, `TryImport`, `RecordTransientFailure`;
  - `ApplyV2Settings` and `ApplyV2SettingsToCreatedStudio`;
  - `ResolveSite`;
  - `Fill`, `ParkAndFinish`, `ParkOrphanFiles`, `MarkFileDone`, `MarkSeriesDone`;
  - `WaitedTooLong`, `RecordFailure`, `IsTransient`, `ParseTags`;
  - the two exceptions;
  - `StudioArea` and `FileArea`;
  - the studio-uuid bookkeeping. Rename it to `CreatedStudioIds`, keyed by `LegacyId`.
- **Abstract members:**
  - `public abstract bool Handles(LegacyMigrationState state)`
  - `protected abstract void Import(LegacyV2Series series, List<LegacyV2Episode> episodes, List<LegacyV2EpisodeFile> files, ILegacyImportProgress progress)`
- **Visibility:** whatever the subclasses need becomes `protected`.
- **`IsTransient`** also treats `FingerprintTryLaterException` (new, in the base file) as transient.
- **`V2TpdbImportRunner`** keeps only `Handles`, `Import`, `ResolveScenes`, `LinkFile` and `ReportOutcome`.

- [ ] **Step 1:** Run `--filter "FullyQualifiedName~V2TpdbImportRunnerFixture"` and record the pass count.
- [ ] **Step 2:** Refactor.
- [ ] **Step 3:** Run the same filter. Expected: the same count, all passing, with no edits to the fixture.
- [ ] **Step 4:** Commit `Chore: Share the v2 import pass loop between scene sources`.

### Task 7: v2 → StashDB runner

**Files:**
- Create: `src/NzbDrone.Core/LegacyMigration/V2StashImportRunner.cs`
- Test: `src/NzbDrone.Core.Test/LegacyMigration/V2StashImportRunnerFixture.cs`, built like `V2TpdbImportRunnerFixture`.

**Interfaces:**
- **Consumes:**
  - `V2SeriesImportRunnerBase` (Task 6);
  - `ISceneSourceIdMapper` (Task 4);
  - `LegacySceneMatcher` and `ILegacyHashMatcher` (Task 5);
  - `ISceneCatalogImporter`, `IProvideMovieInfo.GetStudioScenePage`, `IMovieService.GetByStudioForeignId`, `IMovieService.FindScene`, `IAddMovieService`, `IStudioService`, `IAddStudioService`, `ILegacyFileLinker`.
- **`Handles`:** `SourceKind == V2 && SceneSource == SceneMetadataType.StashDb`.
- **`Import(series, …)`:**
  1. `siteUuid = ResolveSite(series)`, which may be null.
  2. `MapStudio(Tpdb, siteUuid, series.Title)`.
     - `None` → `NeedsAttention` "Studio not found on StashDB", then `ParkAndFinish`, and return.
     - `Title` → also an Info row in `NeedsAttention`, "Matched by title", so the user can check it.
  3. **Add the studio and its catalog:**
     - Find or add the studio unmonitored, exactly as the TPDB runner does, keyed by the StashDB id.
     - Run the catalog import through `ImportScenes`, with every stub `Monitored = false` and `Path = series.Path`.
  4. **Load the library catalog** with `GetByStudioForeignId(stashId)`, as `SceneKey(ForeignId, Title, ReleaseDateUtc)`.
  5. **Link each pending file.** For each pending file whose owner episode is the first episode by `LegacyId` with `LegacyEpisodeFileId == file.LegacyId`:
     1. Try `FindExact(catalog, episode.Title, ParsedAirDate)`.
     2. Else `FindScene(Parser.ParseMoviePath(file.Path))`, when the result is in this studio.
     3. Else the hash match, run as one `Match` call for all the series' files that are still unmatched.
        - `TryLater` on any file → throw `FingerprintTryLaterException`, so the whole series retries.
        - A hash scene not in the library is added with `AddMovies(…, refreshInline: true)` (Review Focus 5).

     Then:
     - **Matched:** `Link(movie, file.Path, f => Fill(f, file))`. Outcomes are reported as the TPDB runner reports them. If the outcome is `Linked` or `AlreadyLinked` and `episode.Monitored`, set the movie `Monitored = true` and save it.
     - **Unmatched:** `Park(file.Path, series.QualityProfileId, FileArea, "Not identified on StashDB")`.
     - `MarkFileDone` either way.
  6. **Episodes without a linked file:** `FindExact` against the catalog, minus the scenes already claimed. A match with `episode.Monitored` sets the movie to monitored.
  7. **Report:** if any catalog scene stays unmatched to a v2 episode, add one `LeftBehind` row with Area `Scenes`, Subject `series.Title`, Detail `"StashDB scenes with no v2 twin, left unmonitored"`, and Count the number of those scenes.
  8. `ApplyV2Settings`, then `MarkSeriesDone`.
- **`ParsedAirDate`:** v2's `AirDate` string parsed with `DateTime.TryParse` under the invariant culture. Null when it doesn't parse.

- [ ] **Step 1:** Write the failing tests:
  - `handles_only_v2_stashdb`
  - `studio_unmapped_parks_files_and_reports`
  - `title_mapped_studio_adds_attention_row`
  - `catalog_added_unmonitored`
  - `file_linked_by_title_and_date_copies_monitored`
  - `file_linked_by_parser_when_title_misses`
  - `file_linked_by_oshash_adds_scene_outside_studio` (Review Focus 5)
  - `ambiguous_file_parked`
  - `fingerprint_try_later_retries_series_without_spending_attempt_when_outage`: reuse the G-d outage assertions.
  - `episode_without_file_monitors_its_twin`
  - `unmatched_catalog_count_reported`
  - `resume_skips_done_files`: two passes, no duplicate link or report row.
  - In `SetupWizardServiceFixture`, the v2 StashDB refusal test becomes `v2_with_stashdb_runs`. It asserts that the backup is called and the state is `SourceKind = V2`, `SceneSource = StashDb`, `Phase2Pending`. This task removes the v2 `SetupNotAvailableYet` return in `Validate`. Keep the `SetupErrorKey` constant, because the frontend maps it.
- [ ] **Step 2:** Run with `--filter "FullyQualifiedName~V2StashImportRunnerFixture"`. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run the filter plus `V2TpdbImportRunnerFixture` and `LegacyMigrationImportServiceFixture`. Expected: PASS. The service sees exactly one runner per state.
- [ ] **Step 5:** Commit `New: Import a v2 library onto StashDB scenes`.

### Task 8: Eros rebuild runner

**Files:**
- Create: `src/NzbDrone.Core/LegacyMigration/ErosRebuildImportRunner.cs`
- Modify: `src/NzbDrone.Core/LegacyMigration/ErosSeedImportRunner.cs` and `ILegacyImportRunner.cs`
- Test: `src/NzbDrone.Core.Test/LegacyMigration/ErosRebuildImportRunnerFixture.cs`, plus one new case in `ErosSeedImportRunnerFixture`.

**Interfaces:**
- **`ErosSeedImportRunner`:**
  - `Handles` becomes `SourceKind == Eros && !state.Rebuild`.
  - Its pass body moves to `public LegacyImportPass RunMovieSeeds(LegacyMigrationState state, ILegacyImportProgress progress, bool recordOutages)`, which never calls `progress.Total`.
  - `RunPass` calls `Total` and then `RunMovieSeeds`.
- **`LegacyImportPass.Combine(LegacyImportPass a, LegacyImportPass b)`** (static):
  - sums the counts;
  - takes the earliest `NextDue`;
  - `Outage = (a.Attempted == 0 || a.Outage) && (b.Attempted == 0 || b.Outage) && (a.Attempted + b.Attempted) > 0`.
- **`ErosRebuildImportRunner`:**
  - It takes `ErosSeedImportRunner` as a concrete constructor dependency.
  - `Handles`: `SourceKind == Eros && state.Rebuild`.
- **`RunPass`:**
  - `Total` = all studio, performer, scene and movie seeds. Resolved ones are advanced at once.
  - **Stages, in order:** studios, then performers, then scenes, then `RunMovieSeeds`.
  - The scene stage runs only when no studio seed is unresolved. Until then its scenes count as `Pending`.
  - Each stage uses the same due, outage and give-up handling as `ErosSeedImportRunner`, for its own seeds.
  - The stage results are combined with `Combine`.
- **Studio seed:**
  1. **Pick the target id:** `seed.LegacyTpdbId` when the target is TPDB and it is set (`Mapping`). Otherwise use `MapStudio(state's previous source, seed.LegacyForeignId, seed.Title)`. The previous source is the source that isn't `state.SceneSource`.
  2. **`None`:** a `NeedsAttention` row, "Studio not found on {target}", then Resolved.
  3. **Studio already in the library:** an Info `NeedsAttention` row, "Merged with {other title}", and the existing studio's settings are kept (Review Focus 3).
  4. **New studio:** add it unmonitored with `SearchOnAdd = false`, and **no catalog import**. Then apply the seed's `Monitored`, `MoviesMonitored`, `WhisparrMonitorNewItems`, `AfterDate`, `RootFolderPath`, `QualityProfileId` and `Tags`, and save.
  5. Set `NewForeignId` and mark the seed Resolved.
- **Performer seed:** the same steps with `MapPerformer`, `IAddPerformerService` and `IPerformerService`.
- **Scene seed:**
  1. **Studio:** look up the studio seed by `LegacyStudioForeignId`. Its `NewForeignId` (which may be null) names the studio.
  2. **Hash match** (only if `ImportFileId` is set). Send the `ImportFile` path and stored `OsHash` to `ILegacyHashMatcher`, batched per pass over all due scene seeds with files.
     - `TryLater` → transient.
     - A newly computed hash is saved through `IImportlFileService.SaveOsHashes`.
  3. **Catalog match:** when there is no hash match and the studio is known.
     - Load the studio's catalog once per pass by walking `IProvideMovieInfo.GetStudioScenePage` (use `IScenePageWalker`) into `SceneKey`s.
     - Then `FindExact(catalog, seed.Title, seed.ReleaseDate)`.
  4. **No match:** a `NeedsAttention` row with Subject `"{Title} ({ReleaseDate:yyyy-MM-dd})"` and Detail `"Legacy {previous} id {LegacyForeignId}"`. The file stays parked, and the seed is Resolved.
  5. **Match:**
     - `FindByForeignId` first. Otherwise add the scene with `Monitored = seed.Monitored`, `QualityProfileId`, `RootFolderPath`, `Tags`, `Path = seed.Path`, `AddOptions { SearchForMovie = false }`, through `AddMovies(…, false, refreshInline: true)`, as `ErosSeedImportRunner` does.
     - If an import file is present: `Link(movie, importFile.Path, f => FillFromSeed(f, seed))`, where `FillFromSeed` copies the `File*` columns through the same converters `V2SeriesImportRunnerBase.Fill` uses.
       - `Linked` or `AlreadyLinked` → remove the `ImportFiles` row.
       - Any other outcome → a `NeedsAttention` row, worded as in `ErosSeedImportRunner.LinkImportFile` (Review Focus 2).
     - Mark the seed Resolved.

- [ ] **Step 1:** Write the failing tests:
  - `handles_only_rebuild`, and in `ErosSeedImportRunnerFixture`, `does_not_handle_rebuild`.
  - `studio_uses_stored_tpdb_id_without_calling_mapper`
  - `studio_unmapped_reported`
  - `studio_added_without_catalog_then_settings_applied`: `ISceneCatalogImporter` is never called.
  - `two_seeds_one_studio_merge_reported` (Review Focus 3)
  - `scenes_wait_for_unresolved_studios`
  - `scene_linked_by_oshash_keeps_file_metadata`: the quality, release group and date added from the seed.
  - `scene_linked_by_catalog_title_and_date`
  - `scene_without_file_added_monitored_as_seed`
  - `scene_unmatched_reported_and_file_stays_parked`
  - `two_seeds_same_scene_second_reported` (Review Focus 2)
  - `restart_between_stages_no_duplicates` (Review Focus 1): run a pass, rebuild the runner, run again, and assert the add counts.
  - `movie_seeds_run_last_and_combine`
  - `combine_outage_rules`: a `[TestCase]` table.
- [ ] **Step 2:** Run with `--filter "FullyQualifiedName~ErosRebuildImportRunnerFixture|FullyQualifiedName~ErosSeedImportRunnerFixture"`. Expected: the new tests FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run the filter plus `LegacyMigrationImportServiceFixture`. Expected: PASS.
- [ ] **Step 5:** Commit `New: Rebuild an Eros library on a new scene source`.

### Task 9: Halt-page gating and full verification

**Files:**
- Modify: `frontend/src/Setup/SetupSourceForm.tsx` and `en.json`, but only if the form disables, hides or warns "not available yet" on v2 + StashDB or on an Eros source change. Check both, and remove any such gating. The "rebuilds from your files; much slower" note stays.

**Interfaces:** none new.

- [ ] **Step 1:** Read `SetupSourceForm.tsx` for any gating. Remove it if found. If none is found, the task is verification only.
- [ ] **Step 2:** Run the full Core suite on SQLite and on Postgres, then the Api, Common, Mono and Host projects, each separately. Expected: all green.
- [ ] **Step 3:** Run `yarn typecheck`, `yarn lint` and `yarn build` from the repo root. Expected: green, and tsc reports 0 errors.
- [ ] **Step 4:** If anything changed, commit `New: Offer StashDB scenes to v2 installs and a scene-source change to Eros installs on the setup page`.

### Task 10: Live check (controller, not a subagent)

Use the 6969 dev instance, with the stopped instance freed first and SFW mode on.

1. **v2 → StashDB on SQLite**, with the G-d v2 fixture copy:
   - the halt page offers StashDB without a refusal;
   - the preflight shows as the first step;
   - phase 2 completes;
   - files are linked in place;
   - monitored state matches v2 for the linked scenes;
   - nothing is queued as a grab;
   - the report shows the unmatched counts.
2. **Eros StashDB → TPDB on SQLite**, with the G-d Eros fixture copy:
   - studios and performers come back with the user's settings;
   - scenes link by hash or by title and date;
   - the files are untouched on disk (checksums of a sample);
   - the report lists the left-behind counts.
3. **Repeat both on Postgres.**
4. **Kill -9 mid-phase-2 on the Eros run and restart:** it completes with no duplicates.
5. **Legacy DB and `config.xml` hashes unchanged.** For Postgres, compare dumps with the `\restrict` lines stripped.
6. **Screenshots**, with SFW on, into the session scratchpad: the halt page with StashDB picked for v2, and the migration report after the Eros rebuild.

Record any findings as fix tasks before the final review.
