# Migration wizard G-d: phase 2, automation pause, report, health notice — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** After phase 1 leaves the migration state at `Phase2Pending`, a resumable library import runs inside the normal app. It covers two paths: v2 with scenes on TPDB (the seeded path), and the Eros mixed-movie seeds. Automation stays paused until the import completes. A health notice and a System → Migration Report page show the outcome.

**Architecture:**
- **The command.** One command, `LegacyMigrationImportCommand`, is queued on every startup while the state is incomplete. It dispatches to one phase runner per source:
  - `V2TpdbImportRunner` reads the G-c staging tables;
  - `ErosSeedImportRunner` reads G-b's `LegacyMovieSeeds`.
- **Shared pieces.** Both runners use:
  - a numeric-TPDB-ID resolver (Skyhook `tpdb/resolve`);
  - an in-place file linker;
  - a report writer;
  - a retry policy that turns repeated failures into "needs attention".
- **Pause and notice.** One gate service, `ILegacyMigrationGate`, answers "is automation paused". It is read at five hook points. A health check and a v5 API controller back the notice and the report page.

**Tech Stack:** C#/.NET 10, DryIoc auto-wiring, Dapper, FluentMigrator, NUnit + Moq + FluentAssertions, React 19 + TanStack Query (`useApiQuery`/`useApiMutation`).

**Spec:** `docs/superpowers/specs/2026-10-24-migration-wizard-design.md`. The relevant sections are "Phase 2", "Paused automation", "Progress in System → Tasks", "Resume" and "Migration report".

## Scope

**In:**
- Phase 2 for **v2 with scenes on TPDB**.
- Phase 2 for **Eros, same scene source, mixed movies**: the `LegacyMovieSeeds` rows and their `ImportFiles`.
- The automation pause.
- Requeue on startup, retry, and needs-attention handling.
- Command progress.
- The health notice, with dismissal.
- The report API and page.

**Out, owned by G-e:**
- v2 with scenes on StashDB.
- The Eros scene-source change: phase 1 seeding plus the phase 2 rebuild.
- The deferred G-c item that runs the Skyhook preflight before the port.

**Until G-e lands,** the halt page refuses v2 + StashDB with `SetupNotAvailableYet`, the same way G-b refuses an Eros source change (Task 7).

## Global Constraints

- **Edits:** use the Edit and Write tools only. Never sed, shell redirection or chained Bash. Run one plain command per Bash call, with absolute paths.
- **Commits:** on `develop` in `~/Git/whisparr-next`. Never push.
- **Attribution and naming:**
  - No AI attribution.
  - Use the plz12345 handle and `~/` paths.
  - No infrastructure details (hosts, proxy layout).
- **Commit prefixes:** `New:`, `Fix:` or `Chore:`.
- **Read-only legacy data.** Phase 2 never opens the legacy DB or `config.xml`. It reads only the v5 DB's staging and seed tables.
- **Disk:**
  - No step renames, moves or deletes a file on disk.
  - The linker only writes DB rows.
- **No automatic grabs while `LegacyMigrationState.Completed` is false.** User-started searches (`CommandTrigger.Manual`) are still allowed.
- **Wrong links are never silent.**
  - A Skyhook ID mapping (`tpdb/resolve`) links automatically.
  - A title search links only on a single exact match: the normalized title (`Parser.CleanMovieTitle`-equivalent `CleanTitle()`) and the year must both match.
  - Everything else becomes a `NeedsAttention` report row.
- **Postgres:** v5 `Id` columns are GENERATED ALWAYS. These tasks write through repositories and services, never with explicit IDs.
- **G-c staging rules:**
  - `TpdbSceneId = 0` means v2 had no TPDB ID.
  - A file row whose `Path` is a directory came from a NULL v2 `RelativePath`. It is flagged and never linked.
  - `Languages`, `Quality` and `IndexerFlags` are already v5 IDs.
- **v2 `MonitorNewItems`:** `0` = All, `1` = None.
- **New strings** go in `src/NzbDrone.Core/Localization/Core/en.json` (flat, PascalCase, alphabetical).
- **Tests:**
  - NUnit `*Fixture`, 80% coverage on new code.
  - Run each test project separately (Core, Api, Common, Mono, Host). A solution-wide run gives false failures.
  - Run Core on SQLite and on Postgres.
  - Frontend: `yarn typecheck`, `yarn lint` and `yarn build`, run from the repo root.

## Review Focus

1. **Restart mid-import.** A kill between two series resumes at the next series without duplicate scenes, files or report rows. Task 5 and Task 7 tests.
2. **Rescan races the linker.** The inline refresh of a new scene can link a v2 file by parser before the legacy link runs.
   - The same file on the same scene counts as already linked.
   - On a different scene, it becomes a `NeedsAttention` row and the existing link is left alone.
   - Task 4 test.
