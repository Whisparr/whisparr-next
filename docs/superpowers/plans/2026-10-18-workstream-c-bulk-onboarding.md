# Workstream C: Bulk Onboarding Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Adding or refreshing a studio or performer pulls its scenes from Skyhook's paged
`scenes/full` endpoint and prefetches new performers through `performer/bulk`. No scene or
performer is fetched one at a time.

**Architecture:**
- Each `scenes/full` page is cached in memory, along with each `performer/bulk` result.
- `GetSceneInfo` and `GetPerformerInfo` read those caches before going to HTTP.
- For each page:
  - `AddMovies` adds the page's new scenes;
  - the refresh's write step runs inline on those scenes, fed from memory;
  - no `RefreshMovieCommand` is queued.
- A page walker handles 202 (retry the page later) and 503 (back off).
- One importer service is shared by studio sync and performer sync.

**Tech Stack:** .NET 10, NUnit + Moq + FluentAssertions, the Skyhook v5 endpoints (live at
`https://api.whisparr.com/v5`).

**Spec:** `docs/v5-roadmap.md`, section "C. Bulk onboarding". Section B1 and its plan
(`docs/superpowers/plans/2026-10-17-workstream-b1-skyhook-v5-bulk.md`) define the
endpoints.

### Deviation from the roadmap text (for review)

The roadmap proposes these changes:
- map each page straight to `MovieMetadata`;
- skip the refresh fetch by checking `LastInfoSync`;
- move performer fetches out of `RefreshMovieService` into `AddPerformersCommand`.

This plan uses a prefetch cache instead. The HTTP cost is the same: `3 + pages +
⌈P/100⌉` calls, and **zero per-scene calls**. The difference is that the per-scene rules
stay in one place instead of being duplicated on a second code path. Those rules are:
- in `AddMovies`: exclusions, after-date, naming;
- in the refresh's write step: studio link, alternative titles, credits, hidden performer
  rows, performer lists, and the first disk scan that queues search on add.

**Agreed with plz12345 (2026-10-18):**
- **No queued per-scene `RefreshMovieCommand` for bulk adds.** The refresh's write step
  runs inline, page by page, inside the studio or performer command. It reads the cache,
  so there's no network.
- **No `MovieUpdatedEvent` from that inline step.** `MediaCoverService` handles that event
  synchronously and blocks the publisher once its 100 slots are full (2 workers).
- **Covers stay hotlinked** until a later refresh caches them.
- **SignalR still updates live.** `MoviesImportedEvent` already broadcasts each page in
  one batch, and it already carries the studio and performer ids for the API cache
  clearing.
- **The add is complete when the command finishes**, and it doesn't flood the command
  queue.

## Global Constraints

- Repo `~/Git/whisparr-next`. Commit directly to `develop`: no PR, no CI.
- Never add AI attribution: no `Co-Authored-By`, no "Generated with" footers.
- Write `plz12345` in docs, never a real name, and write paths as `~/...`.
- No server hostnames, IPs, `/opt` paths or proxy layout in this repo. The public URL
  `https://api.whisparr.com/v5` is fine.
- Commit prefixes: `New:`, `Fix:`, `Chore:`.
- Skyhook JSON is **PascalCase**.
- Skyhook endpoints, exactly as live:
  - `GET site/{stashId}/scenes/full?cursor=` and `GET performer/{stashId}/scenes/full?cursor=`.
    Omit `limit` so every client shares the server default of 250 and its Cloudflare cache
    key.
  - `POST performer/bulk`, with a JSON array body of at most **100** ids per call.
- Page body: `{ Total, NextCursor, Items: [scene], Pending: [stashId] }`.
  - Each item has exactly the shape of `GET scene/{id}`. A `performer/bulk` entry has
    exactly the shape of `GET performer/{id}`. Both were verified key-for-key against prod.
- Page statuses:
  - `200`: complete.
  - `202`: some items still hydrating. Carries `Retry-After: 5`, a non-empty `Pending`,
    the items already hydrated, and `NextCursor`.
  - `503`: queue full. Carries `Retry-After: 30`.
  - `404`: unknown id.
  - `400`: bad cursor.
