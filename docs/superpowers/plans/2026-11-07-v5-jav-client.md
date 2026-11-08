# v5 JAV Client Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** JAV becomes a first-class v5 content type. Users can add JAV by code or title, browse it in its own 3:4 index, monitor it per studio and performer, search indexers by code, import files by code, and have v2 JAV studios carried over by the migration.

**Architecture:** JAV is a `Movie` row with `ItemType.Jav` and `MetadataSource.TpdbJav`, fed by Skyhook's `tpdb/jav*` routes. It reuses the scene catalog importer for studio and performer sync, `SceneSearchCriteria` for indexer search (code titles, no date), movie-style folders with its own naming formats, and the Movies index component driven by an `itemType` prop. One availability check (scene source = TPDB) gates every surface.

**Tech Stack:** C# .NET 10 (NzbDrone.Core, Whisparr.Api.V5), FluentMigrator, NUnit/Moq/FluentAssertions, React 19 + TypeScript + CSS Modules.

**Spec:** `docs/superpowers/specs/2026-11-07-v5-jav-client-design.md`

## Global Constraints

- **Branch and landing:** work on branch `jav-client`, which already holds the roadmap note and is rebased on `develop`. `develop` is protected, so this lands by PR.
- **Attribution:** no AI attribution anywhere. Write `plz12345`, never a real name.
- **Public repo:** no infrastructure details (hosts, paths, network names, Skyhook internals beyond public routes).
- **Editing:** use the Edit/Write tools, never sed/perl/python or shell redirection.
- **dotnet:** run with `--disable-build-servers -p:UseSharedCompilation=false`, run `dotnet build-server shutdown` afterwards, and never run two implementers at once.
- **Enum values:** append only. `ItemType.Jav = 4`, `MetadataSource.TpdbJav = 4`, `TpdbIdKind.Jav = 3`, `ImportExclusionType.Jav` goes after the last existing member.
- **Migration:** a single migration `314_add_jav.cs`.
- **Code form:** stored and compared as `UPPER-DIGITS` (`ABP-123`). The normaliser regex is `^(\d{0,4}[A-Za-z]{2,8})[-_ ]?(\d{2,8})$`, copied from Skyhook's `JavCode`.
- **Availability:** `IJavAvailability.IsAvailable()` is `configService.WhisparrSceneMetadataSource == SceneMetadataType.Tpdb`. Every JAV entry point checks it.
- **Translations:** new user-facing strings go in `src/NzbDrone.Core/Localization/Core/en.json` only (flat, PascalCase, alphabetical).
- **Tests:** new backend code needs 80% coverage. Run every test project before the PR (Core, Api, Common, Host, Mono).
- **Frontend checks:** `yarn typecheck`, `yarn lint` and `yarn build` must run clean from the repo root. Mark component props `Readonly<T>` on every touched file.
- **Prerequisite:** the Skyhook `HasJav` field (Eros-Metadata plan `2026-11-07-skyhook-v5-has-jav.md`) must be deployed before the Task 13 live check and the Task 14 smoke test. Unit tests don't need it.

## Review Focus

1. **A StashDB-locked install** that calls JAV endpoints directly (`movie` POST with `itemType: jav`, studio PUT with `javMonitored: true`) gets a 400 validation error, never a stored JAV row. The tests are in Tasks 3 and 5.
2. **Skyhook JAV switch off** (JAV routes answer 503 + `Retry-After`): sync treats it as a throttle and gives up per the walker rules. Refresh of an existing JAV release must not mark it deleted. The test is in Task 3.
3. **`HasJav: null`** (unknown) on a studio refresh keeps the stored value instead of resetting it to false, and the toggle doesn't flicker off. The test is in Task 2.
4. **A file named with only a code** (`abp123.mp4`, `[FHD] ABP-123.mkv`) matches by code even when a scene or movie title contains the same characters. The test is in Task 7.
5. **A v2 JAV series when the user switched to StashDB in the wizard** (v2 itself is TPDB-backed) parks every file with a reason and reports one row. Nothing is silently dropped. The test is in Task 11.

---

### Task 1: JAV type, availability check and schema

**Files:**
- Modify: `src/NzbDrone.Core/Movies/MovieMetadata.cs` (enums at lines ~92-108)
- Modify: `src/NzbDrone.Core/MetadataSource/SkyHook/TpdbIdResolver.cs:13` (`TpdbIdKind`), `src/NzbDrone.Core/ImportLists/ImportExclusions/ImportExclusionType.cs`
- Create: `src/NzbDrone.Core/Movies/Jav/JavAvailability.cs`, `src/NzbDrone.Core/Movies/Jav/JavCode.cs`
- Modify: `src/NzbDrone.Core/Movies/Movie.cs` (add `IsJavLookup()` beside `IsSceneLookup()`)
- Modify: `src/NzbDrone.Core/Movies/Studios/Studio.cs`, `src/NzbDrone.Core/Movies/Performers/Performer.cs`, `src/NzbDrone.Core/Organizer/NamingConfig.cs`
- Create: `src/NzbDrone.Core/Datastore/Migration/314_add_jav.cs`
- Test: `src/NzbDrone.Core.Test/Movies/Jav/JavCodeFixture.cs`, `src/NzbDrone.Core.Test/Movies/Jav/JavAvailabilityFixture.cs`, `src/NzbDrone.Core.Test/Datastore/Migration/314_add_javFixture.cs`

