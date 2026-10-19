# Workstream E: Metadata-Source Lock and TPDB Scenes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A v5 install picks its scene source (StashDB or TPDB) and movie source (TMDB or TPDB) once, at first run. After that **both** sources are locked together, so neither can change and leave orphaned or duplicate items behind (decision 12). Every scene, studio and performer path then works end to end with TPDB through the live Skyhook `/v5/tpdb/...` routes.

**Architecture:**
- **Lock.** A new `IMetadataSourceService` owns the two source settings and the lock. Nothing else writes them: `SaveConfigDictionary` drops them, which covers the frozen V3 API too.
- **Source routing.** `SkyHookProxy` reads the locked scene source once per call. It picks Skyhook routes (`scene/…` or `tpdb/scene/…`) and the id key (`ForeignIds.StashId` or `ForeignIds.TpdbId`) through one helper, `SceneSourceIds`. The rest of the app keeps using `ForeignId`, which holds the locked source's native UUID.
- **First-run UI.** A modal, modelled on `FirstRun/AuthenticationRequiredModal`, asks for the sources until they're locked.

**Tech Stack:** C# .NET 10, NUnit + Moq + FluentAssertions (`CoreTest<T>` / `DbTest`); React 19 + TypeScript, TanStack Query (`useApiQuery` / `useApiMutation`).

**Spec:** `docs/v5-roadmap.md` §E. Decisions 2, 3, 6, 9 and 12 are in "Decisions (plz12345's)". The Skyhook routes are in §B "TPDB scene path (B2): built".

## Global Constraints

- **Repo and branch:** `~/Git/whisparr-next`, branch `develop`. Commit straight to `develop`, with no PRs. Use `New:` / `Fix:` / `Chore:` prefixes. Never add AI attribution. Write `plz12345` in docs, never a real name.
- **Public-repo hygiene:** no hostnames, IPs, server paths or proxy layout in code, docs or commits.
- **API split:** `Api.V3` is frozen and gets no new endpoints. New surface goes in `Whisparr.Api.V5`. Core changes reach V3 automatically.
- **Migrations:** v5-only migrations are ≥300 and the next free number is **301**. Config keys are stored lowercased (`ConfigService.SetValue`), and enum values are stored as their lowercased name.
- **Allowed sources:** scene StashDB or TPDB; movie TMDB or TPDB (decision 2). The picker never offers `MovieMetadataType.NONE`. An existing NONE install stays NONE when the migration locks it.
- **Skyhook TPDB routes (B2, live):**
  - `tpdb/scene|site|performer/{uuid}`;
  - `tpdb/scene|site|performer/search?q=`, which takes `q` only;
  - `tpdb/site|performer/{uuid}/scenes/full?cursor=` on B1's contract. A 202 body may carry `Building: true`, meaning the key list isn't built yet, so there are no items and no `NextCursor`.
  - `POST tpdb/performer/bulk`.
  - TPDB entities carry `ForeignIds.TpdbId` (the UUID).
  - Task 0 adds `tpdb/scene|site|performer/changed` and `tpdb/site|performer/{uuid}/works`.
- **Eros-Metadata (Task 0 only):**
  - `~/Git/Whisparr-Eros-Metadata`, on a branch off `origin/v5`.
  - Test it in a local Docker container (the persistent `skyhook-v5-pg`, `Stats__Port=0`), then land it on `v5` by PR, held until plz12345 says done.
  - Tokens come from the gitignored `src/API/appsettings.Development.json`, passed as env vars and never printed.
- **User-facing strings:** keys in `src/NzbDrone.Core/Localization/Core/en.json`, flat PascalCase and alphabetical.
- **Tests:** NUnit `*Fixture`, at least 80% coverage on new backend code. Run every test project before the final commit (Core, Common, Api, Host, Mono).
- **Frontend:** `yarn typecheck` must report zero errors, plus `yarn lint` and `yarn build`. Props are typed `Readonly<…>` (S6759).
- **Tools:** make file edits with the Edit/Write tools, not sed or shell redirection. Run each Bash command on its own, with absolute paths.

## Scope rulings