- `performer/bulk` answers `200`, or `202` with a `Pending` list.
- Run all five test projects before each commit (Core, Common, Api, Host, Mono):
  `dotnet test <project> --filter "TestCategory!=IntegrationTest&TestCategory!=AutomationTest&TestCategory!=ManualTest"`.

## Review Focus

1. **A page stays 202 forever** (StashDB down). The sync finishes once the page has gone 5
   minutes without its `Pending` shrinking. It keeps every scene it received and logs the
   ids still pending. Test: Task 2,
   `should_give_up_on_a_page_after_five_minutes_without_progress`.
2. **503 throttling.** Wait `Retry-After` and retry the same cursor, without spinning. If
   throttled for 5 minutes straight, stop the walk with a warning. Test: Task 2,
   `should_wait_retry_after_and_retry_when_throttled`.
3. **The cache is gone before the refresh runs** (app restart, or the 2 h TTL expired).
   `GetSceneInfo` falls back to HTTP and returns the same result. Test: Task 1,
   `should_fetch_scene_over_http_when_not_prefetched`.
4. **A page lists a scene that's already in the library or excluded.** It isn't re-added,
   and no performer prefetch is spent on it. Test: Task 4,
   `should_skip_scenes_already_in_library_or_excluded`.
5. **A later page fails with a non-retryable error.** Scenes from earlier pages stay added,
   and the exception reaches `Execute`'s existing per-studio catch, so the other studios
   still sync. Tests: Task 4,
   `should_keep_scenes_from_earlier_pages_when_a_later_page_fails`; Task 5,
   `should_sync_next_studio_when_scene_paging_fails`.

6. **One scene fails in the inline refresh** (bad data, a DB error). The rest of the page
   and the walk carry on, and the failure is logged with the scene. Test: Task 3,
   `should_continue_with_next_scene_when_one_fails`.

No longer applicable: deleting a studio while its scene refreshes were queued used to
re-add it. Bulk adds no longer queue those refreshes, so the issue is gone for studio and
performer adds.

---

### Task 1: Scene pages and the prefetch cache in SkyHookProxy

**Files:**
- Create: `src/NzbDrone.Core/MetadataSource/ScenePage.cs`, for `ScenePage`,
  `ScenePageItem` and `ScenePageStatus`.
- Create: `src/NzbDrone.Core/MetadataSource/SkyHook/Resource/ScenePageResource.cs`, for
  `ScenePageResource` and `PerformerBulkResource`.
- Modify: `src/NzbDrone.Core/MetadataSource/IProvideMovieInfo.cs`.
- Modify: `src/NzbDrone.Core/MetadataSource/SkyHook/SkyHookProxy.cs`:
  - `GetSceneInfo` (line 404) and `GetPerformerInfo` (line 557);
  - add a constructor parameter `ICacheManager cacheManager`.
- Test: `src/NzbDrone.Core.Test/MetadataSource/SkyHook/SkyHookProxyScenePageFixture.cs`.
  Mock `IHttpClient` the way `SkyHookProxyStudioDateSearchFixture` does.

**Interfaces:**
- Produces:
  ```csharp
  public enum ScenePageStatus { Complete, Pending, Throttled }
  public class ScenePageItem { public string StashId { get; set; } public List<string> PerformerStashIds { get; set; } }
  public class ScenePage
  {
      public ScenePageStatus Status { get; set; }
      public int Total { get; set; }
      public string NextCursor { get; set; }          // null on the last page
      public List<ScenePageItem> Items { get; set; }  // hydrated items only
      public List<string> Pending { get; set; }
      public TimeSpan RetryAfter { get; set; }        // header seconds; default 5s (202) / 30s (503)
  }
  // IProvideMovieInfo
  ScenePage GetStudioScenePage(string stashId, string cursor);     // cursor null = first page
  ScenePage GetPerformerScenePage(string stashId, string cursor);
  List<string> PrefetchPerformers(IEnumerable<string> stashIds);   // returns ids NOT cached (pending/missing/failed)
  ```
- Page fetch errors:
  - `404` throws `MovieNotFoundException(stashId)`.
  - Any status other than 200, 202, 503 or 404 throws `HttpException`.
- `PrefetchPerformers` never throws: a failed batch is logged at Warn, and its ids are
  returned.