**Interfaces:**
- Produces:
  - `ItemType.Jav` (4), `MetadataSource.TpdbJav` (4), `TpdbIdKind.Jav`, `ImportExclusionType.Jav`
  - `interface IJavAvailability { bool IsAvailable(); }`
  - `static class JavCode { bool TryNormalize(string text, out string code); string SearchVariant(string code); }`. `SearchVariant("ABP-123")` returns `"ABP123"`.
  - `bool Movie.IsJavLookup()`: true when `MovieMetadata.Value.ItemType == Jav` or `MetadataSource == TpdbJav`
  - Studio and Performer: `bool JavMonitored`, `bool HasJav`, `int JavCount`, `int TotalJavCount`. The last two are `[MemberwiseEquals ignore]`/not-stored stats, like `SceneCount`.
  - NamingConfig: `bool RenameJav`, `string StandardJavFormat`, `string JavFolderFormat`
- Migration 314:
  - `Studios`/`Performers`: add `JavMonitored` (bool, default false) and `HasJav` (bool, default false).
  - `NamingConfig`: add `RenameJav` (bool, default true), plus `StandardJavFormat` and `JavFolderFormat` (string, nullable, filled with the defaults below).
  - Index `MovieMetadata(Code)`.
- Defaults, set in `NamingConfig.Default` and in the migration's update:
  - `StandardJavFormat = "{Scene Code} - {Scene CleanTitle} {[Quality Title]}"`
  - `JavFolderFormat = "jav/{Studio CleanTitle}/{Scene Code}"` (Windows: `jav\\{Studio CleanTitle}\\{Scene Code}`)

- [ ] **Step 1: Write failing tests.**
  - `JavCodeFixture`, a `[TestCase]` table for `TryNormalize`:
    - `"abp123"`, `"ABP 123"`, `"ABP_123"`, `"abp-123"` all give `"ABP-123"`.
    - `"300MIUM-1234"` gives `"300MIUM-1234"`.
    - `"Momo Sakai"`, `""`, `null` and `"FC2-PPV-1234567"` give false.
    - `SearchVariant("ABP-123") == "ABP123"`.
  - `JavAvailabilityFixture`: TPDB scene source gives true. StashDb gives false.
  - Migration fixture (pattern of `313_add_import_file_top_matchFixture`): after 314, an existing Studio row has `JavMonitored == false`, and the NamingConfig row has the two default formats.
  - Pin the enum ints with `((int)ItemType.Jav).Should().Be(4); ((int)MetadataSource.TpdbJav).Should().Be(4);`.
