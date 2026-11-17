# Covers for Bulk-Added Items Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Items added through `IRefreshNewScenes.RefreshNewScenes` get their covers downloaded in the background, without blocking the caller, re-fetching metadata, or adding per-item disk checks.

**Architecture:** `MediaCoverService` gains an unbounded in-memory backlog of movie ids beside its existing bounded queue. Bulk callers add ids and return straight away. Workers drain the backlog only when the bounded queue is empty and the migration gate is open, load each movie by id from the database, and download its covers without an existence check.

**Tech Stack:** C# / .NET 10, NUnit + Moq (AutoMoq) + FluentAssertions, DryIoc auto-wiring (no registration needed).

**Spec:** `docs/superpowers/specs/2026-11-16-bulk-add-covers-design.md`

## Global Constraints

- Repo `~/Git/whisparr-next`. Branch `fix/bulk-add-covers` off `origin/develop` with `git switch --no-track -c fix/bulk-add-covers origin/develop` (a tracking branch pushes to `develop`). `develop` is protected: everything lands through a PR.
- Edit files with the Edit/Write tools, never `sed`, shell redirection or heredocs.
- `QueueNewMovieCovers` never waits and does no I/O.
- Bulk work adds no `FileExists`, no `.source` read and no directory walk. The download and its write are the only disk work.
- No metadata refresh to get covers. `RefreshNewScenes` still publishes no `MovieUpdatedEvent`.
- The existing bounded queue (`MovieCoverQueueCapacity`, waiting producers, `AlreadyExists` check) is unchanged for the `MovieUpdatedEvent` path.
- The backlog is in memory only and is dropped at shutdown.
- Commit subjects use the repo's `Fix:` / `Chore:` Title Case prefixes. No AI attribution trailers.
- Before handover, fix easy Sonar issues in every touched file.

## Review Focus

- Shutdown with thousands of ids in the backlog: workers must exit within the existing shutdown deadline, not drain the backlog first. Test in Task 1.
- A movie deleted while its backlog download is running: its cover folder must still be removed afterwards (existing tombstone path). Test in Task 1.
- `QueueNewMovieCovers` called before `ApplicationStartedEvent` or after shutdown: ignored, no throw, nothing queued. Test in Task 1.
- An empty id list: no-op. Test in Task 1.
- A refreshed item whose metadata has no images (a failed refresh leaves the add-time stub): no download, no error logged. Test in Task 1.

---

### Task 1: Backlog intake and draining in MediaCoverService

**Files:**
- Create: `src/NzbDrone.Core/MediaCover/IQueueNewMovieCovers.cs`
- Modify: `src/NzbDrone.Core/MediaCover/MediaCoverService.cs` (constructor, `ProcessMovieCoverQueue` ~line 985, `EnsureCovers(Movie)` ~line 595, `PurgeDeletedMovieCovers` ~line 1110, `MovieCoverQueueTestSeam`)
- Test: `src/NzbDrone.Core.Test/MediaCoverTests/MediaCoverServiceFixture.cs`

**Interfaces:**
- Produces: `public interface IQueueNewMovieCovers { void QueueNewMovieCovers(IEnumerable<int> movieIds); }` in namespace `NzbDrone.Core.MediaCover`, implemented by `MediaCoverService`.
- Produces (test seam): `internal int NewMovieCoverBacklogCount` on `MovieCoverQueueTestSeam`.
- Consumes: `IMovieService.GetMovie(int)` (joined single-row query; throws `ModelNotFoundException` when the row is gone). Inject `IMovieService` into the `MediaCoverService` constructor; it has no dependency back on media covers.

