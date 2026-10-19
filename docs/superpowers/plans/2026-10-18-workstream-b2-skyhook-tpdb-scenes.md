# Workstream B2: Skyhook TPDB Scene Path Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Skyhook serves TPDB scenes, sites and performers under `tpdb/...` routes. That includes cursor-paged `scenes/full` and numeric-to-UUID resolution. Everything runs from a separate v5 container that shares the v4 database and owns only the new TPDB scene loops.

**Architecture:**
- **Containers.** One repo (`Whisparr-Eros-Metadata`), two long-lived branches. `v4` stays as is; a new `v5`, cut from the tip of `v4`, carries this work.
  - Each branch builds its own image tag, and each branch's CI deploys only its own container.
  - Both containers share the v4 Postgres database.
  - New `Caching:Loops` switches (v5 branch) let the v5 container run only the new TPDB scene loops. The v4 container runs v4 code, so its loops are unchanged.
  - Both still fetch upstream on demand for the requests they serve.
- **Cache tables.** New gzip-JSON cache tables mirror `tpdbmoviecache`.
- **Paging.** The B1 page machinery (`ScenePageService`, `SceneCursor`, `ScenePageResponder`) gets an `ISceneKeySource` seam so TPDB plugs in next to StashDB.
- **Proxy.** TPDB scene, site and performer mapping is ported from the v2 Skyhook's `TPDbProxy`.

**Tech Stack:** ASP.NET Core (net10.0), Dapper + FluentMigrator on Postgres, NUnit 3 + Moq (`src/Backend.Test`), the TPDB REST API (`https://api.theporndb.net/`).

**Spec:** `docs/v5-roadmap.md`, B, "TPDB scene path", plus these decisions agreed with plz12345 on 2026-10-18:
- **Middle-path v5 container:** a shared Postgres database, v4 owns the existing loops, v5 owns only the TPDB scene loops, and the `/v5` route moves to the v5 container.
- **Branch, not repo:** a `v5` branch in the same repo, cut from the tip of `v4`, with GitHub Actions made version-specific.

## Global Constraints

- **Repo:** `~/Git/Whisparr-Eros-Metadata`, with long-lived branches `v4` and `v5`.
  - `v5` is cut from the tip of `v4` (22b3afd at planning time).
  - B2 work goes on the feature branch `v5-tpdb-scenes` off `v5`.
  - Test in a local Docker container (the persistent `skyhook-v5-pg` Postgres plus a locally built image) before any push.
  - Land on `v5` by PR only, and hold the PR until plz12345 agrees it's done.
  - Fixes made on `v4` get merged forward into `v5`.
- **Attribution:** never add AI attribution. Write `plz12345` in docs, never a real name.
- **No infrastructure details** (hosts, IPs, server paths, proxy layout) in either repo. Host-side steps go to plz12345 as a private runbook in chat, never in a file in a repo.
- **Secrets:** the StashDB and TPDB tokens come from the gitignored `appsettings.Development.json`. Pass them to containers as env vars only, and never print them or write them to a tracked file. Test containers run with `Stats__Port=0`.
- **JAV is out of scope for B2.** It comes later as its own feature across the v5 client and v5 Skyhook. Build no JAV routes or loops in this phase.
- **Migrations:** the highest applied migration in prod is 14, on `v4`. v5-only migrations start at **100**, so `v4` can keep using 15+ without a collision in the shared `VersionInfo`. This plan's migration is **100**. Never reuse an applied number. FluentMigrator in v4 code ignores applied versions it doesn't know about.
- **Defaults:** the v5 code with no new config behaves like v4 today. Every new switch defaults to today's behaviour, and `Caching:Loops:TpdbScenes` defaults to **false**. The v5 container sets the switches through env.
- **API shape:**
  - JSON stays PascalCase.
  - Every TPDB entity carries `ForeignIds.TpdbId`, the UUID string.
  - New additive field `ExternalIds.TpdbNumericId` (`int?`, TPDB `_id`) carries the numeric id, so a client can match v2's numeric ids without a call per scene.
  - TPDB scenes have `ItemType = Scene`.
- **Paging:** `scenes/full` keeps B1's contract exactly:
  - default limit 250, max 1000;
  - cursor base64url of `date|id`, ascending;
  - 202 plus `Retry-After: 5` plus `no-store` while pending;
  - 503 plus `Retry-After: 30` when throttled;
  - `s-maxage` 86400 on middle pages and 3600 on the last page.