3. **Download finishes during the import.** It stays `ImportPending` in the client and imports after completion. Task 2 test.
4. **Skyhook down for the whole import.** Each series is retried five times, spread over 15 minutes or more. It then becomes `NeedsAttention` and counts as done, so the migration still completes and the pause lifts. Task 5 test.
5. **v2 file outside every root folder, or missing on disk.** It is never linked or parked. It gets a report row with the path. Task 4 test.

---

### Task 1: State, report and retry columns

**Files:**
- Create: `src/NzbDrone.Core/Datastore/Migration/307_add_legacy_import_retries.cs`
- Create: `src/NzbDrone.Core/LegacyMigration/LegacyMigrationReportRepository.cs`
- Create: `src/NzbDrone.Core/LegacyMigration/LegacyMigrationService.cs`
- Create: `src/NzbDrone.Core/LegacyMigration/Events/LegacyMigrationCompletedEvent.cs`
- Create: repositories for `LegacyV2Series`, `LegacyV2Episode`, `LegacyV2EpisodeFile` and `LegacyMovieSeed`, in `src/NzbDrone.Core/LegacyMigration/`. Skip any that already exist; check `TableMapping.cs` and the folder first.
- Modify: `LegacyMigrationState.cs`, `LegacyV2Series.cs`, `LegacyMovieSeed.cs`
- Test: `src/NzbDrone.Core.Test/LegacyMigration/LegacyMigrationServiceFixture.cs` (a `DbTest`)

**Interfaces:**
- **Migration 307 adds:**
  - `LegacyMigrationState.ReportDismissed` (bool, default false);
  - `LegacyV2Series.Attempts` (int, default 0) and `LegacyV2Series.LastAttemptAt` (DateTimeOffset, nullable);
  - the same two columns on `LegacyMovieSeeds`.
- **The models gain matching properties.** `LastAttemptAt` is a `DateTime?`.
- **Produces `ILegacyMigrationService`:**
  - `LegacyMigrationState GetCurrent()`: null when there is no row.
  - `bool IsIncomplete()`: true when a row exists and `Completed` is false. It is cached in memory and cleared by `MarkCompleted`.
  - `void SaveProgress(LegacyMigrationState state)`
  - `void MarkCompleted()`: sets `Completed`, `CompletedAt = UtcNow` and `Phase = "Done"`, then publishes `LegacyMigrationCompletedEvent`.
  - `void DismissReport()`
  - `void Report(LegacyReportCategory category, string area, string subject, string detail, int count = 1)`
  - `List<LegacyMigrationReportItem> GetReport()`
- **Produces** `ILegacyMigrationReportRepository : IBasicRepository<LegacyMigrationReportItem>`.

- [ ] **Step 1:** Write the failing tests:
  - `is_incomplete_false_without_state_row`
  - `is_incomplete_true_until_mark_completed`: asserts `Completed`, `CompletedAt` not null, `Phase == "Done"`, and that the event was published once.
  - `dismiss_report_persists`
  - `report_appends_item_with_count_default_1`
  - `migration_307_defaults`: inserts rows through the repositories and reads back `Attempts == 0`, `LastAttemptAt == null` and `ReportDismissed == false`.
- [ ] **Step 2:** Run `dotnet test src/NzbDrone.Core.Test/Whisparr.Core.Test.csproj --filter "FullyQualifiedName~LegacyMigrationServiceFixture"`. Expected: FAIL (types missing).
- [ ] **Step 3:** Implement the migration, models, repositories and service. Register any new tables in `TableMapping.cs`, following the 305/306 entries.
- [ ] **Step 4:** Run the same filter on SQLite and on Postgres. Expected: PASS.
- [ ] **Step 5:** Commit `New: Migration state service, report writer and retry columns for the library import`.

### Task 2: Automation pause gate

**Files:**
- Create: `src/NzbDrone.Core/LegacyMigration/LegacyMigrationGate.cs`
- Modify, at these hook points:
  - `src/NzbDrone.Core/Indexers/RssSyncService.cs`: `Execute(RssSyncCommand)`
  - `src/NzbDrone.Core/IndexerSearch/MoviesSearchService.cs`: `Execute(MissingMoviesSearchCommand)` and `Execute(CutoffUnmetMoviesSearchCommand)`
  - `src/NzbDrone.Core/Movies/MovieScannedHandler.cs`: `HandleScanEvents`, at the search-on-add push
  - `src/NzbDrone.Core/ImportLists/ImportListSyncService.cs`: `Execute(ImportListSyncCommand)`
  - `src/NzbDrone.Core/Download/DownloadProcessingService.cs`: the `ImportPending` branch of `Execute(ProcessMonitoredDownloadsCommand)`
- Test: `src/NzbDrone.Core.Test/LegacyMigration/LegacyMigrationGateFixture.cs`, plus one test per hook in the hook's existing fixture (create the fixture if there is none).

**Interfaces:**
- **Consumes:** `ILegacyMigrationService.IsIncomplete()` (Task 1).
- **Produces:** `ILegacyMigrationGate { bool AutomationPaused { get; } }`, which delegates to `IsIncomplete()`.