- [ ] **Step 1: Write the failing tests.** Follow the fixture's existing threading style (block workers with an `AlreadyExists` or `DownloadFile` callback and a `ManualResetEventSlim`, wait on `MediaCoversUpdatedEvent` with a `CountdownEvent`, start with `Subject.Handle(new ApplicationStartedEvent())`). Give each movie a poster `RemoteUrl` and have `IMovieService.GetMovie(id)` return it.
  - `should_queue_new_movie_covers_without_blocking_when_the_update_queue_is_full`: block all workers, fill the bounded queue to `MovieCoverQueueCapacity` from another thread (as `should_restore_queue_capacity_after_multiple_queued_movies_are_deleted` does), then `QueueNewMovieCovers(Enumerable.Range(10_000, 1000))` returns within 1 s and `NewMovieCoverBacklogCount == 1000`.
  - `should_download_new_movie_covers_without_checking_the_disk`: queue one id, wait for `MediaCoversUpdatedEvent`. `IHttpClient.DownloadFile(remoteUrl, <movie>/poster.jpg)` called once. `ICoverExistsSpecification.RecordSource` called once. `ICoverExistsSpecification.AlreadyExists` and `IDiskProvider.FileExists(any)` never called.
  - `should_process_movie_updates_before_new_movie_covers`: block every worker, queue backlog id A, then `Handle(new MovieUpdatedEvent(B))`, release. Record processing order through the `DownloadFile` callback. B is processed before A.
  - `should_download_a_duplicated_new_movie_once`: `QueueNewMovieCovers(new[] { 5, 5 })` then `QueueNewMovieCovers(new[] { 5 })` while workers are blocked. `GetMovie(5)` called once.
  - `should_skip_a_new_movie_already_queued_by_an_update`: workers blocked, `Handle(new MovieUpdatedEvent(movie 7))`, then `QueueNewMovieCovers(new[] { 7 })`, release. `DownloadFile` called once for movie 7's poster.
  - `should_skip_a_new_movie_that_no_longer_exists`: `GetMovie(9)` throws `ModelNotFoundException`. No `DownloadFile`, no error logged (`ExceptionVerification.ExpectedErrors(0)` is implicit), worker keeps serving the next id.
  - `should_drop_new_movie_covers_for_deleted_movies`: workers blocked, queue ids 11 and 12, `HandleAsync(new MoviesDeletedEvent(<movie 11>, …))`, release. `GetMovie(11)` never called, `GetMovie(12)` called once.
  - Review Focus: `should_stop_within_the_shutdown_deadline_with_a_large_new_movie_backlog`: one blocked worker, queue 5000 ids, set `ShutdownTimeout` to 2 s, release the block and send `ApplicationShutdownRequested`. `WorkerCountAlive` reaches 0 within 3 s, and `GetMovie` was called far fewer than 5000 times (assert `< 100`).
  - Review Focus: `should_remove_covers_of_a_new_movie_deleted_while_downloading`: mirror `should_remove_covers_recreated_by_active_work_after_movie_deletion` with the movie entering through `QueueNewMovieCovers`. `DeleteFolder(<movie folder>, true)` called after the download finishes.
  - Review Focus: `should_ignore_new_movie_covers_before_start_and_after_shutdown`: `QueueNewMovieCovers(new[] { 1 })` before `ApplicationStartedEvent` and again after `ApplicationShutdownRequested`. No throw, `NewMovieCoverBacklogCount == 0`, `GetMovie` never called.
  - Review Focus: `should_ignore_an_empty_new_movie_list`: `QueueNewMovieCovers(Array.Empty<int>())` → `NewMovieCoverBacklogCount == 0`.
  - Review Focus: `should_not_download_for_a_new_movie_without_images`: `GetMovie` returns a movie with `Images` empty. No `DownloadFile`, `MediaCoversUpdatedEvent` published with `Updated == false`, no errors logged.

- [ ] **Step 2: Run the tests and see them fail**