These are decisions made while planning, against the roadmap text:
- **No per-row source on studios or performers.** The roadmap's "TPDB studio and performer equivalents" isn't needed because the lock means one scene source per database. `Studio.ForeignId` and `Performer.ForeignId` hold the locked source's UUID, and only `MovieMetadata.MetadataSource` gains `TpdbScene`.
- **StashDB import lists are hidden under TPDB, not renamed.** This was tested against TPDB's stash-box GraphQL endpoint on 2026-10-19 with the lists' exact query.
  - The lists' query fails as sent. TPDB requires a `direction` field, and it types `studios`, `performers`, `tags` and `date` as plain strings rather than `{modifier, value}` objects.
  - With adapted shapes, `queryScenes` answers `scenes: null` with `count` equal to `per_page` for every filter, including none at all. It's a stub.
  - `findStudio` and `searchScene` work, which covers Stash's identify task. `queryStudios` doesn't exist, and `queryPerformers` ignores its name filter.
  - A StashDB list under a TPDB library would also add StashDB UUIDs that the TPDB routes can't resolve.
  - A TPDB-backed list would need TPDB's REST API instead. That's a later feature, so the rename waits until then.
- **First add locks.** An add through the API before the picker has run locks the current values (StashDb and TMDB by default). That way a scene can never land under one source and then have the source changed.
- **The TPDB changed feeds are added to Skyhook (Task 0).** The StashDB `scene|site|performer/changed` routes are served from the Skyhook cache (`lastchange >= since`). The TPDB cache tables have the same column, and B2's feed loop keeps them current from TPDB's `/changes/scenes`, which is the same upstream feed the v3 Skyhook (WhisparrAPI.Metadata) polls internally. The v3 Skyhook never exposed a changed route to clients; v2 just refetches on a schedule.
- **TPDB works routes are added to Skyhook (Task 0).** This was confirmed on 2026-10-19 against TPDB's OpenAPI spec (`api.theporndb.net/specs?openapi.json`) and live calls:
  - `/sites/{identifier}/movies` returns Digital Playground's 1,472 movies.
  - `/performers/{identifier}/movies` takes the **parent** performer UUID. It includes "Pirates 2" for Abbey Brooks. The per-site performer id that appears on the movie returns 404.
  - B2 already keys TPDB scene and movie credits by that parent UUID (`TPDbProxy.cs:490/525/757`), and Skyhook already has `GetMoviesBySiteRequest` and `GetMoviesByPersonRequest`.
  - TPDB works return TPDB movie UUIDs only. With TPDB scenes and **TMDB** movies, studio and performer movie sync stays off and is logged. The picker already steers TPDB-scene users to TPDB movies (decision 3).
- **TPDB `/changes` supports `performers`** (the spec's `type` enum is `movies`, `scenes`, `jav`, `performers`; sites aren't covered). Task 0 adds a TPDB performer feed loop next to B2's scene feed loop. Sites stay expiry-driven.
- **`ParsedMovieInfo.StashId` keeps its name.** It holds whatever UUID appears in a release title, and renaming it touches the parser for no behaviour change.

## Review Focus

1. **Re-syncing a TPDB studio must not re-add scenes already in the library.** The importer's dedupe reads `AllMovieStashIds()`, which is empty under TPDB. Task 4 test: `should_skip_scenes_already_in_library_when_source_is_tpdb`.
2. **A first page that answers `Building` must still walk every page once the build finishes.** Today the walker queues it for a revisit that ignores `NextCursor`, so only page one would import. Task 4 test: `should_wait_out_building_then_walk_all_pages`.
3. **A credit or studio with no id in the locked source is skipped.** For example, a TMDB-movie performer that has a StashId but no TpdbId while scenes are on TPDB. It must not throw, and must not create a performer with a blank `ForeignId`. Task 2 test: `should_skip_credits_without_an_id_in_the_locked_source`.
4. **A host-config PUT on V3 or V5 that carries a different movie source leaves the stored source unchanged.** On V5 it fails validation. Task 1 test: `should_not_save_source_keys_through_config_dictionary`. Task 5 test: `should_reject_movie_source_change_in_host_config`.
5. **An undated TPDB scene (no `ReleaseDate`) imports, names and displays without errors.** Task 4 test: `should_import_undated_tpdb_scene`.

---

### Task 0: Skyhook TPDB changed and works routes, plus the performer feed (Eros-Metadata, `v5`)