**Rules:**
- **RSS sync:** skipped whatever the trigger. Log at Info: "RSS sync is paused until the library migration completes".
- **Missing and cutoff searches:** skipped when `Trigger != CommandTrigger.Manual`.
- **Search on add:**
  - The `MoviesSearchCommand` is not pushed.
  - `AddOptions` is still cleared, so the search doesn't fire later. Adds made during the migration never search.
- **Import list sync:** skipped whatever the trigger.
- **Completed downloads:**
  - `_completedDownloadService.Import` isn't called while paused.
  - The download stays `ImportPending`, and the next run after completion imports it.
  - Failed-download processing still runs.

- [ ] **Step 1:** Write the failing tests:
  - `rss_sync_skipped_while_paused`: verifies the release fetch is never called.
  - `scheduled_missing_search_skipped_while_paused`
  - `manual_missing_search_runs_while_paused`
  - `search_on_add_not_pushed_while_paused_and_add_options_cleared`
  - `import_list_sync_skipped_while_paused`
  - `import_pending_download_not_imported_while_paused`: also asserts `ProcessFailed` still runs for a `FailedPending` item.
  - `each_hook_runs_normally_when_not_paused`: one assertion per hook.
- [ ] **Step 2:** Run with filters for each touched fixture. Expected: FAIL.
- [ ] **Step 3:** Implement the gate and the five early-outs.
- [ ] **Step 4:** Run the filters. Expected: PASS.
- [ ] **Step 5:** Commit `New: Pause RSS sync, scheduled searches, search on add, list sync and download import during the library migration`.

### Task 3: TPDB numeric ID resolver client

**Files:**
- Create: `src/NzbDrone.Core/MetadataSource/SkyHook/TpdbIdResolver.cs`
- Test: `src/NzbDrone.Core.Test/MetadataSource/SkyHook/TpdbIdResolverFixture.cs`

**Interfaces:**
- **Produces:**
  - `public enum TpdbIdKind { Scene, Site, Performer }`, sent lowercased as `"scene"`, `"site"` or `"performer"`.
  - `public class TpdbResolveResult { Dictionary<int, string> Ids; bool Pending; }`
  - `ITpdbIdResolver.Resolve(TpdbIdKind kind, IReadOnlyCollection<int> ids)`, returning `TpdbResolveResult`.
- **Behaviour:**
  - POST `tpdb/resolve` with `{ kind, ids }` through the same `_whisparrMetadata` request builder and `RouteConst` segment as `SkyHookProxy`.
  - IDs are sent in batches of at most **1000** and the maps are merged.
  - IDs ≤ 0 are dropped before sending.
  - A 200 answer returns the map. Unknown IDs are simply absent.
  - A 202, 429 or 503 answer sets `Pending = true` and keeps any partial map.
  - Any other status throws `HttpException`.
  - Callers treat `Pending` and exceptions as transient.

- [ ] **Step 1:** Write the failing tests, with a mocked `IHttpClient`:
  - `batches_by_1000`: 2,500 IDs make 3 calls.
  - `drops_non_positive_ids`
  - `merges_partial_maps`
  - `accepted_sets_pending_and_keeps_partial`
  - `server_error_throws`
- [ ] **Step 2:** Run with the `TpdbIdResolverFixture` filter. Expected: FAIL.
- [ ] **Step 3:** Implement it.
- [ ] **Step 4:** Run the filter. Expected: PASS.
- [ ] **Step 5:** Commit `New: Client for Skyhook TPDB numeric id resolve`.

### Task 4: In-place file linker and unmatched-file parking

**Files:**
- Create: `src/NzbDrone.Core/LegacyMigration/LegacyFileLinker.cs`
- Test: `src/NzbDrone.Core.Test/LegacyMigration/LegacyFileLinkerFixture.cs`

**Interfaces:**
- **Consumes:**
  - `IMediaFileService` (`Add`, `GetFilesByMovie`, `FilterExistingFiles`);
  - `IMovieService` (`UpdateMovie`);
  - `IDiskProvider` (`FileExists`, `FolderExists`);
  - `IRootFolderService`;
  - `IImportlFileService.Add`;
  - `ILegacyMigrationService.Report` (Task 1).
- **Produces:**
  - `public enum LinkOutcome { Linked, AlreadyLinked, LinkedElsewhere, Missing, IsDirectory, OutsideRootFolders }`
  - `ILegacyFileLinker.Link(Movie movie, string path, Action<MovieFile> fill)`, returning `LinkOutcome`.
  - `ILegacyFileLinker.Park(string path, int qualityProfileId, string area, string reason)`, returning `bool`.

**`Link` rules, checked in this order:**

| Condition | Outcome | Writes |
|---|---|---|
| The path is a directory | `IsDirectory` | none |
| The path doesn't exist | `Missing` | none |
| A `MovieFile` already has this path, on `movie` | `AlreadyLinked` | none |
| A `MovieFile` already has this path, on another movie | `LinkedElsewhere` | none |
| None of the above | `Linked` | see below |