- The cache lives in `ICacheManager.GetCache<MovieResource>(GetType(), "scenePrefetch")` and
  `GetCache<PerformerResource>(GetType(), "performerPrefetch")`.
  - Entries are keyed by StashDB id, with a **2-hour** lifetime.
  - Cache the raw resources and map them on every read. A cached `MovieMetadata` would be
    shared across reads and mutated by `AddMovies`.

- [ ] **Step 1: Write the failing tests** in `SkyHookProxyScenePageFixture`:
  - `should_request_site_scenes_full_with_cursor`: the request path ends with
    `site/{id}/scenes/full`; the `cursor` query param is absent on the first page and
    equals the given cursor afterwards; there is no `limit` param.
  - `should_map_complete_page`: a PascalCase JSON body gives a 200 with 2 items. Expect
    `Status == Complete`, `Total`, `NextCursor`, and each item's `StashId` and
    `PerformerStashIds`, taken from `Credits[].Performer.ForeignIds.StashId`.
  - `should_map_pending_page_with_retry_after`: a 202 with `Retry-After: 7` and
    `Pending: ["x"]`. Expect `Status == Pending`, `RetryAfter == 7s`, `Pending == ["x"]`,
    and the hydrated items still mapped.
  - `should_map_throttled_page`: a 503 with no header. Expect `Status == Throttled` and
    `RetryAfter == 30s`.
  - `should_throw_not_found_for_unknown_studio`: a 404 throws `MovieNotFoundException`.
  - `should_serve_scene_info_from_a_fetched_page_without_http`: fetch a page with scene `a`,
    then call `GetSceneInfo("a")`. Expect a result equal to the HTTP mapping, and
    `IHttpClient.Get<MovieResource>` never called.
  - `should_fetch_scene_over_http_when_not_prefetched`: `GetSceneInfo("b")` does one HTTP
    GET of `scene/b`.
  - `should_prefetch_performers_in_batches_of_100`: 250 ids give 3 POSTs to
    `performer/bulk`, with bodies of 100, 100 and 50.
  - `should_return_pending_and_failed_performer_ids`: batch 1 is a 202 with
    `Pending: ["p2"]`; batch 2 is a 500. Expect the result to contain `p2` plus all of batch
    2's ids.
  - `should_serve_performer_info_from_prefetch_without_http`.
- [ ] **Step 2: Run them.** Expect them to fail to compile (no `ScenePage`, no
  `GetStudioScenePage`):
  `dotnet test src/NzbDrone.Core.Test/Whisparr.Core.Test.csproj --filter "FullyQualifiedName~SkyHookProxyScenePageFixture"`.
- [ ] **Step 3: Implement.**
  - Extract `private static Tuple<MovieMetadata, Studio, List<Performer>, List<Credit>> MapSceneInfo(MovieResource resource)`
    from the body of `GetSceneInfo`. Both the cache-hit path and the HTTP path call it.
  - Do the same for performers with the existing `MapPerformer`.
  - The page and bulk requests use `_whisparrMetadata.Create().SetSegment(RouteConst, …)`
    with `SuppressHttpError = true`, like `GetSceneInfo`.
  - Parse `Retry-After` the way `TooManyRequestsException` does: seconds or an HTTP date.
  - `PrefetchPerformers` takes distinct ids and chunks them into 100s.
- [ ] **Step 4: Run the fixture** (expect PASS), then run all five test projects.
- [ ] **Step 5: Commit**: `New: Skyhook scene pages and performer prefetch`.

### Task 2: ScenePageWalker

**Files:**
- Create: `src/NzbDrone.Core/MetadataSource/ScenePageWalker.cs`. It holds
  `IScenePageWalker`, `ScenePageWalker`, `ScenePageWalkResult`, `ISkyhookPacer` and
  `SkyhookPacer`.
- Test: `src/NzbDrone.Core.Test/MetadataSource/ScenePageWalkerFixture.cs`. Use a fake
  `ISkyhookPacer` whose `Sleep` advances its own `UtcNow`, so the tests never really sleep.