**Files** (in `~/Git/Whisparr-Eros-Metadata`):
- Modify: `src/API/Controllers/TpdbSceneController.cs`
- Modify: `src/Backend/Cache/CachedTpdbEntityService.cs`, plus its repository, so it exposes `GetChanged(DateTime since)`. Mirror `CachedEntityRepository.GetChanged` (`SELECT foreignid … WHERE lastchange >= @Date`).
- Modify: `src/Backend/SkyHook/SkyHook.cs` (`ISkyHook`), adding the TPDB works methods. Reuse `GetMoviesBySiteRequest` and `GetMoviesByPersonRequest`, paging through all pages the way the StashDB works path already does after its mapping step.
- Modify: `src/Backend/Cache/CacheRefreshService.cs`, adding a TPDB performer feed loop gated by `TpdbScenes`. It mirrors B2's scene feed loop on `GetRecentlyChanged(since, "performers")`: re-fetch only changed UUIDs already in `tpdbperformercache`, upsert them, and purge their routes. It needs its own since/overlap state, as the scene and movie feeds have.
- Test: `src/Backend.Test/API/TpdbSceneControllerFixture.cs` and `src/Backend.Test/Cache/CacheRefreshServiceFixture.cs`.

**Interfaces:**
- Produces:
  - `GET tpdb/scene/changed?since=`, `GET tpdb/site/changed?since=` and `GET tpdb/performer/changed?since=`. Each returns a JSON array of TPDB UUIDs with the same `Cache-Control` as the StashDB `scene/changed` route (`SceneController.GetChanged`: `now + 10 min`).
  - `GET tpdb/site/{uuid}/works` and `GET tpdb/performer/{uuid}/works`. Each returns the same JSON shape as StashDB's `site/{guid}/works` (`StudioWorksResource` / `PerformerWorksResource`), with `TpdbMovies` filled, `Scenes` and `Movies` empty, and the same `Cache-Control` as the StashDB works routes. A TPDB 404 gives 404, and a 429 gives 503 with `Retry-After: 30`, as for B2's routes.
  - The performer feed loop shows up in `ActiveLoopNames()` when `TpdbScenes` is on.
- **Check `lastchange`:** confirm it moves only when an entity's content changes, not on every refresh. If B2's `UpdateWith` bumps it on every crawl, the feed is still correct, just broader than needed. Record which one it is in the report, and don't change `UpdateWith` here.

- [ ] **Step 1: Write the failing tests:**
  - `should_list_tpdb_scenes_changed_since`
  - `should_set_changed_cache_header`
  - `should_return_tpdb_site_works_as_tpdb_movies`
  - `should_return_tpdb_performer_works_as_tpdb_movies`
  - `should_map_tpdb_404_on_works_to_404`
  - `should_refresh_only_cached_changed_tpdb_performers`
- [ ] **Step 2:** Run `dotnet test ~/Git/Whisparr-Eros-Metadata/src/Backend.Test`. Expect FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run the suite. Expect PASS. Then build the image, run it against `skyhook-v5-pg`, and `curl` `tpdb/scene/changed?since=<yesterday>`, expecting 200 with an array.
- [ ] **Step 5:** Commit `New: TPDB changed feeds, works routes and performer change loop`. Push the branch and open a PR to `v5`, held. Task 3's live behaviour depends on this being deployed.

### Task 1: Source settings, lock and migration

**Files:**
- Create: `src/NzbDrone.Core/Configuration/SceneMetadataType.cs`
- Create: `src/NzbDrone.Core/MetadataSource/MetadataSourceService.cs`, holding `IMetadataSourceService`, `MetadataSourceService`, `MetadataSources` and `MetadataSourcesLockedException`.
- Create: `src/NzbDrone.Core/Datastore/Migration/301_lock_metadata_sources_for_existing_libraries.cs`
- Modify: `src/NzbDrone.Core/Configuration/IConfigService.cs` and `ConfigService.cs`. The new properties go next to `WhisparrMovieMetadataSource` (`ConfigService.cs:610`). `SaveConfigDictionary` changes too.
- Modify: `src/NzbDrone.Core/Movies/AddMovieService.cs`, `Movies/Studios/AddStudioService.cs` and `Movies/Performers/AddPerformerService.cs`, so each calls `EnsureLocked()` before adding.
- Test: `src/NzbDrone.Core.Test/MetadataSource/MetadataSourceServiceFixture.cs` and `src/NzbDrone.Core.Test/Datastore/Migration/301_lock_metadata_sources_fixture.cs`.