**When the outcome is `Linked`:**
- If the file isn't under `movie.Path`, set `movie.Path` to the file's directory and save the movie.
- Create a `MovieFile` with:
  - `Path`;
  - `RelativePath` relative to `movie.Path`;
  - `MovieId`;
  - `MatchTier = MatchTier.Confirmed`;
  - `MediaInfo = null`, since the media-info backfill fills it.
- `fill` sets size, quality, languages, release group, scene name, original path, indexer flags and date added.
- Set `movie.MovieFileId`.

**`Park` rules:**
- It writes an `ImportFile` with:
  - the root folder found by `GetBestRootFolderPath`;
  - `RelativePath`;
  - `Name` = the file name;
  - `QualityProfileId`.
- It also writes an `UnmatchedFile` report row with `subject` = the path and `detail` = the reason, and returns true.
- A missing file, a directory, or a file outside every root folder writes a `NeedsAttention` row instead and returns false.
- If an `ImportFile` with that path already exists, it returns true and writes nothing, so a rerun stays idempotent.

- [ ] **Step 1:** Write the failing tests, one per outcome:
  - `links_file_in_place_with_confirmed_tier`: asserts no `IDiskTransferService` or `IDiskProvider` move, copy or delete is called. Use `Mocker.GetMock<IDiskProvider>().VerifyNoOtherCalls()` after the expected `FileExists`/`FolderExists`.
  - `repoints_movie_path_to_file_directory`
  - `directory_path_is_flagged`
  - `missing_file_is_flagged`
  - `already_linked_same_movie_is_noop`
  - `linked_to_other_movie_is_left_alone`
  - `park_writes_import_file_and_unmatched_report`
  - `park_outside_root_folders_reports_needs_attention`
  - `park_is_idempotent`
- [ ] **Step 2:** Run with the `LegacyFileLinkerFixture` filter. Expected: FAIL.
- [ ] **Step 3:** Implement it.
- [ ] **Step 4:** Run the filter. Expected: PASS.
- [ ] **Step 5:** Commit `New: In-place file linker for the library migration`.

### Task 5: Import command, runner dispatch, retry and requeue

**Files:**
- Create: `src/NzbDrone.Core/LegacyMigration/LegacyMigrationImportCommand.cs`
- Create: `src/NzbDrone.Core/LegacyMigration/LegacyMigrationImportService.cs`
- Create: `src/NzbDrone.Core/LegacyMigration/ILegacyImportRunner.cs`
- Create: `src/NzbDrone.Core/LegacyMigration/LegacyRetryPolicy.cs`
- Test: `src/NzbDrone.Core.Test/LegacyMigration/LegacyMigrationImportServiceFixture.cs`
- Test: `src/NzbDrone.Core.Test/LegacyMigration/LegacyRetryPolicyFixture.cs`

**Interfaces:**
- **Consumes:**
  - `ILegacyMigrationService` (Task 1);
  - `IReportCommandProgress.Report(string phase, long done, long total, double percent)`;
  - `IManageCommandQueue.Push`;
  - `ISkyhookPacer` (`UtcNow`, `Sleep`) as the clock and delay seam.
- **Produces:**
  - `LegacyMigrationImportCommand : Command`, with `SendUpdatesToClient => true` and `UpdateScheduledTask => false`.
  - **`ILegacyImportRunner`:**
    - `bool Handles(LegacyMigrationState state)`;
    - `LegacyImportPass RunPass(LegacyMigrationState state, ILegacyImportProgress progress)`.
  - `LegacyImportPass { int Pending; int WaitingRetry; }`. Pending counts items still to do. WaitingRetry counts items that failed transiently this pass.
  - **`ILegacyImportProgress`:**
    - `void Total(long total)`;
    - `void Advance(string phase, long by = 1)`.

    The service owns the counter and forwards to `IReportCommandProgress`, with percent = done/total×100.
  - **`LegacyRetryPolicy`:**
    - `const int MaxAttempts = 5`;
    - `static TimeSpan DelayBefore(int attempt)`: attempt 1 → 1 min, 2 → 2, 3 → 4, 4 → 8;
    - `bool ShouldGiveUp(int attempts)`: true when `attempts >= MaxAttempts`.

    On a transient failure, a runner sets `Attempts++` and `LastAttemptAt = UtcNow` on the item through its repository ("record a failure" in Tasks 6 and 7). When `ShouldGiveUp(Attempts)` is true, it writes a `NeedsAttention` row and marks the item done instead. A parked file still gets its D-order candidates later from the existing `ImportFileMatchService` schedule; phase 2 doesn't call it.

**`Execute` behaviour:**
1. Read the state. If there's no state, or it's `Completed`, return.
2. Pick the single runner whose `Handles` is true.
   - None found: log an Error.
   - Several: write a `NeedsAttention` row with area `Migration` and return **without** completing.