- **API keys:** the v5 prod stack reuses v4's prod API keys (StashDB, TMDB, TPDB). plz12345's call: the two won't carry peak prod load at the same time. Default rate limits, unless the runbook says otherwise.
- **Tests:** run `dotnet test src/Backend.Test` before every commit. Follow the existing fixtures (`TPDbProxyFixture`, `ScenePageServiceFixture`, `CacheRefreshServiceFixture`, `SceneFullControllerFixture`).

## Review Focus

1. **A v4 container with no new config** runs exactly the loops it runs today, and no TPDB scene loop. Test: Task 1, `should_run_only_existing_loops_by_default`.
2. **Undated TPDB scenes.** TPDB has scenes with no date. Unlike StashDB, they're kept: they sort first, the empty date encodes in the cursor, and they appear once. Test: Task 4, `should_keep_undated_tpdb_scenes_first_and_page_them_once`.
3. **An unknown TPDB UUID or numeric id** gives 404, not 500, on every `tpdb/...` route and in `resolve`, where it's simply absent from the result. Tests: Task 5, `should_return_404_for_unknown_tpdb_scene` and `should_omit_unknown_ids_from_resolve`.
4. **A TPDB credit with a parent performer** links the parent's UUID, never the alias's numeric id or `TmdbId`. Test: Task 2, `should_credit_parent_performer_uuid`.
5. **A TPDB site with 10,000+ scenes.** The key fetch pages TPDB at 100 per call without timing out a request, and the first `scenes/full` page answers 202 while the key list is being built. Test: Task 4, `should_answer_pending_while_tpdb_key_list_builds`.

---

### Task 0: The `v5` branch and version-specific CI

**Files:**
- `.github/workflows/build.yml` on the **`v5`** branch, through a PR from `v5-ci` into `v5`.

**Today** (`build.yml`):
- It triggers on `push` to `v4` and on `workflow_dispatch`, with `MAJOR_VERSION: "4.0.0"`.
- It tags `:latest`, `:<branch>` and `:<version>`.
- The deploy job runs if `refs/heads/v4`, then `docker compose pull` and `docker compose up -d`, which covers **all** services.

**Required:**
- **On `v5`:**
  - trigger `branches: [v5]`;
  - `MAJOR_VERSION: "5.0.0"`;
  - tags `:v5` (branch) and `:<version>` only, **never `:latest`**, which stays v4's;
  - the deploy job is v4's job unchanged (same SSH secrets and steps), except that `if` is `refs/heads/v5` and it uses `cd ${{ secrets.DEPLOY_PATH_V5 }}`. The v5 stack has **its own compose file** in its own directory on the host (plz12345's decision), so a v4 deploy never touches it and vice versa.
- **On `v4`:** no change. Its deploy runs in the v4 compose directory only.
- **The v5 host path** is the repository secret `DEPLOY_PATH_V5`, set by plz12345 and never in the repo. A v5 deploy fails until the v5 compose stack exists on the host, so the stack goes in before this PR merges.

- [ ] **Step 1:** Create `v5` from `origin/v4` and push it. Pushing it triggers nothing, because its `build.yml` still says `v4`.
- [ ] **Step 2:** On `v5-ci`, make the v5 changes above. Validate with `actionlint` if it's available; otherwise do a YAML parse plus a careful read. Check that the tag list has no `latest` and that both deploy commands name the service variable.
- [ ] **Step 3:** Open the PR and hold it for plz12345, who sets up the v5 compose stack and `DEPLOY_PATH_V5` (the runbook in Task 7) before merging it.
- [ ] **Step 4:** After merge, confirm from the Actions tab that a push to `v5` builds and pushes `:v5` and `:5.0.0.N` (and not `:latest`), and that the deploy job runs in the v5 directory.

### Task 1: Per-loop switches (the middle-path container)

**Files** (all B2 code from here on goes on `v5-tpdb-scenes` off `v5`):
- Create: `src/Backend/Cache/LoopOptions.cs`
- Modify: `src/Backend/Cache/CacheRefreshService.cs` (`ExecuteAsync`, `RefreshFromUpdateFeeds`)
- Modify: `src/API/Startup.cs` (options binding; `ClientMetricsBackgroundService` gate)
- Modify: `src/API/appsettings.json` (document the defaults)
- Test: `src/Backend.Test/Cache/CacheRefreshServiceFixture.cs`