Run: `dotnet test src/NzbDrone.Core.Test/Whisparr.Core.Test.csproj --disable-build-servers --filter "FullyQualifiedName~MediaCoverServiceFixture"`
Expected: FAIL to compile (`IQueueNewMovieCovers` / `QueueNewMovieCovers` / `NewMovieCoverBacklogCount` don't exist).

- [ ] **Step 3: Implement `IQueueNewMovieCovers` and `MediaCoverService.QueueNewMovieCovers(IEnumerable<int> movieIds)`**
  - State, guarded by `_movieCoverQueueLock`: `Queue<int> _newMovieCoverBacklog` and `HashSet<int> _newMovieCoverBacklogIds`.
  - Intake: if the lifecycle state isn't `Running`, log Debug and return. Add each id not already in the set. Release `_queuedMovieCovers` once per added id. Never touch `_movieCoverQueueSlots`.
  - Worker (`ProcessMovieCoverQueue`), on each wake, under the lock, in this order:
    1. The bounded queue has an entry: existing path, unchanged.
    2. The bounded queue is empty and the state isn't `Running`: return (existing). This drops the backlog at shutdown.
    3. The backlog has an id: dequeue it and remove it from the set. If `_pendingMovieCovers`, `_activeMovieCoverWorkerCounts` or `_deletedMovieCoversWithActiveWork` holds that id, `continue`. Otherwise increment `_activeMovieCoverWorkerCounts[id]`, then outside the lock: `GetMovie(id)` (on `ModelNotFoundException`, log Debug and skip), `EnsureCovers(movie, coversKnownMissing: true)`, publish `MediaCoversUpdatedEvent(movie, updated)`. `CompleteMovieCoverProcessing(id)` runs in a `finally`, as on the existing path.
    4. Otherwise `continue`.
  - Accounting: there is one `_queuedMovieCovers` permit per id added at intake, and none for a duplicate. A permit left over after an id is purged or skipped wakes a worker that finds nothing and continues. That's harmless, so nothing reconciles it.
  - `EnsureCovers(Movie movie, bool coversKnownMissing = false)`: when true, skip `_coverExistsSpecification.AlreadyExists` and treat every cover as missing. Everything else stays as it is, including `RecordSource`, `_coverExistsCache.Set` and the forced resize.
  - `PurgeDeletedMovieCovers`: also remove deleted ids from both backlog collections. Don't drain `_queuedMovieCovers` for them: an extra wake-up finds nothing and continues, while draining could take a permit that belongs to a bounded-queue entry.

- [ ] **Step 4: Run the tests and see them pass**

Run: `dotnet test src/NzbDrone.Core.Test/Whisparr.Core.Test.csproj --disable-build-servers --filter "FullyQualifiedName~MediaCoverTests"`
Expected: PASS, all existing `MediaCoverServiceFixture` tests included. Run it three times: the fixture is threaded, and a flaky pass is a failure.

- [ ] **Step 5: Commit**

```bash
git add src/NzbDrone.Core/MediaCover/IQueueNewMovieCovers.cs src/NzbDrone.Core/MediaCover/MediaCoverService.cs src/NzbDrone.Core.Test/MediaCoverTests/MediaCoverServiceFixture.cs
git commit -m "Fix: Queue Covers for New Items Without Blocking or Disk Checks"
```

### Task 2: Hold the backlog while a migration runs

**Files:**
- Modify: `src/NzbDrone.Core/MediaCover/MediaCoverService.cs`
- Test: `src/NzbDrone.Core.Test/MediaCoverTests/MediaCoverServiceFixture.cs`

**Interfaces:**
- Consumes: `ILegacyMigrationGate.AutomationPaused` (`NzbDrone.Core.LegacyMigration`; cached, cheap to read; `LegacyMigrationService` has no dependency back on media covers). `LegacyMigrationCompletedEvent` (`NzbDrone.Core.LegacyMigration.Events`, no payload).
- Produces: `MediaCoverService : IHandle<LegacyMigrationCompletedEvent>`.

- [ ] **Step 1: Write the failing tests**
  - `should_hold_new_movie_covers_while_the_migration_is_running`: gate returns `true`; start; `QueueNewMovieCovers(new[] { 21, 22 })`; after 500 ms `GetMovie` was never called and `NewMovieCoverBacklogCount == 2`. Then gate returns `false`, `Handle(new LegacyMigrationCompletedEvent())`, and both ids are processed (wait on `MediaCoversUpdatedEvent` ×2).
  - `should_process_movie_updates_while_the_migration_is_running`: gate `true`; `Handle(new MovieUpdatedEvent(movie))` is processed as today.
  - `should_resume_new_movie_covers_queued_before_the_migration_started`: gate `false`; block workers; queue 3 ids; gate flips to `true`; release. Fewer than 3 processed. Gate `false` + `LegacyMigrationCompletedEvent` → all 3 processed.

- [ ] **Step 2: Run the tests and see them fail**

Run: `dotnet test src/NzbDrone.Core.Test/Whisparr.Core.Test.csproj --disable-build-servers --filter "FullyQualifiedName~MediaCoverServiceFixture"`
Expected: the first and third tests FAIL (the backlog drains during the migration).

- [ ] **Step 3: Implement the gate**
  - Inject `ILegacyMigrationGate`. In the worker's step 3 (Task 1), read `AutomationPaused` before taking the lock. When it's paused, treat the backlog as empty: `continue`, which uses up that wake-up.
  - At intake, still add ids while paused, but release no permits.
  - `Handle(LegacyMigrationCompletedEvent)`: under the lock, release `_queuedMovieCovers` once per id in the backlog, but only while the state is `Running`. That replaces the wake-ups used up while paused.

- [ ] **Step 4: Run the tests and see them pass**

Run: `dotnet test src/NzbDrone.Core.Test/Whisparr.Core.Test.csproj --disable-build-servers --filter "FullyQualifiedName~MediaCoverTests"`
Expected: PASS, three runs in a row.

- [ ] **Step 5: Commit**

```bash
git add src/NzbDrone.Core/MediaCover/MediaCoverService.cs src/NzbDrone.Core.Test/MediaCoverTests/MediaCoverServiceFixture.cs
git commit -m "Fix: Hold Covers for New Items Until the Migration Finishes"
```

### Task 3: RefreshNewScenes queues covers

**Files:**
- Modify: `src/NzbDrone.Core/Movies/RefreshMovieService.cs` (`RefreshNewScenes`, ~line 402; constructor)
- Test: `src/NzbDrone.Core.Test/MovieTests/RefreshMovieServiceFixture.cs` (beside `should_refresh_new_scenes_without_publishing_movie_updated`, ~line 224)

**Interfaces:**
- Consumes: `IQueueNewMovieCovers.QueueNewMovieCovers(IEnumerable<int>)` from Task 1.

- [ ] **Step 1: Write the failing tests** (use `GivenNewScenes(n)` and the existing mocks)
  - `should_queue_covers_for_new_scenes`: `GivenNewScenes(3)`, `RefreshNewScenes`. `QueueNewMovieCovers` called once, with exactly the three scene ids. `MovieUpdatedEvent` is still never published (the existing test keeps covering that).
  - `should_queue_covers_for_a_new_scene_that_failed_to_refresh`: make the metadata lookup throw for `scenes[1]` (as the existing failed-refresh test does). All three ids are queued.
  - `should_queue_covers_for_collected_new_scenes_when_loop_throws`: same setup as `should_still_scan_collected_new_scenes_when_loop_throws` (`GetMovie(scenes[1].Id)` throws `IOException`). `QueueNewMovieCovers` is called once with only `scenes[0].Id`, and `ExceptionVerification.ExpectedErrors(1)`.
  - `should_not_queue_covers_when_there_are_no_new_scenes`: `RefreshNewScenes(new List<Movie>())`. `QueueNewMovieCovers` never called.

- [ ] **Step 2: Run the tests and see them fail**

Run: `dotnet test src/NzbDrone.Core.Test/Whisparr.Core.Test.csproj --disable-build-servers --filter "FullyQualifiedName~RefreshMovieServiceFixture"`
Expected: FAIL (`QueueNewMovieCovers` never called).

- [ ] **Step 3: Implement.** Inject `IQueueNewMovieCovers` into `RefreshMovieService`. In `RefreshNewScenes`, collect each scene's id once its refresh has been attempted, whether it succeeded or failed (the same points where `ScanNewScene` is called). In the existing `finally`, call `QueueNewMovieCovers(collected)` when the list isn't empty, before `ScanMovies(scanIds)`, so an exception from the scan can't skip it.

- [ ] **Step 4: Run the tests and see them pass**

Run: `dotnet test src/NzbDrone.Core.Test/Whisparr.Core.Test.csproj --disable-build-servers --filter "FullyQualifiedName~RefreshMovieServiceFixture|FullyQualifiedName~MediaCoverTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/NzbDrone.Core/Movies/RefreshMovieService.cs src/NzbDrone.Core.Test/MovieTests/RefreshMovieServiceFixture.cs
git commit -m "Fix: Download Covers for Scenes Added in Bulk"
```

### Task 4: Full verification, smoke test, docs

**Files:**
- Add: `docs/superpowers/specs/2026-11-16-bulk-add-covers-design.md`, `docs/superpowers/plans/2026-11-16-bulk-add-covers.md`

- [ ] **Step 1: Build and run every test project.** Build the Posix solution, then run each `*.Test` project without `--no-build`, excluding `IntegrationTest`, `AutomationTest` and `ManualTest`. Shut the build server down afterwards (`dotnet build-server shutdown`).
Expected: no new failures. If a failure shows up only under a filter, rerun it behind another fixture before calling it pre-existing.

- [ ] **Step 2: Smoke-test on the dev instance (6969, Debug build).** Link one unmapped file to an item that isn't in the library yet (Unmapped Files page, or `POST /api/v5/unmappedfile/{id}/link`).
Expected: within a minute, `MediaCover/movie/<new id>/poster.jpg` plus `-500` and `-250` exist; the log shows `Downloading [<poster url>]` for that item with no `MovieUpdatedEvent` from `RefreshNewScenes`; and the UI shows the poster without a reload.

- [ ] **Step 3: Commit the spec and plan**

```bash
git add docs/superpowers/specs/2026-11-16-bulk-add-covers-design.md docs/superpowers/plans/2026-11-16-bulk-add-covers.md
git commit -m "Chore: Design and Plan for Covers of Bulk-Added Items"
```

- [ ] **Step 4: Stop.** Don't push or open the PR until plz12345 has reviewed the branch.