3. Loop `RunPass`:
   - `Pending == 0`: call `MarkCompleted()` and return.
   - `WaitingRetry > 0`: sleep `DelayBefore` the smallest attempt among the waiting items (the runner reports it through `LegacyImportPass.NextAttempt`), then pass again.
   - `Pending > 0` with nothing waiting is a runner bug: log an Error and return without completing.

**`IHandle<ApplicationStartedEvent>`:** pushes `LegacyMigrationImportCommand` when `IsIncomplete()`.

- [ ] **Step 1:** Write the failing tests, using a fake runner:
  - `queues_command_on_startup_when_incomplete`
  - `does_not_queue_when_completed_or_absent`
  - `completes_when_runner_reports_nothing_pending`
  - `sleeps_policy_delay_then_retries_waiting_items`: asserts `ISkyhookPacer.Sleep(1 minute)`.
  - `never_completes_while_items_pending`
  - `no_runner_logs_and_does_not_complete`
  - `progress_reports_real_percentage`: total 4, advance 1 → 25.
  - Retry policy: `delays_are_1_2_4_8_minutes` and `gives_up_at_5`.
- [ ] **Step 2:** Run with the fixture filters. Expected: FAIL.
- [ ] **Step 3:** Implement it. Add `NextAttempt` (int) to `LegacyImportPass`.
- [ ] **Step 4:** Run the filters. Expected: PASS.
- [ ] **Step 5:** Commit `New: Resumable library migration command`.

### Task 6: Eros mixed-movie seed runner

**Files:**
- Create: `src/NzbDrone.Core/LegacyMigration/ErosSeedImportRunner.cs`
- Test: `src/NzbDrone.Core.Test/LegacyMigration/ErosSeedImportRunnerFixture.cs`

**Interfaces:**
- **Consumes:**
  - `ILegacyImportRunner`, `LegacyRetryPolicy` and `ILegacyImportProgress` (Task 5);
  - `ILegacyFileLinker` (Task 4);
  - the `LegacyMovieSeed` repository (Task 1);
  - `ISearchForNewMovie.SearchForNewMovie(string)`;
  - `IAddMovieService.AddMovie`;
  - `IMovieService.FindByForeignId`;
  - `IImportlFileService` (`Get`, `Remove`).
- **`Handles`:** `state.SourceKind == LegacyKind.Eros`.

**Per unresolved seed:**
1. **Find the movie.**
   - Chosen movie source TMDB and `TmdbId > 0`: search `tmdb:{TmdbId}` and accept the single result whose `TmdbId` matches.
   - Otherwise: search `"{Title} {Year}"` and accept only a single result whose `CleanTitle()` equals the seed title's `CleanTitle()` and whose `Year` equals the seed's year.
2. **No match**, or several matches: write a `NeedsAttention` row with area `Movies`, subject = the title and year, and detail = the legacy foreign ID. The seed's `ImportFile` stays for manual import. Mark the seed `Resolved`.
3. **A match:**
   - If `FindByForeignId` already has the movie, reuse it.
   - Otherwise call `AddMovie` with the seed's `Monitored`, `QualityProfileId`, `RootFolderPath`, `Tags` (from JSON) and `AddOptions.SearchForMovie = false`.
   - If `ImportFileId` is set, link that `ImportFile`'s path with `fill` taking only size and `DateAdded = UtcNow`. Quality is parsed later by the rescan.
   - `Linked`, `AlreadyLinked`: remove the `ImportFile`.
   - Any other outcome: write a `NeedsAttention` row and keep the `ImportFile`.
   - Mark the seed `Resolved`.
4. **Transient failure** (`HttpException`, `WebException`, `TimeoutException`): `RecordFailure`, and give up at 5 attempts.
5. **Progress:** total = all seeds; advance per resolved seed. The phase label is `"Movies"`.

- [ ] **Step 1:** Write the failing tests:
  - `tmdb_seed_added_and_file_linked_and_import_file_removed`
  - `title_search_single_exact_match_links`
  - `title_search_two_results_needs_attention_keeps_import_file`
  - `year_mismatch_needs_attention`
  - `existing_movie_reused_not_added_twice`
  - `transient_error_records_attempt_and_stays_pending`
  - `fifth_failure_gives_up_as_needs_attention`
  - `resolved_seeds_are_skipped_on_rerun`
  - `added_movie_never_searches`
- [ ] **Step 2:** Run with the fixture filter. Expected: FAIL.
- [ ] **Step 3:** Implement it.
- [ ] **Step 4:** Run the filter. Expected: PASS.
- [ ] **Step 5:** Commit `New: Re-identify Eros movies from the other metadata source after migration`.

### Task 7: v2 TPDB seeded runner