**Interfaces:**
- Produces:
  ```csharp
  public enum SceneMetadataType { StashDb = 0, Tpdb = 1 }
  // IConfigService
  SceneMetadataType WhisparrSceneMetadataSource { get; }   // default StashDb
  bool MetadataSourcesLocked { get; }                       // default false
  // ConfigService also gets public setters for both, used only by MetadataSourceService
  public class MetadataSources { public SceneMetadataType Scene; public MovieMetadataType Movie; public bool Locked; }
  public interface IMetadataSourceService
  {
      MetadataSources Get();
      void Choose(SceneMetadataType scene, MovieMetadataType movie); // locks; throws MetadataSourcesLockedException if already locked, ArgumentException for MovieMetadataType.NONE
      void EnsureLocked();                                           // locks at the current values if not yet locked; no-op otherwise
  }
  ```
- **`SaveConfigDictionary` drops three keys:** `WhisparrSceneMetadataSource`, `WhisparrMovieMetadataSource` and `MetadataSourcesLocked`, matched case-insensitively. It logs one Debug line when it drops any.
- **Migration 301:** when `Movies` has at least one row, insert `whisparrscenemetadatasource = stashdb` and `metadatasourceslocked = true` into `Config`, leaving the movie source as it is. When `Movies` is empty, do nothing, so a fresh install sees the picker.

- [ ] **Step 1: Write the failing tests:**
  - `should_choose_and_lock_sources`
  - `should_throw_when_choosing_after_lock`
  - `should_reject_none_movie_source`
  - `should_lock_current_values_on_first_add` (`EnsureLocked` writes the defaults, StashDb and TMDB)
  - `should_not_relock_when_already_locked`
  - `should_not_save_source_keys_through_config_dictionary`
  - Migration fixture: `should_lock_when_library_has_movies` and `should_leave_unlocked_when_library_empty`
  - Add-service cases: `AddMovieService`, `AddStudioService` and `AddPerformerService` each call `EnsureLocked()` once per add batch, verified with a Moq mock.
- [ ] **Step 2:** Run `dotnet test src/NzbDrone.Core.Test/Whisparr.Core.Test.csproj --filter "FullyQualifiedName~MetadataSourceServiceFixture|FullyQualifiedName~301_lock"`. Expect FAIL (compile errors).
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run the same filter, then the Core suite with `--filter "TestCategory!=IntegrationTest&TestCategory!=AutomationTest&TestCategory!=ManualTest"`. Expect PASS.
- [ ] **Step 5:** Commit `New: Lock metadata sources once chosen`.

### Task 2: Map Skyhook resources by the locked scene source

**Files:**
- Create: `src/NzbDrone.Core/MetadataSource/SkyHook/SceneSourceIds.cs`
- Modify: `src/NzbDrone.Core/Movies/MovieMetadata.cs`. Add `TpdbScene` to `MetadataSource`, after `TpdbMovie`, so existing stored values keep their numbers.
- Modify: `src/NzbDrone.Core/MetadataSource/SkyHook/SkyHookProxy.cs`, in the static mappers: `MapForeignId` (:89), `MapMovie` (:110, the studio key at :177–191 and the tag ids at :155), `MapMetadataSource` (:203), `MapSceneInfo` (:626), `MapCast` (:1485), `MapSceneCast` (:1575), `MapStudio` (:1993), `MapSearchResult` (:2024) and `MapPerformer` (:2052).
- Modify: `src/NzbDrone.Core/Movies/RefreshMovieService.cs:291-329`, `GetMetadata`. `MetadataSource.TpdbScene` calls `GetSceneInfo(movie.ForeignId)`, the same as `Stash`.
- Test: `src/NzbDrone.Core.Test/MetadataSource/SkyHook/SkyHookProxyMappingFixture.cs` (new).

**Interfaces:**
- Consumes: Task 1's `SceneMetadataType`.
- Produces:
  ```csharp
  public static class SceneSourceIds
  {
      public static string Key(SceneMetadataType source, ExternalIdResource ids); // StashId or TpdbId; null when absent
      public static string Route(SceneMetadataType source, string stashRoute);    // "scene" -> "tpdb/scene", "site/search" -> "tpdb/site/search", etc. StashDb returns stashRoute unchanged
      public static Movies.MetadataSource SceneMetadataSource(SceneMetadataType source); // Stash or TpdbScene
  }
  ```
  - Every static mapper that keys a scene, studio or performer gets a trailing parameter, `SceneMetadataType sceneSource = SceneMetadataType.StashDb`, so existing callers and tests keep compiling. Instance call sites pass `_configService.WhisparrSceneMetadataSource`.
  - **Key rules:**
    - a scene's `ForeignId` and a studio's or performer's `ForeignId` come from `SceneSourceIds.Key`;
    - `MovieMetadata.StudioForeignId` likewise;
    - credits' `PerformerForeignId` likewise;
    - `ScenePageItem` ids likewise (Task 3).
  - Under TPDB, `MovieMetadata.StashId` stays whatever Skyhook sent (usually null), and `TpdbId` holds the UUID.
  - A credit, performer or studio whose locked-source key is empty is dropped from the mapped result, never stored with a blank `ForeignId`. `MapStudio` already returns null when there's no StashId; generalise that to the key.
  - Tag ids (`TagIds`) stay StashDB-only. Under TPDB they're an empty list, because tag exclusion is StashDB-based.