- [ ] **Step 2: Run them.** `dotnet test src/NzbDrone.Core.Test/Whisparr.Core.Test.csproj --disable-build-servers -p:UseSharedCompilation=false --filter "FullyQualifiedName~Jav|FullyQualifiedName~314_add_jav"`. Expected: FAIL (the types don't exist).
- [ ] **Step 3: Implement** the Produces list. Add the new Studio/Performer/NamingConfig columns to `TableMapping.cs` only if neighbouring stats are explicitly ignored there (check how `SceneCount` is handled and mirror it).
- [ ] **Step 4: Run the tests.** Same command, PASS. Then run the full Core.Test build to catch exhaustive `switch` warnings-as-errors on the new enum values.
- [ ] **Step 5: Commit.** `git commit -m "New: JAV item type, availability check and schema"`

### Task 2: Skyhook client for JAV

**Files:**
- Modify: `src/NzbDrone.Core/MetadataSource/IProvideMovieInfo.cs`, `src/NzbDrone.Core/MetadataSource/ISearchForNewMovie.cs`
- Modify: `src/NzbDrone.Core/MetadataSource/SkyHook/SkyHookProxy.cs`, at:
  - `MapMovie` ~116
  - `MapMetadataSource` ~225
  - `GetScenePage` ~502
  - `SearchForNewEntity` ~1038
  - `GetStudioInfo` ~818
  - `GetPerformerInfo` ~778
- Modify: `src/NzbDrone.Core/MetadataSource/SkyHook/Resource/StudioResource.cs`, `PerformerResource.cs` (add `bool? HasJav`)
- Modify: `src/NzbDrone.Core/MetadataSource/SkyHook/TpdbIdResolver.cs` (map `TpdbIdKind.Jav` to the kind string `"jav"`)
- Test: `src/NzbDrone.Core.Test/MetadataSource/SkyHook/SkyHookProxyJavFixture.cs` (new, follows `SkyHookProxyScenePageFixture`'s fake-HTTP setup)

**Interfaces:**
- Consumes: Task 1 enums, `JavCode`.
- Produces:
  - `Tuple<MovieMetadata, Studio, List<Performer>, List<Credit>> GetJavInfo(string uuid)`: checks `_scenePrefetch` first, then `GET tpdb/jav/{uuid}`. A 404 throws `MovieNotFoundException`.
  - `ScenePage GetStudioJavPage(string uuid, string cursor)` and `ScenePage GetPerformerJavPage(string uuid, string cursor)`: `tpdb/site|performer/{uuid}/jav/full`, with the same status mapping and prefetch as scene pages. Generalise `GetScenePage(route, id, cursor)` with a `lane` segment (`"scenes"` or `"jav"`). For the JAV lane the route always gets the `tpdb/` prefix.
  - `HashSet<string> GetChangedJav(DateTime startTime)`: `tpdb/jav/changed`, with the same rounding and error rule as `GetChangedSceneSourceIds`.
  - `List<Movie> SearchForNewJav(string term)`: `tpdb/jav/search?q=`.
  - `SearchForNewEntity(term, ItemType.Jav)` routes to `SearchForNewJav`. It skips the studio+date scene path, and a bare UUID or `tpdb:` id goes to `GetJavInfo`.
- `MapMovie` for `ItemType.Jav`:
  - `ForeignId = TpdbId` and `MetadataSource = TpdbJav`.
  - `Code` is normalised with `JavCode.TryNormalize`, falling back to the upper-cased raw code.
  - `Title` loses a leading `"{Code} - "` when the raw title starts with the code (case-insensitive).
  - The studio key is the TPDB id.
- `GetStudioInfo`/`GetPerformerInfo` map Skyhook's `HasJav` onto a new `bool? HasJavReported` property on `Studio` and `Performer`. It is not stored: ignore it in `TableMapping.cs` the way the count stats are ignored. Task 4's refresh services copy it into `HasJav` only when it is non-null.

- [ ] **Step 1: Write failing tests** in `SkyHookProxyJavFixture`:
  - `get_jav_info_maps_type_source_code_and_strips_code_from_title`: the fixture JSON is the live `jav/{uuid}` shape (ItemType `"Jav"`, Code `"ABP-123"`, Title `"ABP-123 - Momo Sakai Perfect Satisfaction Fresh Face Soapland Deluxe"`). The result has `ItemType == Jav`, `MetadataSource == TpdbJav`, `ForeignId == "8e91a563-8a9a-40db-a737-36a663b3ae1f"`, `Code == "ABP-123"` and `Title == "Momo Sakai Perfect Satisfaction Fresh Face Soapland Deluxe"`.
  - `get_jav_info_404_throws_movie_not_found`
  - `studio_jav_page_requests_tpdb_site_jav_full_with_cursor`: asserts the request URL path ends `tpdb/site/{uuid}/jav/full` and carries `cursor=abc`.
  - `studio_jav_page_503_is_throttled`
  - `search_for_new_entity_jav_calls_jav_search_only`: no `scene/search` and no `movie/search` request.
  - `studio_info_maps_has_jav`: three cases, true, false and null, giving `HasJavReported`.
  - `deserializes_unknown_jav_item_type`: guards that `"Jav"` parses through the camel-case enum converter.
- [ ] **Step 2: Run them.** `--filter "FullyQualifiedName~SkyHookProxyJavFixture"`. Expected: FAIL.
- [ ] **Step 3: Implement** the Produces list.
- [ ] **Step 4: Run** the fixture plus all `FullyQualifiedName~SkyHookProxy` tests. PASS, with no regressions in the scene page tests.
- [ ] **Step 5: Commit.** `git commit -m "New: Skyhook client for TPDB JAV"`

### Task 3: Add, refresh and validate JAV releases

**Files:**
- Modify:
  - `src/NzbDrone.Core/Movies/AddMovieService.cs` (`GetMetadata` ~231, exclusion type ~281)
  - `src/NzbDrone.Core/Movies/RefreshMovieService.cs` (dispatch ~295-327, changed feed ~455)
  - `src/NzbDrone.Core/Movies/AddMovieValidator.cs`
- Test: `src/NzbDrone.Core.Test/MovieTests/AddMovieFixture.cs` (or the existing add fixture, matched by name), `src/NzbDrone.Core.Test/MovieTests/RefreshMovieServiceFixture.cs`

**Interfaces:**
- Consumes: `GetJavInfo`, `GetChangedJav`, `Movie.IsJavLookup()`, `IJavAvailability`.
- Behaviour:
  - `GetMetadata` checks `IsJavLookup()` before `IsSceneLookup()` and calls `GetJavInfo(movie.ForeignId)`.
  - Refresh dispatch: `MetadataSource.TpdbJav` calls `GetJavInfo(movie.ForeignId)`.
  - The changed-feed pass adds `GetChangedJav` ids to the scene set only when `IsAvailable()`.
  - The exclusion type for JAV is `ImportExclusionType.Jav`.
  - `AddMovieValidator` gains a rule. When the new movie `IsJavLookup()` and `!IsAvailable()`, it fails with the en.json key `JavRequiresTpdbSceneSource` ("JAV needs the TPDB scene metadata source").
  - A JAV refresh while JAV is unavailable (the source can't change after lock, but rows could arrive from a restore) logs at Warn and skips. It doesn't throw or delete.
  - A `SkyHookException` or HTTP 503 from `GetJavInfo` is transient, as for scenes, never "removed from source".

- [ ] **Step 1: Failing tests.**
  - `add_jav_uses_get_jav_info`
  - `add_jav_rejected_when_scene_source_is_stashdb`: expects a ValidationException containing the key text, and no `GetJavInfo` call.
  - `refresh_tpdbjav_calls_get_jav_info`
  - `refresh_jav_503_does_not_mark_removed`: `GetJavInfo` throws `HttpException` 503, so the status isn't `Deleted` and no `MovieRemovedFromSource` event fires.
  - `changed_feed_includes_jav_only_when_available`
- [ ] **Step 2: Run them.** FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** the touched fixtures. PASS.
- [ ] **Step 5: Commit.** `git commit -m "New: Add and refresh JAV releases"`

### Task 4: Studio and performer JAV monitoring and sync

**Files:**
- Modify: `src/NzbDrone.Core/Movies/Studios/RefreshStudioService.cs` (`SyncStudioItems` ~103, metadata refresh ~90)
- Modify: `src/NzbDrone.Core/Movies/Performers/RefreshPerformerService.cs` (~154-190), `src/NzbDrone.Core/Movies/Performers/Performer.cs` (~81-104 copy/monitor helpers), `src/NzbDrone.Core/Movies/Studios/Studio.cs` (~53 `ApplyChanges`)
- Modify: `src/NzbDrone.Core/Movies/Performers/AddPerformerService.cs:145`, `PerformerAddedHandler.cs:21,33`
- Modify: `src/NzbDrone.Core/Movies/Studios/StudioService.cs:222-228`, `src/NzbDrone.Core/Movies/Performers/PerformerService.cs:263-269` (the JavCount and TotalJavCount stats)
- Test: `src/NzbDrone.Core.Test/StudioTests/RefreshStudioServiceFixture.cs`, `src/NzbDrone.Core.Test/PerformerTests/RefreshPerformerServiceFixture.cs`

**Interfaces:**
- Consumes: `GetStudioJavPage`, `GetPerformerJavPage`, `HasJavReported`, `ISceneCatalogImporter.ImportScenes(ownerTitle, fetchPage, buildStub, keepGoing)`.
- Behaviour:
  - Refresh copies `HasJavReported` into `HasJav` when non-null.
  - Every "anything monitored?" check (`Monitored || MoviesMonitored`) gains `|| JavMonitored`.
  - When `JavMonitored && HasJav && IsAvailable()`, sync calls `ImportScenes(title, c => GetStudioJavPage(ForeignId, c), stub, keepGoing)`. The stub is the scene stub plus `MovieMetadata = new MovieMetadata { ItemType = ItemType.Jav }`. `keepGoing` re-reads the owner and requires `JavMonitored`.
  - The JAV walk runs after the scene walk. A JAV `CatalogIncompleteException`/GaveUp is logged like the scene one.
  - Stats count `ItemType.Jav` into `JavCount` (with file) and `TotalJavCount`.

- [ ] **Step 1: Failing tests.**
  - `jav_monitored_studio_walks_jav_pages`: asserts the fetchPage lambda calls `GetStudioJavPage` and the stub has `ItemType.Jav`.
  - `jav_not_walked_when_has_jav_false`
  - `jav_not_walked_when_unavailable`
  - `has_jav_null_keeps_stored_value`: the stored value is true, the refresh reports null, and it is still true.
  - `has_jav_false_overwrites`
  - `only_jav_monitored_still_syncs` (`Monitored = MoviesMonitored = false`, `JavMonitored = true`)
  - The same three walk cases for performers.
- [ ] **Step 2: Run them.** FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** the Studio and Performer test folders. PASS.
- [ ] **Step 5: Commit.** `git commit -m "New: Monitor JAV per studio and performer"`

### Task 5: V5 API surface

**Files:**
- Modify:
  - `src/Whisparr.Api.V5/Studios/StudioResource.cs`, `StudioEditorResource.cs`, `StudioEditorController.cs`, `StudioController.cs` (validators ~96-103, stats ~396-405), `Helpers/Paging.cs`
  - The same five files under `src/Whisparr.Api.V5/Performers/` (`PerformerController.cs` stats ~416-422)
  - `src/Whisparr.Api.V5/Credits/CreditResource.cs`, `src/NzbDrone.Core/Movies/Credits/CreditRepository.cs:54,91`, `Credit.cs:59` (`JavMonitored` on the credit performer)
  - `src/Whisparr.Api.V5/Movies/MovieController.cs:328` (stats itemType parse)
  - `src/Whisparr.Api.V5/LibrarySearch/LibrarySearchController.cs`, `src/NzbDrone.Core/LibrarySearch/LibrarySearchService.cs:64-77`
  - `src/Whisparr.Api.V5/System/SystemController.cs:106`
  - `src/Whisparr.Api.V5/Config/MetadataSourceResource.cs` (`bool JavAvailable`)
- Test: `src/NzbDrone.Api.Test/v5/...`, beside the existing Studio, Performer and MetadataSource controller fixtures.

**Interfaces:**
- Produces (JSON is camelCase):
  - studio/performer: `javMonitored`, `hasJav`, `javCount`, `totalJavCount`
  - editor: `javMonitored?`
  - credit performer: `javMonitored`
  - `GET /api/v5/movie/stats?itemType=jav`
  - `GET /api/v5/movie/lookup?term=&itemType=jav` (the existing enum binding)
  - library search result bucket `jav` plus a `GET search/jav` paged route mirroring the existing scene and movie ones
  - system status `javCount`
  - `GET /api/v5/config/metadatasource` gains `javAvailable`
- Validators: `javMonitored == true` on PUT, POST or editor fails with `JavRequiresTpdbSceneSource` when unavailable.
- The `MovieController.GetStats` parse becomes a `switch` on `"scene"`, `"jav"` and default `Movie`.

- [ ] **Step 1: Failing tests.**
  - `studio_put_jav_monitored_rejected_under_stashdb`
  - `studio_resource_maps_jav_fields`
  - `stats_itemtype_jav_counts_jav_only`
  - `metadata_source_reports_jav_available`: true for TPDB, false for StashDb.
  - `library_search_returns_jav_bucket`
- [ ] **Step 2: Run them.** `dotnet test src/NzbDrone.Api.Test ... --filter "FullyQualifiedName~v5"`. FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** the Api.Test v5 tests. PASS.
- [ ] **Step 5: Commit.** `git commit -m "New: JAV fields and routes in the v5 API"`

### Task 6: Naming and folders for JAV

**Files:**
- Modify: `src/NzbDrone.Core/Organizer/FileNameBuilder.cs` (~195-230 file, ~292 folder), `FileNameSampleService.cs` (~67-160), `SceneFolderLayout.cs:98`
- Modify: `src/Whisparr.Api.V5/Config/NamingConfigResource.cs`, `NamingConfigController.cs` (validation), `NamingExampleResource.cs` (`JavExample`, `JavFolderExample`)
- Test: `src/NzbDrone.Core.Test/OrganizerTests/FileNameBuilderTests/JavFileNameBuilderFixture.cs`

**Interfaces:**
- Consumes: the NamingConfig fields from Task 1.
- Behaviour:
  - For `ItemType.Jav` the rename toggle is `RenameJav`, the file pattern `StandardJavFormat` and the folder pattern `JavFolderFormat`.
  - Token sets are the studio, scene and scene-title-placeholder tokens (the scene branch). `{Scene Code}` yields the normalised code.
  - `SceneFolderLayout` flat handling stays scene-only, so JAV is never flat.
  - The controller rejects an empty `StandardJavFormat` or `JavFolderFormat`, as it does for the scene formats.
  - The sample has `Code = "ABP-123"`, `Title = "Perfect Satisfaction"` and studio `"Absolutely Perfect"`.

- [ ] **Step 1: Failing tests.**
  - `jav_default_file_name`: gives `"ABP-123 - Perfect Satisfaction [Bluray-1080p]"` with quality Bluray-1080p.
  - `jav_default_folder`: gives `"jav/Absolutely Perfect/ABP-123"`, with the separator per OS as existing tests handle it.
  - `rename_jav_false_keeps_original`
  - `scene_format_unchanged_by_jav`: a regression check on one existing scene case.
- [ ] **Step 2: Run them.** FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** `FullyQualifiedName~OrganizerTests`. PASS.
- [ ] **Step 5: Commit.** `git commit -m "New: JAV naming formats"`

### Task 7: Match releases and files by JAV code

**Files:**
- Modify: `src/NzbDrone.Core/Movies/MovieRepository.cs` (add `FindByJavCode`), `MovieService.cs` (`FindScene` ~730 code branch), `src/NzbDrone.Core/Parser/ParsingService.cs` (~137 lookup selection)
- Test: `src/NzbDrone.Core.Test/MovieTests/MovieRepositoryTests/MovieRepositoryFixture.cs`, `src/NzbDrone.Core.Test/ParserTests/ParsingServiceTests/JavCodeMatchFixture.cs` (new), `src/NzbDrone.Core.Test/ParserTests/ParserFixture.cs` (new `[TestCase]` rows, since parsed codes now feed matching)

**Interfaces:**
- Produces:
  - `Movie MovieRepository.FindByJavCode(string code)`: `ItemType == Jav` and `Code == code` (codes are stored normalised, so the comparison is exact).
  - `IMovieService.FindByJavCode(string code)`.
- Behaviour:
  - When `JavCode.TryNormalize(parsedMovieInfo.Code, out var code)` succeeds and JAV is available, `FindByJavCode(code)` runs first. It runs both in `FindScene` and when the search criteria's movie is JAV.
  - A hit returns immediately with match type `Episode` (the existing code-match type).
  - When the search criteria's movie is JAV, a miss returns null and never falls through to scene or movie title matching.
  - Outside a JAV search, a miss falls through to today's logic unchanged.
- `ParserFixture` rows record what `Parser.ParseMovieTitle(...).Code` gives today for:
  - `"ABP-123.mp4"`
  - `"[FHD] ABP-123.mkv"`
  - `"abp-123 Momo Sakai 1080p.mp4"`
  - `"[ABP-123] Perfect Satisfaction.mkv"`

  If a row shows the parser misses a form the spec needs, fix the regex in the same task and add the row. No parser change ships without its row.

- [ ] **Step 1: Failing tests.**
  - The repository test: `find_by_jav_code_ignores_scene_with_same_code`.
  - `JavCodeMatchFixture`:
    - `code_only_file_matches_jav`
    - `lowercase_nodash_code_matches`
    - `jav_search_miss_does_not_title_match_scene` (Review Focus 4)
    - `non_jav_search_falls_through_unchanged`
    - `unavailable_skips_code_lookup`
  - Plus the `ParserFixture` rows.
- [ ] **Step 2: Run them.** `--filter "FullyQualifiedName~ParserTests|FullyQualifiedName~MovieRepositoryFixture"`. FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** all of `ParserTests` plus `MovieTests`. PASS.
- [ ] **Step 5: Commit.** `git commit -m "New: Match releases and files by JAV code"`

### Task 8: Indexer search for JAV

**Files:**
- Modify: `src/NzbDrone.Core/IndexerSearch/ReleaseSearchService.cs` (`MovieSearch` ~58)
- Test: `src/NzbDrone.Core.Test/IndexerSearchTests/ReleaseSearchServiceFixture.cs`

**Interfaces:**
- Behaviour: for `ItemType.Jav`, build `SceneSearchCriteria` with `SceneTitles = [code, JavCode.SearchVariant(code)]`, `ReleaseDate = null`, `SiteTitle = null`, then dispatch. Title, date and studio expansion are skipped. A JAV release with no code falls back to its title alone.

- [ ] **Step 1: Failing tests.**
  - `jav_search_uses_code_titles_without_date`: SceneTitles equal `["ABP-123","ABP123"]` and ReleaseDate is null.
  - `jav_without_code_searches_title`
- [ ] **Step 2: Run them.** FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** `FullyQualifiedName~IndexerSearchTests`. PASS.
- [ ] **Step 5: Commit.** `git commit -m "New: Search indexers for JAV by code"`

### Task 9: Import, scan and the other ItemType branches

Every existing `ItemType` branch gets a JAV decision. The rule: **JAV is a movie for files, folders and import, and a scene for studio and credit display.** The table is the full list from `rg 'ItemType\.(Scene|Movie)'` at plan time. The implementer re-runs that grep and adds any new hit to this table in the commit.

| Site | JAV behaves as |
|---|---|
| `MediaFiles/MovieImport/ImportDecisionMaker.cs:150,476` | Movie |
| `MediaFiles/DiskScanService.cs:259,504` | Movie |
| `MediaFiles/MovieImport/Manual/ManualImportService.cs:705` | Movie |
| `MediaFiles/MediaFileDeletionService.cs:261`, `SharedFolderScanner.cs:137`, `Fingerprints/LinkedFileVerifier.cs:52` | Movie (not flat, not shared folder, no fingerprint) |
| `Validation/Paths/MoviePathValidation.cs:39`, `ValidatedItemType.cs:12,14` | Movie |
| `Movies/Movie.cs:121` `ToFormattedString` | `"{Code} - {Title}"` |
| `HealthCheck/Checks/RemovedMovieCheck.cs:32` | its own JAV list and message (en.json `RemovedJavCheckMessage`) |
| `Notifications/NotificationService.cs:54`, `Webhook/WebhookBase.cs:223` | Webhook `ItemType = "Jav"`. Notification text as scene. |
| `Statistics/StatisticsRepository.cs:212`, `MovieStats/MovieStatisticsRepository.cs` | a separate `jav` bucket |
| `Parser/ParsingService.cs:202-238` | already handled in Task 7 |
| `MovieService.cs:1140` `FindByYear(ItemType.Movie…)` | unchanged (movies only) |

**Files:** the table's files. Test: one focused test per changed behaviour, in each file's existing fixture.

- [ ] **Step 1: Failing tests.**
  - `jav_import_uses_movie_folder_rules` (ImportDecisionMaker)
  - `disk_scan_treats_jav_as_movie_folder`
  - `removed_check_lists_jav_separately`
  - `webhook_item_type_jav`
  - `statistics_counts_jav_bucket`
- [ ] **Step 2: Run them.** FAIL.
- [ ] **Step 3: Implement** per the table.
- [ ] **Step 4: Run** the whole Core.Test project (`TestCategory!=IntegrationTest&TestCategory!=AutomationTest&TestCategory!=ManualTest`). PASS.
- [ ] **Step 5: Commit.** `git commit -m "New: Import and scan JAV like movies"`

### Task 10: Unmapped Files suggestions for JAV

**Files:**
- Modify: `src/NzbDrone.Core/RootFolders/UnmappedFileService.cs` (~102), `src/NzbDrone.Core/RootFolders/FindUnmappedFileMatchesService.cs:112`, `src/Whisparr.Api.V5/UnmappedFiles/UnmappedFileController.cs:54`
- Test: `src/NzbDrone.Core.Test/RootFolderTests/UnmappedFileServiceFixture.cs`

**Interfaces:**
- Behaviour:
  - When a candidate's parsed code normalises and JAV is available, search `ItemType.Jav` with the normalised code first and put those results ahead of the scene results.
  - `Suggest(id, term, itemType)` accepts `ItemType.Jav`.
  - Top-match uses the same order.

- [ ] **Step 1: Failing tests.**
  - `jav_code_file_suggests_jav_first`
  - `jav_unavailable_suggests_scenes_only`
- [ ] **Step 2: Run them.** FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** `FullyQualifiedName~RootFolderTests`. PASS.
- [ ] **Step 5: Commit.** `git commit -m "New: Suggest JAV for unmapped files with a code"`

### Task 11: v2 migration of JAV studios

**Files:**
- Modify:
  - `src/NzbDrone.Core/LegacyMigration/V2TpdbImportRunner.cs` (`Import` ~56, `ResolveScenes` ~130)
  - `src/NzbDrone.Core/LegacyMigration/V2StashImportRunner.cs` (`Import` ~66)
  - `src/NzbDrone.Core/LegacyMigration/V2SeriesImportRunnerBase.cs` (`ApplyV2Settings` ~204)
  - `src/NzbDrone.Core/LegacyMigration/LegacyV2Series.cs` (add `bool IsJav => SeriesType == 1`)
- Test: `src/NzbDrone.Core.Test/LegacyMigration/V2TpdbImportRunnerFixture.cs`, `V2StashImportRunnerFixture.cs`

**Interfaces:**
- Consumes: `TpdbIdKind.Jav`, `GetStudioJavPage`, `ImportScenes`, and the stub rule from Task 4.
- Behaviour, under TPDB with `series.IsJav`:
  - The site resolves as today.
  - Episode ids resolve with `TpdbIdKind.Jav`.
  - The catalog walk uses `GetStudioJavPage`.
  - Stubs get `ItemType.Jav`. Missing ids are added with the JAV stub.
  - Files link as today.
  - `ApplyV2Settings` sets `JavMonitored = series.Monitored` and leaves `Monitored` false. It also sets `HasJav = true`, since v2 knew the studio was JAV.
- Behaviour, under StashDB with `series.IsJav`: write one `NeedsAttention` row in the `Studios` area with detail `$"JAV needs the TPDB scene source; {wanted} wanted, {files.Count} files parked"`, where `wanted` counts monitored episodes with no file. Then call `ParkAndFinish(series, episodes, files, "JAV needs the TPDB scene source", progress)`.
- **Assumption to verify:** v2 JAV series store the TPDB site's numeric id in `TvdbId`, as other series do. Verify against gykes' mixed-install backup before the PR. If it's wrong, record a ruling and adjust `ResolveSite` for JAV.

- [ ] **Step 1: Failing tests.**
  - `tpdb_jav_series_resolves_with_jav_kind_and_walks_jav_pages`
  - `tpdb_jav_series_sets_jav_monitored_not_scene_monitored`
  - `tpdb_jav_file_links_to_jav_release`
  - `stash_jav_series_parks_all_files_and_reports_one_row` (Review Focus 5)
  - `non_jav_series_unchanged`: a regression check on one existing case.
- [ ] **Step 2: Run them.** `--filter "FullyQualifiedName~LegacyMigration"`. FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** `FullyQualifiedName~LegacyMigration`. PASS.
- [ ] **Step 5: Commit.** `git commit -m "New: Migrate v2 JAV studios"`

### Task 12: Frontend: JAV section, index and Add JAV

**Files:**
- Modify:
  - `frontend/src/Settings/MetadataSource/useMetadataSources.ts`: add `useJavAvailable(): boolean` from `javAvailable`
  - `frontend/src/Components/Page/Sidebar/PageSidebar.tsx`: a JAV section after Movies, `{ title: translate('Jav'), to: '/jav', children: [{ title: translate('AddNew'), to: '/add/new/jav' }] }`, hidden when `!useJavAvailable()`
  - `frontend/src/App/AppRoutes.tsx`: `/jav` gives `<MovieIndex itemType="jav" />`, `/add/new/jav` gives `<AddNewJav />`
- Modify: `frontend/src/Movie/Index/MovieIndex.tsx`, `useMovieIndexQuery.ts:45`, `useMovieStats.ts:13`, `Posters/MovieIndexPosters.tsx:55`, `Posters/MovieIndexPoster.tsx`, `Select/MovieIndexSelectFooter.tsx:214`
  - These take `itemType: 'movie' | 'jav'`, defaulting to `'movie'`.
  - The options store comes from `itemType`: `movieIndexOptionsStore` or a new `frontend/src/Movie/Index/javIndexOptionsStore.ts` (`createOptionsStore('jav_index_options', …)`, same defaults).
  - JAV poster height is `Math.ceil((4 / 3) * posterWidth)`.
  - The JAV poster image class is `object-fit: cover; object-position: right`.
- Create: `frontend/src/AddMovie/AddNewMovie/AddNewJav.tsx`, following `AddNewScene.tsx` and passing `itemType="jav"`. The search placeholder is `translate('JavSearchPlaceholder')` ("Code or title, e.g. ABP-123").
- Modify: `frontend/src/Movie/Movie.ts:75` (`itemType: 'movie' | 'scene' | 'jav'`), plus the `'movie' | 'scene'` unions in `UnmappedFiles/useUnmappedFiles.ts:12`, `DeleteMovieFilesModalContent.tsx:16` and `ImportMovieTypes.ts:44` where JAV can appear. Each of the ~28 `itemType === 'scene'|'movie'` sites follows Task 9's rule: movie for files and folders, scene for studio and credit display.
- Modify: `frontend/src/Movie/Details/MovieDetails.tsx`: the JAV poster gets the same right crop, and the code shows before the title.
- Modify: `en.json` keys `Jav`, `JavSearchPlaceholder`, `NoJav` (the empty-index text).

- [ ] **Step 1: Implement**, then run `yarn typecheck && yarn lint && yarn build` from the repo root and expect zero errors.
- [ ] **Step 2: Live check** against a local TPDB-locked instance on 6969:
  - The JAV nav shows.
  - `/add/new/jav` searching `ABP-123` returns only JAV.
  - Adding it shows a 3:4 tile with the front cover visible.
  - A StashDB-locked instance (scratch DB) shows no JAV nav.
  - Turn on SFW mode before any screenshot.
- [ ] **Step 3: Commit.** `git commit -m "New: JAV section, index and Add JAV"`

### Task 13: Frontend: studio and performer JAV toggle, editors, naming settings

**Files:**
- Modify:
  - `frontend/src/Studio/Studio.ts`, `frontend/src/Performer/Performer.ts`, `frontend/src/typings/MovieCredit.ts`: add `javMonitored`, `hasJav`, `javCount`, `totalJavCount`
  - `frontend/src/Studio/Details/StudioDetails.tsx`, `frontend/src/Performer/Details/PerformerDetails.tsx`: a third `MonitorToggleButton` labelled `translate('MonitorJav')`, shown when `useJavAvailable() && hasJav`. It patches `javMonitored` the way the `moviesMonitored` toggle does in `useStudioDetails.ts`/`usePerformerDetails.ts`.
  - `Studio/Edit/EditStudioModalContent.tsx`, `Performer/Edit/EditPerformerModalContent.tsx`, `Studio/Index/Select/Edit/EditStudiosModalContent.tsx`, `Performer/Index/Select/Edit/EditPerformersModalContent.tsx` and their `useEdit*ModalMutation.ts`: a `javMonitored` field, under the same gate
  - `studioIndexFilters.ts`, `studioIndexFilterBuilderProps.ts`, `performerIndexFilters.ts`, `performerIndexFilterBuilderProps.ts`: a `javMonitored` boolean filter
  - `frontend/src/Settings/MediaManagement/Naming/Naming.tsx`: `renameJav`, `standardJavFormat` and `javFolderFormat` fields with examples, shown only when JAV is available
  - `en.json` keys: `MonitorJav`, `RenameJav`, `RenameJavHelpText`, `StandardJavFormat`, `JavFolderFormat`
- [ ] **Step 1: Implement**, then run `yarn typecheck && yarn lint && yarn build` and expect zero errors.
- [ ] **Step 2: Live check.**
  - Open Absolutely Perfect (TPDB `25d9ae63-ad98-457e-9dd0-cdc4bae7ce35`, which has JAV, 688 releases). The JAV toggle shows.
  - Turning it on walks the JAV catalog: the JAV index fills, and the studio's JAV count rises.
  - A western-only studio shows no JAV toggle.
  - The naming page saves the JAV formats.
- [ ] **Step 3: Commit.** `git commit -m "New: Monitor JAV from studios and performers"`

### Task 14: End-to-end smoke test and roadmap

- [ ] **Step 1: Run every test project.** That's Core, Api, Common, Host and Mono, all with build servers disabled. Then run `dotnet build-server shutdown`. Everything must pass.
- [ ] **Step 2: Smoke test on 6969 (TPDB-locked).**
  1. Add `ABP-123` monitored. An interactive search shows releases queried by code.
  2. Drop a file named `abp123.mp4` into a download folder and import it. It lands at `jav/Absolutely Perfect/ABP-123/ABP-123 - … .mp4`.
  3. The Calendar and Wanted Missing pages list JAV.
  4. Unmapped Files suggests JAV for a code-named file.
- [ ] **Step 3: Update the roadmap.** Edit the JAV section of `docs/v5-roadmap.md`: replace "Client side still to come" with what shipped and the out-of-scope list from the spec.
- [ ] **Step 4: Commit.** `git commit -m "Chore: Note the v5 JAV client in the roadmap"`