**Files:**
- Create: `src/NzbDrone.Core/LegacyMigration/V2TpdbImportRunner.cs`
- Modify: `src/NzbDrone.Core/Datastore/Migration/Setup/SetupWizardService.cs` `Validate`, to refuse v2 when `choice.SceneSource != SceneMetadataType.Tpdb` with `SetupErrorKey.SetupNotAvailableYet` and the message "Migrating Whisparr v2 to StashDB scenes isn't available yet. Your existing data has not been changed."
- Test: `src/NzbDrone.Core.Test/LegacyMigration/V2TpdbImportRunnerFixture.cs`
- Test: add `v2_with_stashdb_refused_not_available_yet` to the existing `SetupWizardServiceFixture`.

**Interfaces:**
- **Consumes:**
  - `ITpdbIdResolver` (Task 3);
  - `ILegacyFileLinker` (Task 4);
  - `ILegacyImportRunner`, `LegacyRetryPolicy` and `ILegacyImportProgress` (Task 5);
  - the staging repositories (Task 1);
  - `IAddStudioService.AddStudio`;
  - `IStudioService` (`FindByForeignId`, `Update`);
  - `ISceneCatalogImporter.ImportScenes(ownerTitle, fetchPage, buildStub, keepGoing)`;
  - `IProvideMovieInfo.GetStudioScenePage`;
  - `IAddMovieService.AddMovies(list, true, refreshInline: true)`;
  - `IMovieService.FindByForeignId`.
- **`Handles`:** `state.SourceKind == LegacyKind.V2 && state.SceneSource == SceneMetadataType.Tpdb`.
- **Progress:** total = undone series + undone files. Phases are labelled `"Studios"` and `"Files"`.

**Per series that isn't done, ordered by `LegacyId`:**
1. **Resolve the site.** Call `Resolve(Site, [TpdbSiteId])`.
   - Unknown ID, or `TpdbSiteId == 0`: write a `NeedsAttention` row (area `Studios`, subject = the title). Park every not-done file of the series with reason "Studio not found on TPDB", then mark the series and its files done.
   - Pending or an exception: `RecordFailure`, then continue to the next series.
2. **Resolve the scenes.** Call `Resolve(Scene, ids)` for the series' episodes with `TpdbSceneId > 0`. Pending or an exception: `RecordFailure`, then continue.
3. **Create the studio**, or reuse it with `FindByForeignId(uuid)`. Use:
   - `ForeignId = uuid`;
   - `Monitored = false`, `MoviesMonitored = false` and `SearchOnAdd = false`, so the queued `RefreshStudiosCommand` only refreshes metadata;
   - `RootFolderPath`, `QualityProfileId` and `Tags` from staging.
4. **Import the catalog.** Call `ImportScenes` with `keepGoing = () => true`. `buildStub(uuid)` returns a `Movie` with:
   - `ForeignId = uuid`;
   - `Path` = the series `Path`;
   - `RootFolderPath`, `QualityProfileId` and `Tags` from the series;
   - `AddOptions = { SearchForMovie = false, AddMethod = AddMovieMethod.Studio }`;
   - `Monitored` = the matched episode's `Monitored` when the UUID maps to a v2 episode, else `MonitorNewItems == 0`.
5. **Add missing scenes.** Episodes with a resolved UUID that `FindByForeignId` still doesn't find are added in one `AddMovies` call, built by the same `buildStub`.
6. **Link the files.** For each not-done file of the series, take the episodes with `LegacyEpisodeFileId == file.LegacyId`.

   | Situation | Action |
   |---|---|
   | No episode, `TpdbSceneId == 0`, or no movie found | `Park` with reason "No TPDB scene link in v2" |
   | Otherwise | `Link(movie, file.Path, fill)` |

   `fill` copies `Size`, `Quality`, `Languages`, `ReleaseGroup`, `SceneName`, `OriginalFilePath`, `IndexerFlags` and `DateAdded`. Parse JSON with the same converters the repositories use.

   | Link outcome | Report row (area `Files`, subject = path) |
   |---|---|
   | `Linked`, `AlreadyLinked` | none |
   | `LinkedElsewhere` | `NeedsAttention`, detail "Already linked to another scene; v2 had TPDB scene {id}" |
   | `Missing` | `NeedsAttention`, detail "File not found" |
   | `IsDirectory` | `NeedsAttention`, detail "v2 stored no file name" |

   A file linked to several episodes (a multi-episode file) links to the first by `LegacyId`, and writes a `NeedsAttention` row listing the others.

   Mark each file done and advance the progress.
7. **Apply the v2 studio settings.** Set `Monitored` = series `Monitored`, `WhisparrMonitorNewItems = MonitorNewItems == 0`, and `SearchOnAdd = false`, then `Update`. Mark the series and its episodes done, and advance.
8. **Files of unknown series.** After the loop, park any not-done file whose `LegacySeriesId` has no staging series, with reason "No series in v2", and mark it done.
9. **Pass result.** `Pending` = series not done, plus files not done. `WaitingRetry` = series that failed this pass.