- [ ] **Step 1: Write the failing tests** in `SkyHookProxyMappingFixture`:
  - `should_key_tpdb_scene_by_tpdb_uuid` (`ForeignId == ForeignIds.TpdbId`, `MetadataSource == TpdbScene`)
  - `should_key_stash_scene_by_stash_id` (unchanged behaviour)
  - `should_key_tpdb_studio_and_credits_by_tpdb_uuid`
  - `should_skip_credits_without_an_id_in_the_locked_source`
  - `should_route_scene_paths_to_tpdb` (`SceneSourceIds.Route(Tpdb, "performer/bulk") == "tpdb/performer/bulk"`)
  - `should_refresh_tpdb_scene_through_get_scene_info` (in `RefreshMovieService`'s existing fixture)
- [ ] **Step 2:** Run `dotnet test … --filter "FullyQualifiedName~SkyHookProxyMappingFixture"`. Expect FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run the filter, then the Core suite. Expect PASS.
- [ ] **Step 5:** Commit `New: Map TPDB scenes, studios and performers by the locked source`.

### Task 3: Route Skyhook calls by the locked scene source

**Files:**
- Modify: `src/NzbDrone.Core/MetadataSource/SkyHook/SkyHookProxy.cs`:
  - `GetSceneInfo` (:416), `GetPerformerInfo` (:757), `GetStudioInfo` (:791) and `PrefetchPerformers` (:460, including the id it caches under at :489);
  - `GetScenePage`/`MapScenePage` (:525–596);
  - `GetChangedScenes`, `GetChangedStudios` and `GetChangedPerformers` (:269–342);
  - `GetStudioWorks`/`GetPerformerWorks` (:818, :879);
  - `SearchForNewScene`/`SearchForNewPerformer`/`SearchForNewStudio` (:1318–1485, :1882–1990), plus the GUID detection at :1337/:1418/:1903.
- Modify: `src/NzbDrone.Core/MetadataSource/ScenePage.cs`. Rename `ScenePageItem.StashId` to `SceneId` and `PerformerStashIds` to `PerformerIds`, and add `ScenePageStatus.Building`. Also rename `ForgetPrefetched`'s parameters to `sceneIds`/`performerIds` in `IProvideMovieInfo`.
- Modify: `src/NzbDrone.Core/MetadataSource/SkyHook/Resource/ScenePageResource.cs`, adding `public bool Building { get; set; }`.
- Test: `src/NzbDrone.Core.Test/MetadataSource/SkyHook/SkyHookProxyScenePageFixture.cs` (extend it) and `SkyHookProxySceneSourceFixture.cs` (new; HTTP mocked as in the page fixture).

**Interfaces:**
- Consumes: Task 2's `SceneSourceIds`.
- Produces:
  - **`ScenePageStatus.Building`:** returned for a 202 whose body has `Building == true`, with `RetryAfter` taken from the header (default 5 s).
  - **Changed feeds:** under TPDB, `GetChanged{Scenes,Studios,Performers}` call Task 0's `tpdb/{kind}/changed`. Until Task 0 is deployed, an HTTP error there yields an empty set, which the existing code already does for any error. The callers already fall back to time-based refresh on an empty set (`RefreshMovieService.cs:472`, `RefreshStudioService.cs:330`, `RefreshPerformerService` likewise).
  - **Works:** under TPDB, `GetStudioWorks`/`GetPerformerWorks` call Task 0's `tpdb/{site|performer}/{uuid}/works`. The returned `TpdbIds` feed the existing TPDB-movie branch in `RefreshStudioService`/`RefreshPerformerService` unchanged. When the movie source is TMDB, those branches find no `TmdbIds`, so log one Info line per refresh: `"Movie sync for TPDB-sourced studios and performers needs TPDB as the movie source"`.
  - **Search:** under TPDB, a search term that is a bare GUID, or a GUID with a `stash:`/`stashid:`/`tpdb:`/`tpdbid:` prefix, does a direct `tpdb/{kind}/{uuid}` lookup. Anything else goes to `tpdb/{kind}/search?q=`. A studio+date scene search sends one combined `q` string, `"{studio} {date} {title}"`, because the TPDB route takes `q` only.

- [ ] **Step 1: Write the failing tests:**
  - `should_fetch_tpdb_scene_from_tpdb_route`
  - `should_prefetch_tpdb_performers_through_tpdb_bulk`
  - `should_page_tpdb_site_scenes_from_tpdb_route`
  - `should_map_building_202_to_building_status`
  - `should_fetch_tpdb_changed_scenes_from_tpdb_route`
  - `should_fetch_tpdb_studio_works_from_tpdb_route`
  - `should_lookup_tpdb_scene_directly_for_guid_term`
  - `should_send_combined_query_for_tpdb_studio_date_search`
  - Every existing StashDB fixture stays green with the renamed `ScenePageItem` members.
- [ ] **Step 2:** Run the two fixtures. Expect FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run the fixtures, then the Core suite. Expect PASS.
- [ ] **Step 5:** Commit `New: Route scene, studio and performer calls to Skyhook's TPDB routes`.

### Task 4: Bulk import under TPDB

**Files:**
- Modify: `src/NzbDrone.Core/MetadataSource/ScenePageWalker.cs`
- Modify: `src/NzbDrone.Core/Movies/SceneCatalogImporter.cs` (:51, :72–109)
- Modify: `src/NzbDrone.Core/Movies/MovieRepository.cs` / `MovieService.cs`, adding `AllSceneForeignIds()`.
- Test: `src/NzbDrone.Core.Test/MetadataSource/ScenePageWalkerFixture.cs` and `src/NzbDrone.Core.Test/MovieTests/SceneCatalogImporterFixture.cs`.

**Interfaces:**
- Consumes: Task 3's `ScenePageStatus.Building` and the `ScenePageItem.SceneId`/`PerformerIds` names.
- Produces:
  ```csharp
  // IMovieService / IMovieRepository
  List<string> AllSceneForeignIds(); // ForeignId of every Movies row whose MovieMetadata.ItemType == Scene
  // ScenePageWalker
  public static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(15);
  ```
  - **Walker.** A `Building` page is waited out in place, the same way `FetchUnthrottled` handles `Throttled`:
    - sleep `RetryAfter`, then refetch the same cursor;
    - it never goes on the revisit list and never reaches `handlePage`;
    - give up after `BuildTimeout`, recording `cursor:{cursor}` in `GaveUp`, as the throttle timeout does.
  - **Importer.** It dedupes against `AllSceneForeignIds()` instead of `AllMovieStashIds()`, and uses `SceneId`/`PerformerIds`. `AllMovieStashIds()` stays for its other callers.

- [ ] **Step 1: Write the failing tests:**
  - `should_wait_out_building_then_walk_all_pages` (pages: Building, Building, then page 1 with `NextCursor`, then page 2 Complete; expect both pages handled, no revisit, two sleeps)
  - `should_give_up_after_build_timeout`
  - `should_skip_scenes_already_in_library_when_source_is_tpdb`
  - `should_import_undated_tpdb_scene` (null `ReleaseDate` reaches `AddMovies` and the scene is added)
  - In `MovieServiceFixture`: `should_find_scene_by_tpdb_uuid_in_title`. `FindScene` already matches on `ForeignId`, so this pins that a TPDB UUID parsed into `ParsedMovieInfo.StashId` finds the TPDB scene. The parser is unchanged.
- [ ] **Step 2:** Run both fixtures. Expect FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run the fixtures, then the Core suite. Expect PASS.
- [ ] **Step 5:** Commit `Fix: Walk every page of a TPDB catalogue and dedupe by scene foreign id`.

### Task 5: Source API, and the StashDB-only paths made source-aware

**Files:**
- Create: `src/Whisparr.Api.V5/Config/MetadataSourceController.cs` and `MetadataSourceResource.cs`.
- Modify: `src/Whisparr.Api.V5/Config/HostConfigController.cs`. Add a validator rule on `WhisparrMovieMetadataSource`.
- Modify: `src/NzbDrone.Core/Organizer/FileNameBuilder.cs` (:406). Add `{Scene ForeignId}` next to `{StashId}`.
- Modify: `src/NzbDrone.Core/Organizer/NamingConfig.cs` (:15). In the default `SceneFolderFormat`, `{[StashId]}` becomes `{[Scene ForeignId]}`.
- Modify: `src/NzbDrone.Core/Extras/Metadata/Consumers/Xbmc/XbmcMetadata.cs` (:396). The scene URL is `https://stashdb.org/scenes/{ForeignId}` or `https://theporndb.net/scenes/{ForeignId}`, by `MovieMetadata.MetadataSource`.
- Modify: `src/NzbDrone.Core/HealthCheck/Checks/RemovedMovieCheck.cs` (:54). `(stashid {id})` becomes `({id})`.
- Modify: `src/NzbDrone.Core/Notifications/Stash/StashSettings.cs` (:32, :65). A new definition's default `StashBoxEndpoint` is `https://theporndb.net/graphql` when the scene source is TPDB. Update the help text to mention both endpoints.
- Modify: `src/NzbDrone.Core/ImportLists/StashDB/StashDBImportBase.cs` (add a marker interface, `IStashDbImportList`) and `src/NzbDrone.Core/ImportLists/ImportListFactory.cs`. When the scene source is TPDB, StashDB lists are left out of `Active()`, `Enabled()` and the schema/template list.
- Modify: `src/NzbDrone.Core/Localization/Core/en.json`.
- Test: `src/NzbDrone.Api.Test/…/MetadataSourceControllerFixture.cs` (or the nearest existing V5 controller-fixture location), `FileNameBuilderFixture.cs`, `ImportListFactoryFixture.cs` and `XbmcMetadataFixture.cs`.

**Interfaces:**
- Consumes: Task 1's `IMetadataSourceService`.
- Produces:
  - `GET /api/v5/config/metadatasource` returns `{ "sceneMetadataSource": "stashDb"|"tpdb", "movieMetadataSource": "tmdb"|"tpdb"|"none", "locked": bool }`.
  - `PUT /api/v5/config/metadatasource` with `{ sceneMetadataSource, movieMetadataSource }` calls `Choose`. It answers **409** when the sources are already locked and **400** for `none`.
  - The host-config validator fails when `WhisparrMovieMetadataSource` differs from the stored value. Message key: `MetadataSourcesLockedValidation`.
  - The new en.json keys are:
    - `MetadataSourcesLockedValidation`
    - `MetadataSourcesLockedHelpText`, whose text is the roadmap's help tip: stop Whisparr, rename or move `whisparr5.db` (or point to a new Postgres database), then start again to run first-time setup.
    - `SceneMetadataSource`
    - `ChooseMetadataSources`
    - `ChooseMetadataSourcesHelpText`
    - `TpdbScenesEncourageTpdbMovies`
    - `MetadataSourceChoiceIsPermanent`

- [ ] **Step 1: Write the failing tests:**
  - `should_get_sources`
  - `should_choose_sources_when_unlocked`
  - `should_answer_409_when_locked`
  - `should_reject_movie_source_change_in_host_config`
  - `should_render_scene_foreign_id_token`
  - `should_link_tpdb_scene_in_nfo`
  - `should_hide_stashdb_lists_when_source_is_tpdb`
- [ ] **Step 2:** Run the fixtures. Expect FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run the fixtures, then the Core and Api suites. Expect PASS.
- [ ] **Step 5:** Commit `New: Metadata source API and source-neutral naming, links and lists`.

### Task 6: Frontend: first-run picker, locked settings, links

**Files:**
- Create: `frontend/src/Settings/MetadataSource/useMetadataSources.ts`
- Create: `frontend/src/FirstRun/MetadataSourceRequiredModal.tsx` and `MetadataSourceRequiredModalContent.tsx`, plus a `.module.css`.
- Create: `frontend/src/Utilities/MetadataSource/sourceLinks.ts`
- Modify: `frontend/src/Components/Page/Page.tsx` (:107). Render `<MetadataSourceRequiredModal isOpen={authenticationEnabled && !locked} />` after the auth modal.
- Modify: `frontend/src/Settings/General/WhisparrSettings.tsx` (:177–188). The movie-source select becomes read-only text for both sources, with `MetadataSourcesLockedHelpText`.
- Modify: the StashDB link sites in `Scene/Details/SceneDetailsLinks.tsx`, `Studio/Details/StudioDetailsLinks.tsx`, `Performer/Details/PerformerDetailsLinks.tsx`, and the scene, studio and performer add-search results (`AddMovie/AddNewMovie/AddNewScene.tsx`, `Studio/AddNewStudio/AddNewStudio.tsx`, `Performer/AddPerformer/AddNewPerformer.tsx`).
- Modify: `frontend/src/Settings/MediaManagement/Naming/NamingModal.tsx`, adding the `{Scene ForeignId}` token.

**Interfaces:**
- Consumes: Task 5's endpoint.
- Produces:
  ```ts
  export interface MetadataSources { sceneMetadataSource: 'stashDb' | 'tpdb'; movieMetadataSource: 'tmdb' | 'tpdb' | 'none'; locked: boolean }
  export default function useMetadataSources(): { data: MetadataSources | undefined; isFetched: boolean };
  export function useChooseMetadataSources(): { choose: (s: Omit<MetadataSources, 'locked'>) => void; isSaving: boolean; error: unknown };
  export function sceneSourceLink(kind: 'scene' | 'studio' | 'performer', foreignId: string, source: MetadataSources['sceneMetadataSource']): { url: string; label: 'StashDB' | 'TPDB' };
  // stashdb.org/{scenes|studios|performers}/{id}; theporndb.net/{scenes|sites|performers}/{id}
  ```
- **Picker behaviour:**
  - Two radio groups, scene source and movie source. The defaults are StashDB and TMDB.
  - Choosing TPDB for scenes shows the `TpdbScenesEncourageTpdbMovies` alert and preselects TPDB for movies; the user can change it back.
  - A `MetadataSourceChoiceIsPermanent` warning sits above the Save button.
  - Save calls `choose`, then invalidates the `useMetadataSources` query and the general-settings query.
  - The modal can't be dismissed (`closeOnBackgroundClick={false}`, no-op close), just like `AuthenticationRequiredModal`.

- [ ] **Step 1:** Implement, following `FirstRun/AuthenticationRequiredModal*` and `System/Status/useSystemStatus.ts`.
- [ ] **Step 2:** Run `yarn typecheck`, `yarn lint` and `yarn build` from the repo root. Expect zero errors.
- [ ] **Step 3:** Commit `New: First-run metadata source picker and source-aware links`.

### Task 7: Live test, roadmap, push

**Files:**
- Modify: `docs/v5-roadmap.md` §E. Describe it as built, including:
  - the lock;
  - the implicit lock on first add;
  - the API;
  - the TPDB changed feeds;
  - the TPDB works routes and the performer change feed;
  - the remaining TPDB limits: search by `q` only, and no movie sync with TPDB scenes plus TMDB movies;
  - import lists hidden under TPDB, with the GraphQL evidence for why.
- [ ] **Step 1: Build.** Run `dotnet msbuild -restore src/Whisparr.sln -p:Configuration=Debug -p:Platform=Posix -t:PublishAllRids` and `yarn build`, and check that the `_output/net10.0/UI` symlink is in place.
- [ ] **Step 2: TPDB run.**
  - Start the dev instance on a **fresh** data dir under the session scratchpad: `_output/net10.0/Whisparr -nobrowser -data=<dir>`. It uses the live Skyhook `/v5`.
  - Using the driven UI (CDP, as in `whisparr-headless-ui-driving`):
    - The picker appears. Choose TPDB scenes and TPDB movies.
    - Settings → General shows both sources read-only.
    - Add the TPDB site "Brazzers Exxtra". Its scene count should reach Skyhook's `Total`, with no duplicates after a second refresh.
    - Open a scene. Its link goes to theporndb.net.
    - Naming preview renders `{Scene ForeignId}`.
    - Import Lists → Add hides the StashDB lists.
  - Check the API: a `PUT config/metadatasource` answers 409, and a host-config PUT with a different movie source answers 400.
- [ ] **Step 3: StashDB regression.** On a second fresh data dir, choose StashDB and TMDB, add Brazzers Exxtra, and check the scene count matches C's measured run (about 4k) with StashDB links.
- [ ] **Step 4: Full test run.** Run every test project (Core, Common, Api, Host, Mono) with the standard category filter. Expect all to pass.
- [ ] **Step 5: Roadmap.** Commit the roadmap as `Chore: Roadmap E reflects the source lock and TPDB scenes as built`, then push `develop` once plz12345 confirms.