**Interfaces:**
- Consumes: `ScenePage` and `ScenePageStatus` from Task 1.
- Produces:
  ```csharp
  public interface ISkyhookPacer { DateTime UtcNow { get; } void Sleep(TimeSpan duration); }  // SkyhookPacer: DateTime.UtcNow + Thread.Sleep
  public class ScenePageWalkResult { public int Pages { get; set; } public List<string> GaveUp { get; set; } }
  public interface IScenePageWalker
  {
      // fetchPage(cursor) -> page; handlePage is called for EVERY response that carries items, 200 or 202
      ScenePageWalkResult Walk(Func<string, ScenePage> fetchPage, Action<ScenePage> handlePage);
  }
  ```
- `public static readonly TimeSpan StallTimeout = TimeSpan.FromMinutes(5);`
- Exceptions thrown by `fetchPage` or `handlePage` propagate unchanged.

**Algorithm.** The tests don't fully determine it, so it's spelled out here:
1. **First pass.** Start at cursor `null`.
   - `Throttled`: sleep `RetryAfter` and re-fetch the same cursor. If throttling has lasted
     ≥ `StallTimeout`, log a Warn and return, with the marker `"cursor:<c>"` for the page
     it stopped at added to `GaveUp`. Pages after it are unknown, so the next sync picks
     them up.
   - Otherwise call `handlePage(page)`. If `Pending`, remember `(cursor, page)`. Continue
     with `NextCursor` until it's null.
2. **Revisit** each remembered page in order:
   - sleep its `RetryAfter`, re-fetch, then `handlePage`;
   - stop when it's `Complete`;
   - a shrinking `Pending.Count` resets the stall clock;
   - once ≥ `StallTimeout` passes without progress, add its `Pending` ids to `GaveUp`, log
     them at Warn, and move on;
   - `Throttled` during a revisit sleeps `RetryAfter` and counts as no progress.

- [ ] **Step 1: Write the failing tests:**
  - `should_walk_every_page_until_next_cursor_is_null`: 3 complete pages. Expect
    `handlePage` called 3 times in order, `Pages == 3`, `GaveUp` empty, and no sleep.
  - `should_move_on_from_a_pending_page_and_revisit_it`: page 1 is a 202, page 2 a 200; the
    revisit of page 1 is a 200. Expect `handlePage` calls in the order p1(202), p2, p1(200).
  - `should_wait_retry_after_and_retry_when_throttled`: a 503 with `RetryAfter` 30s, then a
    200 for the same cursor. Expect one sleep of 30s, and the same cursor requested twice.
  - `should_give_up_on_a_page_after_five_minutes_without_progress`: the revisit keeps
    answering 202 with `Pending == ["x","y"]`. Expect `Walk` to return
    `GaveUp == ["x","y"]` after the fake clock passes 5 minutes, and no further fetches.
  - `should_reset_the_stall_clock_when_pending_shrinks`: `Pending` sizes 3, 3, 2, 2, … with
    the clock passing 4 minutes between each shrink. Expect no give-up while it keeps
    shrinking.
  - `should_propagate_fetch_exceptions`: page 2 throws `HttpException`. Expect `Walk` to
    throw it, after `handlePage` has run for page 1.
- [ ] **Step 2: Run them.** Expect them to fail to compile.
- [ ] **Step 3: Implement** the algorithm above in `ScenePageWalker`.
  - Constructor: `(ISkyhookPacer pacer, Logger logger)`.
  - DryIoc auto-registers both services.
- [ ] **Step 4: Run the fixture** (expect PASS), then all five projects.
- [ ] **Step 5: Commit**: `New: Walk Skyhook scene pages with 202 and 503 handling`.

### Task 3: Inline refresh for bulk-added scenes

**Files:**
- Modify: `src/NzbDrone.Core/Movies/RefreshMovieService.cs`:
  - `RefreshMovieInfo` (line 95) gains `bool publishUpdated = true`, and when false skips
    the `MovieUpdatedEvent` at its end;
  - the class also implements a new `IRefreshNewScenes`.
- Modify: `src/NzbDrone.Core/Movies/Events/MoviesImportedEvent.cs`,
  `src/NzbDrone.Core/Movies/MovieService.cs` (`AddMovies`),
  `src/NzbDrone.Core/Movies/AddMovieService.cs` (`AddMovies`) and
  `src/NzbDrone.Core/Movies/MovieAddedHandler.cs`.