- [ ] **Step 1:** Write the failing tests:
  - `resolves_site_and_creates_studio_unmonitored_then_applies_v2_settings`
  - `catalog_stub_copies_episode_monitored_and_defaults_new_items_from_monitor_new_items`: one UUID maps to a monitored episode, one to an unmonitored episode, one isn't in v2 with `MonitorNewItems` 0, and one isn't in v2 with `MonitorNewItems` 1.
  - `stub_path_is_series_folder_and_never_searches`
  - `episode_missing_from_catalog_added_individually`
  - `file_links_to_episode_scene_in_place`
  - `tpdb_scene_id_zero_parks_file`
  - `unknown_site_parks_all_files_and_reports_studio`
  - `directory_file_row_reported_never_linked`
  - `multi_episode_file_links_first_and_reports_rest`
  - `resolve_pending_records_attempt_and_leaves_series_pending`
  - `rerun_after_kill_skips_done_series_and_creates_no_duplicates`: run a pass, mark half done, run again, then assert the `AddStudio`/`AddMovies`/`MediaFileService.Add` call counts.
  - `orphan_file_without_series_parked`
- [ ] **Step 2:** Run with the fixture filter and the `SetupWizardServiceFixture` filter. Expected: FAIL.
- [ ] **Step 3:** Implement it.
- [ ] **Step 4:** Run both filters. Expected: PASS.
- [ ] **Step 5:** Run a DB-backed smoke check: `V2TpdbImportRunnerFixture` gets one `DbTest`-based test, `db_backed_pass_against_fixture_staging`. It loads `src/NzbDrone.Core.Test/Files/LegacyV2/v2-fresh-221.sql` through G-c's `V2LibrarySnapshot` into staging, then runs a pass with the resolver, page source and disk provider mocked. Assert:
  - every staging row is `Done`;
  - the report has no duplicate rows.

  Run it on SQLite and Postgres.
- [ ] **Step 6:** Commit `New: Library migration for v2 installs with TPDB scenes`.

### Task 8: Health notice

**Files:**
- Create: `src/NzbDrone.Core/HealthCheck/Checks/LegacyMigrationCheck.cs`
- Modify: `src/NzbDrone.Core/HealthCheck/HealthCheck.cs`, adding the `HealthCheckReason` members `LegacyMigrationInProgress` and `LegacyMigrationReport` in alphabetical position.
- Modify: `src/NzbDrone.Core/Localization/Core/en.json`
- Test: `src/NzbDrone.Core.Test/HealthCheck/Checks/LegacyMigrationCheckFixture.cs`

**Interfaces:**
- **Consumes:** `ILegacyMigrationService` (Task 1).
- **Checked on** `[CheckOn(typeof(LegacyMigrationCompletedEvent))]` as well as the regular schedule.

| State | Result |
|---|---|
| No state row | Ok |
| Incomplete | **Notice**, `LegacyMigrationInProgress`, key `LegacyMigrationInProgressHealthCheckMessage` = "Library migration in progress, see System → Tasks. Searches and other metadata requests are slower until it finishes." |
| Completed, not dismissed | **Notice**, `LegacyMigrationReport`, key `LegacyMigrationReportHealthCheckMessage` = "Library migration finished. Review the migration report for anything that didn't carry over." |
| Completed and dismissed | Ok |

- The completed notice's `WikiUrl` is `new HttpUri("/system/migration-report")`. The frontend `Link` treats a URL with no scheme as an in-app route.
- The in-progress notice's `WikiUrl` is `/system/tasks`.

- [ ] **Step 1:** Write the failing tests:
  - `ok_without_state`
  - `in_progress_notice_links_tasks`
  - `completed_notice_links_report`: asserts `WikiUrl.FullUri == "/system/migration-report"`.
  - `ok_after_dismiss`
- [ ] **Step 2:** Run with the fixture filter. Expected: FAIL.
- [ ] **Step 3:** Implement it. If `HttpUri` changes a relative path, adjust the construction until the `FullUri` assertion holds; the test is the contract.
- [ ] **Step 4:** Run the filter. Expected: PASS.
- [ ] **Step 5:** Commit `New: Health notice for the library migration and its report`.

### Task 9: Migration report API

**Files:**
- Create: `src/Whisparr.Api.V5/System/MigrationReport/MigrationReportController.cs`
- Create: `src/Whisparr.Api.V5/System/MigrationReport/MigrationReportResource.cs`
- Test: `src/NzbDrone.Api.Test/v5/MigrationReportControllerFixture.cs` (create the `v5` folder beside the existing `v3`)

**Interfaces:**
- **Consumes:** `ILegacyMigrationService` (Task 1).
- **`[V5ApiController("system/migrationreport")]`:**
  - `GET` returns `MigrationReportResource`, or 404 when there is no state row.
  - `POST dismiss` calls `DismissReport()` and returns 200. It returns 404 when there is no state.