**Interfaces:**
- Produces:
  ```csharp
  public class LoopOptions   // bound from "Caching:Loops"
  {
      public bool StashDb { get; set; } = true;        // scene/studio/performer update feeds + stale loops
      public bool Tmdb { get; set; } = true;           // TMDB feed + stale movie loop
      public bool TpdbMovies { get; set; } = true;     // TPDB /changes/movies + stale TPDB movie loop
      public bool TpdbScenes { get; set; } = false;    // Task 6's loops
      public bool ClientAnalytics { get; set; } = true; // ClientMetricsBackgroundService
  }
  ```
  `CacheRefreshService` exposes `internal IReadOnlyList<string> ActiveLoopNames()` for tests. Each loop runs only if its switch is on **and** its token is set (today's token rule stays).

- [ ] **Step 1: Write the failing tests:**
  - `should_run_only_existing_loops_by_default`: all tokens set, no `Caching:Loops` section. `ActiveLoopNames()` equals today's set (update feed, stale site, stale performer, stale scene, stale movie, stale TPDB movie), with no TPDB scene loop.
  - `should_run_only_tpdb_scene_loops_in_v5_mode`: `StashDb=false, Tmdb=false, TpdbMovies=false, TpdbScenes=true`. `ActiveLoopNames()` contains only the Task 6 loop names (stub the names as constants now) and the update feed. The update feed's StashDB, TMDB and TPDB movie sections are skipped (verify the proxies are never called).
  - `should_still_require_token`: `TpdbScenes=true` with no `TPDb:Token` gives no TPDB scene loop.
  - `should_skip_client_analytics_when_disabled`: `ClientAnalytics=false` means `ClientMetricsBackgroundService` returns without querying.
- [ ] **Step 2:** Run `dotnet test src/Backend.Test --filter "FullyQualifiedName~CacheRefreshServiceFixture"`. Expect FAIL.
- [ ] **Step 3: Implement.**
  - Bind `LoopOptions` in `Startup` with `services.Configure<LoopOptions>(Configuration.GetSection("Caching:Loops"))`.
  - Gate each loop and each update-feed section.
  - `HydrationQueue` and on-demand fetching are **not** gated, because both containers serve requests.
- [ ] **Step 4:** Run `dotnet test src/Backend.Test`. Expect PASS.
- [ ] **Step 5: Commit**: `New: Per-loop switches so a container can own a subset of refresh loops`.

### Task 2: TPDB scene, site and performer mapping

**Files:**
- Modify: `src/Backend/Model/ExternalIds.cs` (add `int? TpdbNumericId`)
- Modify: `src/Backend/MetadataSource/TPDb/TPDbProxy.cs`:
  - `MapScene` (fix credits; line ~620 writes the performer's numeric id into `TmdbId`);
  - add `MapSite` and `MapPerformer`;
  - add proxy methods.
- Modify or create under `src/Backend/MetadataSource/TPDb/Requests/` and `Resources/`: the site, performer and batch requests as needed. Existing: `GetSceneRequest`, `GetScenesBySiteRequest` (`/sites/{id}/scenes?page=&per_page=100`), `GetScenesByPerformerRequest`, `SceneSearchRequest`, `SiteSearchRequest`.
- Test: `src/Backend.Test/MetadataSource/TPDb/TPDbProxyFixture.cs`, with small JSON fixtures inline or under `TestData/`.

**Interfaces:**
- Produces, on `ITPDbProxy`:
  ```csharp
  Task<Episode> GetTpdbScene(string uuidOrNumeric);                 // null on 404
  Task<Studio> GetTpdbSite(string uuidOrNumeric);
  Task<Performer> GetTpdbPerformer(string uuidOrNumeric);
  Task<TpdbScenePageResult> GetTpdbSiteScenes(string siteUuid, int page);       // 100 per page
  Task<TpdbScenePageResult> GetTpdbPerformerScenes(string performerUuid, int page);
  Task<List<Episode>> SearchTpdbScenes(string query);
  Task<List<Studio>> SearchTpdbSites(string query);
  Task<List<Performer>> SearchTpdbPerformers(string query);
  public sealed record TpdbScenePageResult(List<Episode> Scenes, int LastPage, int Total);
  ```
- **Mapping rules**, ported from the v2 Skyhook's `TPDbProxy.MapScene` (`~/Git/WhisparrAPI.Metadata/src/Backend/MetadataSource/TPDb/TPDbProxy.cs`, about L300-334):
  - **Scene:**
    - `ForeignIds.TpdbId = id` (the UUID), `TpdbNumericId = _id`, `ItemType = Scene`.
    - `Title`, `Overview = description`, `ReleaseDate = date` (`yyyy-MM-dd` or null) and `ReleaseDateUtc`, `Duration = seconds / 60`, `Code = external_id` is **not** set (the SKU problem; see the TPDB movie rule).
    - `Homepage = url`, `Images` from poster/background, `Genres`/`Tags` from tags.
    - `Studio` from `site` with `Studio.ForeignIds.TpdbId = site uuid` and `TpdbNumericId = site_id`, plus `StashId` when `tpdbstudiomapping` has it.
  - **Credit:** `Performer.ForeignIds.TpdbId = parent?.id ?? id` (UUID), `TpdbNumericId = parent?._id ?? _id`, `Name = parent?.name ?? name`, `Character = name`, gender from `parent.extras` or `extra`. Never set `TmdbId`.
  - **Site:** `ForeignIds.TpdbId`, `TpdbNumericId`, `Title`, `Homepage`, `Images` (logo), and the parent network as `NetworkForeignIds.TpdbId` when present.
  - **Performer:** the same identity fields plus the name, gender, birthdate, images and measurements fields that `StashDbProxy`'s performer mapping fills where TPDB has them. Leave the rest null.

- [ ] **Step 1: Write the failing tests:**
  - `should_map_tpdb_scene_ids_and_studio`;
  - `should_credit_parent_performer_uuid` (a credit with a `parent` gets the parent UUID and numeric id; `TmdbId` is null);
  - `should_not_set_code_from_external_id`;
  - `should_map_undated_scene_with_null_release_date`;
  - `should_page_site_scenes_100_per_page` (the request path is `/sites/{uuid}/scenes?page=2&per_page=100`; `LastPage` comes from `meta.last_page`);
  - `should_return_null_on_404`;
  - `should_map_site_and_performer`.
- [ ] **Step 2:** Run `dotnet test src/Backend.Test --filter "FullyQualifiedName~TPDbProxyFixture"`. Expect FAIL.
- [ ] **Step 3: Implement.** Paths: `/scenes/{id}`, `/sites/{id}`, `/performers/{id}`, and the search paths `?q=`. TPDB accepts the UUID or the numeric id on these.
- [ ] **Step 4:** Run `dotnet test src/Backend.Test`. Expect PASS.
- [ ] **Step 5: Commit**: `New: Map TPDB scenes, sites and performers`.

### Task 3: TPDB caches and hydration (migration 100)

**Files:**
- Create: `src/Backend/Datastore/Migration/100_tpdb_scene_caches.cs`. It creates `tpdbscenecache` (the common columns plus `siteid` varchar, nullable), `tpdbsitecache` and `tpdbperformercache`, all with the common columns `id` serial PK, `foreignid` varchar unique, `value` binary, `lastchange`, `lastrefresh`, `expiry`, and the same autovacuum settings as the 010 tables. It also creates `tpdbidmap` (`kind` varchar, `numericid` int, `uuid` varchar, unique on (`kind`, `numericid`)).
- Create: `src/Backend/Cache/CachedTpdbScene.cs`, `CachedTpdbSite.cs`, `CachedTpdbPerformer.cs`, their repositories and services, modelled on `CachedTpdbMovie` / `CachedTpdbMovieRepository` / `CachedTpdbMovieService`. `ForeignId` is `ForeignIds.TpdbId`; `CachedTpdbScene.SiteId` is `Studio.ForeignIds.TpdbId`; the expiry is 30 days, as for TPDB movies.
- Create: `src/Backend/Cache/TpdbIdMapRepository.cs`.
- Modify: `src/Backend/Datastore/TableMapping.cs`
- Modify: `src/Backend/SkyHook/HydrationQueue.cs` (add `HydrationKind.TpdbScene` and `HydrationKind.TpdbPerformer`, dispatching to the methods below)
- Modify: `src/Backend/SkyHook/SkyHook.cs` (`ISkyHook`)
- Test:
  - `src/Backend.Test/SkyHook/SkyHookTpdbSceneFixture.cs` (new);
  - `HydrationQueueFixture.cs` (add cases).

**Interfaces:**
- Produces:
  ```csharp
  // ISkyHook
  Task<Episode> GetTpdbScene(string uuid);                  // cache first, upstream on miss, attaches cached studio/performers
  Task<Studio> GetTpdbSite(string uuid);
  Task<Performer> GetTpdbPerformer(string uuid);
  Task<HydrationOutcome> HydrateTpdbScenes(IReadOnlyList<string> uuids);
  Task<HydrationOutcome> HydrateTpdbPerformers(IReadOnlyList<string> uuids);
  Task<IReadOnlyDictionary<int, string>> ResolveTpdbIds(string kind, IReadOnlyList<int> numericIds); // kind: "scene"|"site"|"performer"; unknown ids omitted
  // ICachedTpdbSceneService
  IReadOnlyDictionary<string, Episode> GetCached(IEnumerable<string> uuids);
  void Upsert(IEnumerable<Episode> scenes);   // also records tpdbidmap rows for scene, site and credited performers
  ```
- Hydration fetches one id at a time through the TPDB rate limiter. TPDB has no batch-by-ids endpoint. Use `ExecuteBatch` with the limiter for concurrency. A 404 means gone, as for B1's definitive not-found.
- `ResolveTpdbIds` reads `tpdbidmap` first, then fetches the misses from upstream (`/scenes/{numeric}` and so on) and records them.

- [ ] **Step 1: Write the failing tests:**
  - `should_serve_cached_tpdb_scene_without_upstream`;
  - `should_fetch_and_cache_on_miss`;
  - `should_record_id_map_for_scene_site_and_performers_on_upsert`;
  - `should_resolve_numeric_ids_from_map_then_upstream`;
  - `should_omit_unknown_numeric_ids`;
  - `should_mark_404_as_gone_during_tpdb_hydration`.
  - In `HydrationQueueFixture`: `should_dispatch_tpdb_kinds`.
- [ ] **Step 2:** Run the fixtures. Expect FAIL.
- [ ] **Step 3: Implement.** Mirror `CachedTpdbMovieService.GetInternal` for the cache, expiry and get-uncached flow.
- [ ] **Step 4:** Run `dotnet test src/Backend.Test`. Expect PASS. Then apply the migration once against the local `skyhook-v5-pg`, by starting the locally built API image against it, and confirm in psql that the four tables exist.
- [ ] **Step 5: Commit**: `New: TPDB scene, site and performer caches with numeric id resolution`.

### Task 4: Scene paging for a second source

**Files:**
- Create: `src/Backend/SkyHook/ISceneKeySource.cs`, `StashDbSceneKeySource.cs` (today's logic moved out of `ScenePageService`) and `TpdbSceneKeySource.cs`.
- Modify: `src/Backend/SkyHook/ScenePageService.cs`. It takes key sources; `IScenePageService` gains `GetTpdbSiteScenePage` and `GetTpdbPerformerScenePage`.
- Move: `SceneKey` from the `MetadataSource/StashDb` namespace to `Backend/SkyHook`, because it's generic.
- Test:
  - `src/Backend.Test/SkyHook/ScenePageServiceFixture.cs` (existing tests stay green);
  - `TpdbSceneKeySourceFixture.cs` (new).

**Interfaces:**
- Consumes: Task 2's `GetTpdbSiteScenes`/`GetTpdbPerformerScenes`, and Task 3's `ICachedTpdbSceneService` and `HydrationKind.TpdbScene`.
- Produces:
  ```csharp
  public interface ISceneKeySource
  {
      string CacheNamespace { get; }                                   // "site", "performer", "tpdbsite", "tpdbperformer"
      HydrationKind Kind { get; }                                      // Scene or TpdbScene
      Task<List<SceneKey>> FetchKeys(string ownerId);                  // sorted (ReleaseDate ordinal, Id), deduped
      IReadOnlyDictionary<string, Episode> GetCached(IEnumerable<string> ids);
  }
  // IScenePageService additions
  Task<ScenePage> GetTpdbSiteScenePage(string siteUuid, string cursor, int limit);
  Task<ScenePage> GetTpdbPerformerScenePage(string performerUuid, string cursor, int limit);
  ```
- **The TPDB key fetch is also the catalogue fetch.** `TpdbSceneKeySource.FetchKeys` pages `GetTpdbSiteScenes` 100 at a time (with `ExecuteBatch` for pages 2..N), `Upsert`s every scene it receives, and returns the keys. So pages after the fetch are usually 200 with no hydration.
  - **Undated scenes are kept**, with `ReleaseDate = ""`, so they sort first. This differs from StashDB, which drops them.
  - While the key list is being built, `GetPage` answers pending (202) for that owner instead of blocking. Use the existing single-flight per key: a caller that doesn't own the fetch gets an empty pending page.

- [ ] **Step 1: Write the failing tests:**
  - All existing `ScenePageServiceFixture` tests pass unchanged against `StashDbSceneKeySource`.
  - `should_keep_undated_tpdb_scenes_first_and_page_them_once`;
  - `should_upsert_scenes_seen_during_tpdb_key_fetch`;
  - `should_answer_pending_while_tpdb_key_list_builds`;
  - `should_namespace_tpdb_key_lists_separately` (the same id under `site` and `tpdbsite` never collides).
- [ ] **Step 2:** Run the fixtures. Expect FAIL.
- [ ] **Step 3: Implement.** Move StashDB specifics behind `StashDbSceneKeySource`, with no behaviour change, and add the TPDB source.
- [ ] **Step 4:** Run `dotnet test src/Backend.Test`. Expect PASS.
- [ ] **Step 5: Commit**: `New: Page TPDB site and performer scenes through a key-source seam`.

### Task 5: `tpdb/...` routes, resolve, and Cloudflare purge

**Files:**
- Create: `src/API/Controllers/TpdbSceneController.cs`, with `[Route("tpdb")]` alongside the existing `TpdbMovieController`, and `src/API/Resources/TpdbResolveResource.cs`.
- Modify: `src/API/Controllers/ScenePageResponder.cs`. Generalise the exception-to-404 mapping to also cover `TPDbApiException.IsNotFound`. Keep the GUID check for StashDB routes; TPDB routes validate a UUID as well.
- Modify: `src/Backend/Cloudflare/CloudflareProxy.cs` (purge routes)
- Test: `src/Backend.Test/API/TpdbSceneControllerFixture.cs` (new) and `CloudflareSitesFixture`/purge tests.

**Interfaces:**
- Consumes: Task 3's `ISkyHook` TPDB methods, Task 4's page methods, and `ScenePageResponder.Respond`.
- **Routes**, all PascalCase JSON with the same `Cache-Control` rules as their StashDB twins:
  - `GET tpdb/scene/{uuid}`, `GET tpdb/site/{uuid}`, `GET tpdb/performer/{uuid}`: 404 when unknown.
  - `GET tpdb/scene/search?q=`, `tpdb/site/search?q=`, `tpdb/performer/search?q=`.
  - `GET tpdb/site/{uuid}/scenes/full?cursor=&limit=` and `GET tpdb/performer/{uuid}/scenes/full?cursor=&limit=`: B1's contract exactly.
  - `POST tpdb/performer/bulk`: same contract as `performer/bulk` (100 ids processed, more than 1000 gives 400, 202 while hydrating).
  - `POST tpdb/resolve`, body `{ "Kind": "scene"|"site"|"performer", "Ids": [int] }` with at most 1000 ids (otherwise 400), returns `{ "Ids": { "<numeric>": "<uuid>" } }`. Unknown ids are omitted.
- **Purge:** when a TPDB scene, site or performer refreshes, purge `tpdb/scene/{uuid}`, `tpdb/site/{uuid}` or `tpdb/performer/{uuid}` on every `Cloudflare:Sites` entry.

- [ ] **Step 1: Write the failing tests:**
  - `should_return_404_for_unknown_tpdb_scene`;
  - `should_page_tpdb_site_scenes_with_b1_headers`;
  - `should_reject_non_uuid_tpdb_ids_with_404`;
  - `should_omit_unknown_ids_from_resolve`;
  - `should_reject_resolve_over_1000_ids`;
  - `should_purge_tpdb_routes_on_every_site`.
- [ ] **Step 2:** Run the fixtures. Expect FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4:** Run `dotnet test src/Backend.Test`. Expect PASS.
- [ ] **Step 5: Commit**: `New: tpdb scene, site and performer routes with paged scenes and id resolution`.

### Task 6: TPDB scene refresh loops

**Files:**
- Modify: `src/Backend/Cache/CacheRefreshService.cs`
- Test: `src/Backend.Test/Cache/CacheRefreshServiceFixture.cs`

**Interfaces:**
- Consumes: Task 1's `LoopOptions.TpdbScenes`, Task 2's proxy, Task 3's caches, and the existing `GetRecentlyChanged(since, type)` (`/changes/{type}`).
- **Loops**, all gated by `TpdbScenes` plus `TPDb:Token`, with `CrawlInterval` pacing:
  - **TPDB scene update feed:** `GetRecentlyChanged(since, "scenes")`, with the same since/overlap and skip-if-unchanged logic as `RefreshFromTpdbUpdate`. Re-fetch only the changed UUIDs **already in** `tpdbscenecache`, upsert them, and purge their routes.
  - **Stale TPDB sites and performers:** like `RefreshStaleTpdbMovies`, re-fetch expired rows in batches of `RevalidateBatchSize`, up to `RevalidateMaxCount`. TPDB's `/changes` types for sites and performers aren't confirmed, so expiry drives them.
  - **Stale TPDB scenes:** the same pattern, as the safety net.

- [ ] **Step 1: Write the failing tests:**
  - `should_refresh_only_cached_changed_tpdb_scenes`;
  - `should_not_poll_tpdb_scene_changes_when_switch_off`;
  - `should_refresh_stale_tpdb_sites_and_performers_in_batches`.
- [ ] **Step 2:** Run. Expect FAIL.
- [ ] **Step 3: Implement.** Replace Task 1's placeholder loop names with the real loops.
- [ ] **Step 4:** Run `dotnet test src/Backend.Test`. Expect PASS.
- [ ] **Step 5: Commit**: `New: TPDB scene refresh loops for the v5 container`.

### Task 7: Two-container local test, roadmap update, PR

**Files:**
- Modify: `~/Git/whisparr-next/docs/v5-roadmap.md`, B, "TPDB scene path": describe it as built, with the switches, the routes, `TpdbNumericId`, and undated scenes kept. Add one line: "v5 runs as its own container sharing the v4 database; host details live outside the repo."

- [ ] **Step 1: Build the image and run two containers locally.**
  - Build with `docker build -t skyhook-b2 .` from the repo root.
  - Run both against `skyhook-v5-pg`, with the StashDB and TPDB tokens as env vars from `appsettings.Development.json` (never printed) and `Stats__Port=0`.
    - **"v4" on :18080:** default config.
    - **"v5" on :18081:** `Caching__Loops__StashDb=false`, `Caching__Loops__Tmdb=false`, `Caching__Loops__TpdbMovies=false`, `Caching__Loops__TpdbScenes=true`, `Caching__Loops__ClientAnalytics=false`.
  - Run "v4" from the **`v4` branch image** (`git worktree` of `origin/v4`, built as `skyhook-v4`) and "v5" from this branch's image. That's the real split.
  - Confirm from both logs that each runs only its own loops, and that migration 100 applied once.
  - Restart the v4 container after v5 has applied migration 100, and confirm it boots cleanly. This verifies that FluentMigrator in v4 code ignores the unknown applied version 100.
- [ ] **Step 2: Smoke the v5 container:**
  - `tpdb/site/search?q=brazzers`;
  - pick a large TPDB site and walk `tpdb/site/{uuid}/scenes/full` to the end, recording pages, the 202 count, the total time and unique items equal to `Total`;
  - `POST tpdb/resolve` with a few known numeric scene and site ids;
  - one `tpdb/performer/bulk`;
  - one existing StashDB route (`site/search`) to show v5 still serves the v4 routes.

  Record the numbers in the roadmap.
- [ ] **Step 3:** Commit the roadmap on whisparr-next `develop`: `Chore: Roadmap B2 reflects the TPDB scene path as built`.
- [ ] **Step 4:** Push `v5-tpdb-scenes` and open the PR to `v5` (no merge until plz12345 agrees). The PR body covers the switches, the new routes, migration 100 and the test numbers, with **no host details**.
- [ ] **Step 5:** Separately, in chat only, hand plz12345 a private host runbook:
  - create the v5 stack's own compose file (its own directory) from the `:v5` image tag, with the env above and v4's prod API keys;
  - set the repository secret `DEPLOY_PATH_V5`;
  - give `/v5` its own Traefik router to that service and leave `/v4` on the existing service;
  - deploy order: v5 applies migration 100 when it starts; v4 is unaffected;
  - check both containers' loop logs.