- Test:
  - `src/NzbDrone.Core.Test/MovieTests/RefreshMovieServiceFixture.cs` (add cases);
  - `src/NzbDrone.Core.Test/MovieTests/MovieAddedHandlerFixture.cs` (create).

**Interfaces:**
- Produces:
  ```csharp
  public interface IRefreshNewScenes
  {
      // For each movie: RefreshMovieInfo(id, publishUpdated: false), then RescanMovie(movie, isNew: true, CommandTrigger.Unspecified).
      // Per-movie try/catch: log Error with the movie and continue. No MovieUpdatedEvent.
      void RefreshNewScenes(List<Movie> movies);
  }
  // MoviesImportedEvent gains: public bool RefreshInline { get; }, as a constructor parameter (List<Movie> movies, bool refreshInline = false)
  // IMovieService:    List<Movie> AddMovies(List<Movie> newMovies, bool refreshInline = false);
  // IAddMovieService: List<Movie> AddMovies(List<Movie> newMovies, bool ignoreErrors = false, bool refreshInline = false);
  ```
- `MovieAddedHandler.Handle(MoviesImportedEvent)` queues nothing when `RefreshInline` is
  true. The `MovieController` broadcast and the studio and performer cache clearing still
  run off the same event.

- [ ] **Step 1: Write the failing tests:**
  - `should_refresh_new_scenes_without_publishing_movie_updated`: `RefreshNewScenes` on 2
    movies. Expect credits updated (`ICreditService.UpdateCredits`) for both, and no
    `MovieUpdatedEvent` published (verify on `IEventAggregator`).
  - `should_rescan_new_scenes_so_search_on_add_runs`: verify `IDiskScanService.Scan` is
    called for each movie.
  - `should_continue_with_next_scene_when_one_fails`: `GetSceneInfo` throws for movie 1.
    Expect movie 2 still refreshed, and an error logged (`ExceptionVerification.ExpectedErrors(1)`).
  - `should_not_queue_refresh_for_inline_imports`: `MovieAddedHandler` gets
    `new MoviesImportedEvent(movies, refreshInline: true)`. Expect `PushMany` never called.
  - `should_queue_refresh_for_regular_imports`: `refreshInline: false`. Expect one
    `RefreshMovieCommand` per movie (today's behaviour).
- [ ] **Step 2: Run them.** Expect FAIL.
- [ ] **Step 3: Implement.** `RefreshNewScenes` reuses `RefreshMovieInfo` and
  `RescanMovie` unchanged apart from the flag. Don't copy their bodies.
- [ ] **Step 4: Run the fixtures** (expect PASS), then all five projects.
- [ ] **Step 5: Commit**: `New: Refresh bulk-added scenes inline without queuing commands`.

### Task 4: SceneCatalogImporter

**Files:**
- Create: `src/NzbDrone.Core/Movies/SceneCatalogImporter.cs`, for `ISceneCatalogImporter`
  and `SceneCatalogImporter`.
- Test: `src/NzbDrone.Core.Test/MovieTests/SceneCatalogImporterFixture.cs`.
  - Mock `IScenePageWalker.Walk` so that it invokes `handlePage` with canned pages.
  - Mock `IProvideMovieInfo`, `IAddMovieService`, `IRefreshNewScenes`, `IMovieService`,
    `IPerformerService` and `IImportListExclusionService`.

**Interfaces:**
- Consumes: Task 1's `PrefetchPerformers`, Task 2's `IScenePageWalker.Walk`, and Task 3's
  `IRefreshNewScenes` and `AddMovies(…, refreshInline)`.
- Produces:
  ```csharp
  public interface ISceneCatalogImporter
  {
      // ownerTitle is for logs only. buildStub(stashId) returns the unsaved Movie to add (ForeignId = stashId plus owner settings).
      // Returns the number of scenes added.
      int ImportScenes(string ownerTitle, Func<string, ScenePage> fetchPage, Func<string, Movie> buildStub);
  }
  ```

**Per page (`handlePage`):**
1. Keep only items whose `StashId` isn't in the library and isn't excluded.
   - The library set is `_movieService.AllMovieStashIds()`, read once per import and grown
     as scenes are added.
   - The exclusion set is `GetAllExclusions().Select(e => e.ForeignId)`, read once.
2. Collect their distinct `PerformerStashIds` that aren't in
   `_performerService.AllPerformerForeignIds()` (read per page), and call
   `PrefetchPerformers`. **Do this before step 4**, because the inline refresh reads the
   cache.
3. Call `var added = _addMovieService.AddMovies(newItems.Select(i => buildStub(i.StashId)).ToList(), true, refreshInline: true)`
   once per page.
4. Call `_refreshNewScenes.RefreshNewScenes(added)`.
5. After the walk:
   - log Info:
     `"Synced {0}: {1} scenes on Skyhook, added {2}"` (ownerTitle, last page `Total`, added);
   - log Warn listing `GaveUp`, if there is any.

- [ ] **Step 1: Write the failing tests:**
  - `should_add_new_scenes_page_by_page`: 2 pages of 2 new items. Expect `AddMovies` called
    twice, with stubs from `buildStub`, `ignoreErrors: true` and `refreshInline: true`.
    Expect `RefreshNewScenes` called with each call's returned list. The return value is
    the total added.
  - `should_skip_scenes_already_in_library_or_excluded`: a page with `a` (in the library),
    `b` (excluded) and `c` (new). Expect only `c` added, and no prefetch for performers
    credited only on `a` or `b`.
  - `should_not_add_a_scene_twice_when_a_pending_page_is_revisited`: the same page arrives
    as a 202 with `[a]`, then as a 200 with `[a, b]`. Expect the second `AddMovies` to
    receive only `b`.
  - `should_prefetch_only_unknown_performers_before_adding`: verify the call order
    `PrefetchPerformers`, then `AddMovies`, then `RefreshNewScenes` (a Moq `MockSequence`
    or a callback list), with known performers left out.
  - `should_add_scenes_without_waiting_for_pending_performers`: `PrefetchPerformers` returns
    every id as pending. `AddMovies` is still called for the page.
  - `should_keep_scenes_from_earlier_pages_when_a_later_page_fails`: the walker invokes
    page 1, then throws `HttpException`. Expect `AddMovies` called for page 1, and the
    exception propagated.
- [ ] **Step 2: Run them.** Expect them to fail to compile.
- [ ] **Step 3: Implement** `SceneCatalogImporter`.
- [ ] **Step 4: Run the fixture** (expect PASS), then all five projects.
- [ ] **Step 5: Commit**: `New: Import a studio or performer's scenes page by page`.

### Task 5: Studio and performer sync use the importer

**Files:**
- Modify: `src/NzbDrone.Core/Movies/Studios/RefreshStudioService.cs`, `SyncStudioItems`
  (the `studio.Monitored` scene branch, and where `GetStudioWorks` is called).
- Modify: `src/NzbDrone.Core/Movies/Performers/RefreshPerformerService.cs`,
  `SyncPerformerItems` (the same two places).
- Test:
  - update `src/NzbDrone.Core.Test/StudioTests/RefreshStudioServiceFixture.cs`;
  - update `src/NzbDrone.Core.Test/PerformerTests/RefreshPerformerServiceFixture.cs`.
  - Their scene tests currently mock `GetStudioWorks` and `GetPerformerWorks`
    `StashdbIds`. They now mock `ISceneCatalogImporter` instead.
- Move (housekeeping from `80623ca181`):
  - `MovieTests/StudioServiceTests/StudioResourceCacheFixture.cs` → `StudioTests/`;
  - `MovieTests/PerformerServiceTests/PerformerResourceCacheFixture.cs` → `PerformerTests/`;
  - fix their namespaces to `NzbDrone.Core.Test.StudioTests` and
    `NzbDrone.Core.Test.PerformerTests`.

**Interfaces:**
- Consumes: Task 4's `ISceneCatalogImporter.ImportScenes`, and Task 1's
  `GetStudioScenePage` and `GetPerformerScenePage`.

**Behaviour:**
- **Scene branch** (`studio.Monitored` / `performer.Monitored`): call
  `_sceneCatalogImporter.ImportScenes(studio.Title, c => _movieInfo.GetStudioScenePage(studio.ForeignId, c), stashId => new Movie { … })`.
  The stub sets the same fields as today's stub:
  - `QualityProfileId`, `RootFolderPath`, `Tags`;
  - `Monitored = monitorNewItems`;
  - `AddOptions = { SearchForMovie = SearchOnAdd && monitorNewItems, AddMethod = Studio }`.
  - For performers, use `GetPerformerScenePage` and `AddMethod.Performer`.
- **`GetStudioWorks` / `GetPerformerWorks`** are now called only inside the
  `MoviesMonitored` branch. Their `TmdbIds` and `TpdbIds` handling doesn't change, and
  scenes no longer read `StashdbIds`.
- Errors propagate from `ImportScenes`. `Execute`'s existing per-item `try/catch` logs and
  carries on, and the existing `WebException` catch around `GetStudioWorks` stays.

- [ ] **Step 1: Update and add the failing tests:**
  - `should_use_monitor_new_items_when_adding_scenes` (studio and performer versions):
    capture the `buildStub` passed to `ImportScenes`, invoke it with `"s1"`, and assert
    `Monitored`, `AddOptions.SearchForMovie`, `AddMethod` and `ForeignId == "s1"`.
  - `should_page_scenes_from_skyhook_for_monitored_studio`: invoke the captured
    `fetchPage("c1")`, then verify `GetStudioScenePage(studio.ForeignId, "c1")`.
  - `should_not_fetch_studio_works_when_movies_are_not_monitored`: `Monitored = true`,
    `MoviesMonitored = false`. Expect `GetStudioWorks` never called. Write the performer
    equivalent too.
  - `should_sync_next_studio_when_scene_paging_fails`: `ImportScenes` throws `HttpException`
    for studio 1. Expect studio 2 still to be imported, through
    `RefreshStudiosCommand { StudioIds = [1, 2] }`.
- [ ] **Step 2: Run them.** Expect FAIL.
- [ ] **Step 3: Implement.** Add `ISceneCatalogImporter` as a constructor dependency of
  both services.
- [ ] **Step 4: Run both fixtures** (expect PASS), then all five projects.
- [ ] **Step 5: Commit**: `New: Studio and performer adds page scenes from Skyhook`.

### Task 6: Live measurement and roadmap update

**Files:**
- Modify: `docs/v5-roadmap.md`, section C. Describe the design as built:
  - the prefetch cache;
  - the inline refresh with no queued commands and no `MovieUpdatedEvent`;
  - covers hotlinked until a later refresh;
  - the shared importer;
  - the walker's 202/503 rules with the 5-minute stall timeout.

  Add a short "Measured" table.

- [ ] **Step 1: Build and launch** a fresh dev instance.
  - Build: `dotnet build src/Whisparr.sln -p:Platform=Posix`, then `yarn build`.
  - Run `_output/net10.0/Whisparr -nobrowser -data=<new empty scratch dir>`, with
    `<LogLevel>trace</LogLevel>` set in that dir's `config5.xml` after the first boot.
  - `_output/net10.0/UI` must link to `_output/UI`.
- [ ] **Step 2: Add Brazzers Exxtra** (`39cee498-a9ac-4403-910a-1a0157ad22d8`, about 4,000
  scenes): monitored, search on add **off**. Measure and record:
  - **(a)** time from add to the last scene row existing (the `Synced Brazzers Exxtra` log
    line);
  - **(b)** time until the `RefreshStudios` command completes. Check that no
    `RefreshMovieCommand` was queued for these scenes (the trace log, or `GET /api/v5/command`);
  - **(c)** Skyhook requests, counted from the trace log's `api.whisparr.com/v5` request
    lines and split into `scenes/full`, `performer/bulk`, `scene/` and `performer/`.

  Expected:
  - one `scenes/full` per page (about 16);
  - `⌈P/100⌉` `performer/bulk` calls;
  - **zero** `scene/{id}` calls;
  - single `performer/{id}` calls only for ids `PrefetchPerformers` reported pending.
- [ ] **Step 3: Check the UI with SFW mode on.** Scenes appear on the studio page while the
  pages arrive. Covers show through hotlinks with nothing cached under `MediaCover/`. A
  scene's performers appear on its details page, and a credited performer's page lists the
  scene.
- [ ] **Step 4: Stop the instance.** Then commit
  `Chore: Roadmap C reflects the prefetch design and measurements`.