- **`MigrationReportResource`:**
  - `string SourceKind`;
  - `string SceneSource`;
  - `string MovieSource`;
  - `bool Completed`;
  - `DateTime? StartedAt`;
  - `DateTime? CompletedAt`;
  - `string BackupPath`;
  - `bool Dismissed`;
  - `List<MigrationReportItemResource> Items`. Each item has `Category` (string: the enum name), `Area`, `Subject`, `Detail` and `Count`.
  - Items are ordered by category, area, then subject.

- [ ] **Step 1:** Write the failing tests:
  - `get_returns_404_without_state`
  - `get_maps_state_and_items_ordered`
  - `dismiss_calls_service`
- [ ] **Step 2:** Run with the fixture filter on the Api test project. Expected: FAIL.
- [ ] **Step 3:** Implement it.
- [ ] **Step 4:** Run the filter. Expected: PASS.
- [ ] **Step 5:** Commit `New: Migration report API`.

### Task 10: System → Migration Report page

**Files:**
- Create: `frontend/src/System/MigrationReport/MigrationReport.tsx`
- Create: `frontend/src/System/MigrationReport/useMigrationReport.ts`
- Create: `frontend/src/System/MigrationReport/MigrationReport.module.css` (only if needed)
- Create: `frontend/src/typings/MigrationReport.ts`
- Modify: `frontend/src/App/AppRoutes.tsx`, adding `/system/migration-report`.
- Modify: `frontend/src/Components/Page/Sidebar/PageSidebar.tsx`, adding a System child "Migration Report" after Events. Show it only when the query returned data, not on a 404.
- Modify: `src/NzbDrone.Core/Localization/Core/en.json`

**Interfaces:**
- **Consumes:** `GET /system/migrationreport` and `POST /system/migrationreport/dismiss` (Task 9), through `useApiQuery`/`useApiMutation` as in `System/Backup/useBackups.ts`.

**Page:**
- **Summary:**
  - from → to sources, and whether it's complete;
  - start and finish times;
  - the backup path.
- **One table section per category** (Skipped settings, Dropped tokens, Left behind, Unmatched files, Needs attention), with columns Area, Subject, Detail and Count. An empty category is hidden.
- **Unmatched files** has a link to `/add/import`, labelled "Manual import".
- **"Download" button:**
  - builds a CSV client-side (header `Category,Area,Subject,Detail,Count`, RFC 4180 quoting);
  - saves it as `whisparr-migration-report.csv` through a Blob URL.
- **"Dismiss notice" button:** calls dismiss, then invalidates the report query and `/health`. It is disabled while the migration is incomplete or already dismissed.
- **New en.json keys:**
  - `MigrationReport`
  - `MigrationReportDismiss`
  - `MigrationReportDownload`
  - `MigrationReportInProgress`
  - `MigrationReportManualImport`
  - `MigrationReportSummary`
  - one heading key per category: `MigrationReportSkippedSettings`, `MigrationReportDroppedTokens`, `MigrationReportLeftBehind`, `MigrationReportUnmatchedFiles`, `MigrationReportNeedsAttention`
- **Code style:** props `Readonly<T>`, and per-method lodash imports if lodash is used.

- [ ] **Step 1:** Implement the page, hook, typings, route, sidebar entry and keys.
- [ ] **Step 2:** Run `yarn typecheck`, `yarn lint` and `yarn build` from `~/Git/whisparr-next`. Expected: tsc reports 0 errors, lint is clean, and the build succeeds.
- [ ] **Step 3:** Commit `New: System → Migration Report page`.

### Task 11: Full verification and live check (controller-run, not a subagent)

- [ ] **Step 1:** Run each test project separately: Core on SQLite, Core on Postgres, Api, Common, Mono and Host. Every one must pass.
- [ ] **Step 2:** Stop any instance on 6969. Then publish the osx-arm64 build and rsync the UI.
- [ ] **Step 3: v2 path, live.** Use a copy of the scratchpad v2 data (`whisparr2.db` + `config.xml`) as AppData, and migrate with TPDB/TPDB. Check that:
  - System → Tasks shows the import with a percentage;
  - studios appear with v2 monitored state;
  - files link at their v2 paths, and `find` shows no change in mtime or path;
  - the queue and history show no grabs, and the RSS log line says paused;
  - the import completes, and the health notice switches to the report link;
  - the report page lists the misses, Download saves the CSV, and Dismiss clears the notice;
  - the legacy DB checksum is unchanged.
- [ ] **Step 4: restart mid-import.** Kill the app during "Studios" and restart. The import resumes, with no duplicate studios or scenes, and the progress continues.
- [ ] **Step 5: Eros mixed movies, live.** Use an Eros DB with mixed movies, keep StashDB, and switch the movie source. Seeds resolve or show as needs-attention, and their files are linked in place.
- [ ] **Step 6: Postgres.** Repeat step 3 against the scratchpad v2 Postgres config.
- [ ] **Step 7: Screenshots** of the report page and the health notice, with SFW mode on. Save them under the scratchpad and give their full paths.
- [ ] **Step 8:** Update the memory note with what landed and what's deferred, and record that G-e remains.
