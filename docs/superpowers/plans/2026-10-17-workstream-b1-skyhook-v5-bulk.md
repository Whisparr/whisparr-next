# Workstream B1: Skyhook v5 Routing and Paged Bulk Endpoints Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:**
- Skyhook answers under `/v5`.
- It serves full scene objects for a studio or a performer in cursor-paged, Cloudflare-cacheable pages.
- It hydrates performers in bulk.

These are the endpoints that Workstream C's bulk onboarding and A's held Task 9 depend on.

**Architecture:**
- **Scene keys:** the StashDB studio/performer scene-id queries also fetch `release_date`, so each scene becomes a sortable key.
- **`ScenePageService`:** a new service in `Backend/SkyHook/`. It:
  - caches a site's or performer's sorted key list in memory for an hour;
  - slices that list after an opaque keyset cursor;
  - hydrates the slice through the existing `ISkyHook.GetScenes`, which already fills cache gaps from upstream.
- **Controllers:** two new GET actions return the page with a TTL per page. A POST action wraps `ISkyHook.GetPerformers` for bulk performer hydration.
- **`/v5` routing:** it's a proxy rule outside this repo. In here it's only a second `Cloudflare.Sites` entry, so purges also cover `/v5`.

**Tech Stack:** ASP.NET Core (.NET), StashDB GraphQL, `Microsoft.Extensions.Caching.Memory`, NUnit 3 + Moq.

**Spec:** `docs/v5-roadmap.md` in whisparr-next, section "B. Skyhook v5", the **Routing** and **Cursor-paged bulk endpoints** parts.

The roadmap's other two B parts each get their own plan:
- **B2:** the TPDB scene path, which E needs. It also covers "include TPDB-linked movies in performer scenes"; B1's performer pages are StashDB scenes only.
- **B3:** fingerprints, which D needs. D is blocked on R2 access.

## Global Constraints

- **Repo and branch:** the repo is `~/Git/Whisparr-Eros-Metadata`, and its upstream branch is `v4`.
  - Work on a feature branch `v5-bulk` with one commit per task.
  - Before the branch is pushed, build the image locally with the repo `Dockerfile` and test it in a container (see Finish).
  - The branch lands on `v4` only through a PR. Never push straight to `v4`.
- **Commits:** follow the repo's style (`new:`, `fix:`, `chore:`). No AI attribution.
- **Docs:** never write a real name or `/Users/...` path; use `plz12345` and `~/`.
- **Routes:** new routes carry no `v5/` prefix in code. Controllers keep `[Route("")]`.
- **Page size:** default `limit` 250, maximum 1000.
- **Ordering:** release date ascending, then scene ID (ordinal) ascending. Scenes with no release date sort after all dated scenes.
  - Ascending keeps new scenes at the end, so earlier pages stay valid and only the last page changes.
- **Cache-Control TTL:**
  - a page with a next cursor: 24 hours;
  - the last page (no next cursor): 1 hour.
  - Use the existing `public,s-maxage=N,max-age=0` format.
- **Performer bulk:** at most 100 IDs per request.
- **Tests:** NUnit fixtures in `src/Backend.Test`, following `src/Backend.Test/API/SceneControllerFixture.cs` (Moq, a `DefaultHttpContext` on the controller).
  - Run with: `dotnet test src/Backend.Test/Backend.Test.csproj --filter "FullyQualifiedName~<Fixture>"`
  - Run the full project before each commit: `dotnet test src/Backend.Test/Backend.Test.csproj`

## Review Focus

1. **A cursor whose scene has since left the list** (deleted upstream, or moved to another studio). The next page must carry on from the cursor's key position, without skipping or repeating scenes. Pinned in Task 2.
2. **A scene ID in the slice that upstream no longer has** (`GetScenes` drops it). The page returns the scenes it can, with fewer than `limit` items. `nextCursor` still advances past the missing ID, so the client can't loop. Pinned in Task 2.
3. **A huge studio (16K scenes).** The upstream key list is fetched once per hour per site, not on every page request. Pinned in Task 2, which checks the proxy was called once across two page calls.
4. **Garbage input:**
  - a non-GUID id → 404;
  - a malformed or tampered cursor → 400, not 500;
  - `limit` of 0, a negative number, or above 1000 → clamped to 1..1000.

  Pinned in Task 3.
5. **Performer bulk with duplicate, invalid or more than 100 IDs.** Duplicates are collapsed and non-GUIDs dropped. More than 100 IDs after cleaning → 400. Pinned in Task 4.

---

### Task 1: Scene keys from StashDB

**Files:**
- Create: `src/Backend/MetadataSource/StashDb/SceneKey.cs`
- Modify:
  - `src/Backend/MetadataSource/StashDb/Requests/GetStudioScenesRequest.cs`: change the selection to `scenes { id, release_date, studio { id } }`.
  - `src/Backend/MetadataSource/StashDb/Requests/GetPerformerScenesRequest.cs`: add `release_date` to its scene selection.
  - `src/Backend/MetadataSource/StashDb/StashDbProxy.cs`: add two interface methods and their implementations. Factor the existing page loop out of `GetSiteScenes`/`GetPerformerScenes` so the old ID methods and the new key methods share it.
- Test: create `src/Backend.Test/MetadataSource/StashDbSceneKeyFixture.cs`.

**Interfaces:**
- Produces:

```csharp
public sealed record SceneKey(string Id, string ReleaseDate); // ReleaseDate "yyyy-MM-dd" or null
// IStashDbProxy
Task<List<SceneKey>> GetSiteSceneKeys(string guid);
Task<List<SceneKey>> GetPerformerSceneKeys(string guid);
```

- [ ] **Step 1: Write the failing tests.** Mock `IStashDbRequestService` so it returns two pages: `count` 3 and `PerPage` clamped, as `StashDbProxy` does today.

```csharp
[Test] public async Task should_return_id_and_release_date_for_every_studio_scene()
// keys == [("a","2020-01-02"), ("b",null), ("c","2019-05-05")] in upstream order, across both pages
[Test] public async Task should_return_id_and_release_date_for_every_performer_scene()
[Test] public async Task should_keep_GetSiteScenes_returning_ids_only()
// GetSiteScenes(guid) == ["a","b","c"]
[Test] public void should_request_release_date_in_both_scene_id_queries()
// new GetStudioScenesRequest(g,1,100).Body and new GetPerformerScenesRequest(g,1,100).Body both contain "release_date"
```

- [ ] **Step 2: Run them to verify they fail.**
  Run: `dotnet test src/Backend.Test/Backend.Test.csproj --filter "FullyQualifiedName~StashDbSceneKeyFixture"`
  Expected: build failure, because `SceneKey` and `GetSiteSceneKeys` aren't defined yet.

- [ ] **Step 3: Implement it as described under Files.**

- [ ] **Step 4: Run the tests to verify they pass.** Run the Step 2 command, then the full Backend.Test project.
  Expected: PASS.

- [ ] **Step 5: Commit.**

```bash
git commit -am "new: StashDB scene keys with release date"
```

### Task 2: ScenePageService with keyset cursor

**Files:**
- Create: `src/Backend/SkyHook/SceneCursor.cs`
- Create: `src/Backend/SkyHook/ScenePageService.cs`
- Modify: `src/API/Startup.cs`:
  - `services.AddMemoryCache()`, unless it's already registered;
  - `services.AddSingleton<IScenePageService, ScenePageService>()`.
- Test: create `src/Backend.Test/SkyHook/SceneCursorFixture.cs` and `src/Backend.Test/SkyHook/ScenePageServiceFixture.cs`.

**Interfaces:**
- Consumes: `SceneKey` and `IStashDbProxy.GetSiteSceneKeys`/`GetPerformerSceneKeys` (Task 1), and the existing `ISkyHook.GetScenes(List<string>)`.
- Produces:

```csharp
public static class SceneCursor
{
    public static string Encode(SceneKey key);          // base64url of "<releaseDate or empty>|<id>"
    public static SceneKey Decode(string cursor);       // throws FormatException on anything malformed
}

public sealed class ScenePage
{
    public int Total { get; init; }                    // size of the full key list
    public string NextCursor { get; init; }            // null on the last page
    public List<Episode> Items { get; init; }
}

public interface IScenePageService
{
    Task<ScenePage> GetSiteScenePage(string siteGuid, string cursor, int limit);
    Task<ScenePage> GetPerformerScenePage(string performerGuid, string cursor, int limit);
}
```

- [ ] **Step 1: Write the failing tests.**

```csharp
// SceneCursorFixture
[TestCase("2020-01-02", "abc")] [TestCase(null, "abc")]
public void should_round_trip(string date, string id)  // Decode(Encode(k)) == k
[TestCase("")] [TestCase("not-base64!")] [TestCase("bm9waXBl")] /* "nopipe" */
public void should_reject_malformed_cursor(string c)   // Assert.Throws<FormatException>

// ScenePageServiceFixture: keys d1..d5 with dates 2020-01-01..2020-01-04 plus one null date, given to the proxy unsorted
[Test] public async Task should_order_by_release_date_then_id_with_undated_last()
[Test] public async Task should_return_next_cursor_until_the_last_page()     // limit 2 → pages of 2,2,1; last NextCursor null; Total 5
[Test] public async Task should_continue_after_a_cursor_whose_scene_left_the_list()
    // page 1 (limit 2) → cursor; key list changes (2nd item removed); next page starts at the first key > cursor key
[Test] public async Task should_advance_past_scenes_upstream_no_longer_has()
    // GetScenes returns 1 of the 2 requested; Items.Count == 1; NextCursor encodes the 2nd requested key
[Test] public async Task should_preserve_key_order_in_items()                // GetScenes returns reversed; Items follow key order
[Test] public async Task should_fetch_the_key_list_once_per_site_within_an_hour()
    // two page calls for the same site → GetSiteSceneKeys called once (real MemoryCache)
[Test] public async Task should_page_performer_scenes_the_same_way()
```

- [ ] **Step 2: Run them to verify they fail.**
  Run: `dotnet test src/Backend.Test/Backend.Test.csproj --filter "FullyQualifiedName~SceneCursor|FullyQualifiedName~ScenePageService"`
  Expected: build failure, because the types aren't defined yet.

- [ ] **Step 3: Implement.**
  - **Sort key:** `(ReleaseDate ?? "9999-99-99", Id)`, compared ordinally. ISO dates sort correctly as strings.
  - **Slice:** the first `limit` keys whose sort key is strictly greater than the decoded cursor's sort key. Use a binary search on the sorted list.
  - **`NextCursor`:** `Encode(lastKeyInSlice)` when keys remain after the slice, otherwise null. It's based on the slice, not on the hydrated items.
  - **Memory cache:** key it `site:{guid}` / `performer:{guid}`, with a 1-hour absolute expiry.
  - **`limit`:** arrives already clamped (Task 3), so don't clamp it again here.

- [ ] **Step 4: Run the tests to verify they pass.** Run the Step 2 command, then the full project.
  Expected: PASS.

- [ ] **Step 5: Commit.**

```bash
git commit -am "new: Keyset-paged scene pages for sites and performers"
```

### Task 3: `scenes/full` endpoints

**Files:**
- Create: `src/API/Resources/ScenePageResource.cs`, with `int Total`, `string NextCursor`, `List<Episode> Items`. It serializes camelCase like the other resources.
- Modify:
  - `src/API/Controllers/SiteController.cs`: inject `IScenePageService` and add the action after `GetSiteScenes`.
  - `src/API/Controllers/PerformerController.cs`: inject `IScenePageService` and add the action after `GetPerformer`.
  - Update every place the existing fixtures construct these controllers.
- Test: create `src/Backend.Test/API/SceneFullControllerFixture.cs`.

**Interfaces:**
- Consumes: `IScenePageService`, `ScenePage` and `SceneCursor` (Task 2).
- Produces:
  - `GET site/{guid}/scenes/full?cursor=&limit=` → `ScenePageResource`;
  - `GET performer/{guid}/scenes/full?cursor=&limit=` → `ScenePageResource`.

- [ ] **Step 1: Write the failing tests.**

```csharp
[TestCase("not-a-guid")] [TestCase("")]
public async Task should_404_for_a_bad_site_guid(string guid)
[Test] public async Task should_400_for_a_malformed_cursor()            // service throws FormatException → 400, nothing thrown out
[TestCase(null, 250)] [TestCase(0, 1)] [TestCase(-5, 1)] [TestCase(5000, 1000)] [TestCase(40, 40)]
public async Task should_clamp_limit(int? limit, int expected)           // service called with expected
[Test] public async Task should_cache_a_middle_page_for_24_hours()      // NextCursor != null → Cache-Control "public,s-maxage=86400,max-age=0"
[Test] public async Task should_cache_the_last_page_for_1_hour()        // NextCursor == null → "public,s-maxage=3600,max-age=0"
[Test] public async Task should_normalise_the_guid_to_lowercase()       // service receives guid.ToLowerInvariant()
[Test] public async Task should_serve_performer_scenes_full_the_same_way()  // 404 / 400 / TTL on PerformerController
```

- [ ] **Step 2: Run them to verify they fail.**
  Run: `dotnet test src/Backend.Test/Backend.Test.csproj --filter "FullyQualifiedName~SceneFullControllerFixture"`
  Expected: build failure, because the actions don't exist yet.

- [ ] **Step 3: Implement.**
  - Set `RequestPriorityContext.CurrentPriority.Value = 7`, the same as `GetSiteScenes`.
  - Format the TTL with each controller's existing `GetCacheControlHeader(DateTime)`: `UtcNow + 24h`, or `UtcNow + 1h` on the last page. The test compares whole seconds, so compute the header from a fixed `TimeSpan` rather than `DateTime` arithmetic, or allow ±1s in the assertion.
  - Map `TPDbApiException`/`StashDbApiException` 404 → 404, as the neighbouring actions do.

- [ ] **Step 4: Run the tests to verify they pass.** Run the Step 2 command, then the full project.
  Expected: PASS.

- [ ] **Step 5: Commit.**

```bash
git commit -am "new: site and performer scenes/full paged endpoints"
```

### Task 4: `POST performer/bulk`

**Files:**
- Modify: `src/API/Controllers/PerformerController.cs`. Add the action next to `RefreshPerformerCache`. It has no API key: it's a public read, like `performer/{guid}`.
- Test: create `src/Backend.Test/API/PerformerBulkControllerFixture.cs`.

**Interfaces:**
- Consumes: the existing `ISkyHook.GetPerformers(List<string>)`, which reads from cache and fills misses.
- Produces: `POST performer/bulk`, with a body of `["guid", ...]` → `List<Performer>`.

- [ ] **Step 1: Write the failing tests.**

```csharp
[Test] public async Task should_return_performers_for_the_given_ids()
[Test] public async Task should_drop_duplicates_and_non_guids_before_fetching()  // ["A","a","x"] → GetPerformers(["a"])
[Test] public async Task should_400_above_100_ids_after_cleaning()               // 101 distinct guids → 400, GetPerformers never called
[Test] public async Task should_return_empty_for_an_empty_or_null_body()         // 200, [], GetPerformers never called
```

- [ ] **Step 2: Run them to verify they fail.**
  Run: `dotnet test src/Backend.Test/Backend.Test.csproj --filter "FullyQualifiedName~PerformerBulkControllerFixture"`
  Expected: build failure, because the action doesn't exist yet.

- [ ] **Step 3: Implement.** Lowercase each ID, keep those that pass `Guid.TryParse`, and remove duplicates. Use priority 7.

- [ ] **Step 4: Run the tests to verify they pass.** Run the Step 2 command, then the full project.
  Expected: PASS.

- [ ] **Step 5: Commit.**

```bash
git commit -am "new: POST performer/bulk for deferred performer hydration"
```

### Task 5: `/v5` purge site and deployment runbook

**Files:**
- Modify: `src/API/appsettings.json:47-53` and `src/API/appsettings.Development.json:46-52`. Add a second `Sites` entry `{ "BaseUrl": "https://api.whisparr.com/v5", "ZoneId": "<same ZoneId as the v4 entry>" }`.
- Test: create `src/Backend.Test/Cloudflare/CloudflareSitesFixture.cs`.
- Create: `docs/v5-deploy-runbook.md` in **whisparr-next** (not this repo), the handover for plz12345.

**Interfaces:**
- Produces: every Cloudflare purge covers both `https://api.whisparr.com/v4/...` and `.../v5/...`.

- [ ] **Step 1: Write the failing test.**

```csharp
[Test] public void should_configure_v4_and_v5_purge_sites()
// bind CloudflareOptions from src/API/appsettings.json (copy it to the test output or read it by relative path):
// Sites.Select(s => s.BaseUrl) == ["https://api.whisparr.com/v4", "https://api.whisparr.com/v5"], same ZoneId
[Test] public async Task should_purge_both_sites_for_a_scene()
// CloudflareProxy with two Sites and a mocked HTTP handler: invalidating scene "x" posts
// files containing ".../v4/scene/x" and ".../v5/scene/x"
```

- [ ] **Step 2: Run them to verify they fail.**
  Run: `dotnet test src/Backend.Test/Backend.Test.csproj --filter "FullyQualifiedName~CloudflareSitesFixture"`
  Expected: FAIL, because only one site is configured.

- [ ] **Step 3: Add the entries and write the runbook.** The runbook says, in order:
  1. Add a `/v5` → strip-prefix rule next to the existing `/v4` rule, wherever the proxy strips `/v4` today.
  2. Deploy the image.
  3. Run `docker compose up -d`, not `restart`, so the new configuration is picked up.
  4. Check that `curl -s -o /dev/null -w '%{http_code}' https://api.whisparr.com/v5/site/search?q=brazzers` prints `200`, with the same body as `/v4`.
  5. Check that `curl -sI 'https://api.whisparr.com/v5/site/<a known studio guid>/scenes/full?limit=2'` shows `cache-control: public,s-maxage=86400,max-age=0`.
  6. Once these pass, A's held Task 9 (the app's Skyhook base URL `/v5`) can start.

- [ ] **Step 4: Run the tests to verify they pass, then the full Backend.Test project.**
  Expected: PASS.

- [ ] **Step 5: Commit both repos.**

```bash
# Eros-Metadata (local v5-bulk branch)
git commit -am "chore: Purge /v5 alongside /v4"
# whisparr-next (develop)
git add docs/v5-deploy-runbook.md && git commit -m "Chore: Skyhook v5 deploy runbook"
```

---

## Finish

- [ ] Run the full `src/Backend.Test` project and build the solution. Both must pass with no new warnings.
- [ ] **Local container test.**
  - Start the compose DB: `docker compose up -d db` (postgres on port 5437).
  - Build the image: `docker build -t whisparr-metadata:v5-bulk .`
  - Run it against the compose DB. Pass the connection settings and the StashDB credentials from `appsettings.Development.json` as environment variables, and expose port 8080.
  - Then check:
    1. Page through a large studio with `limit=1000`. `Total` equals the number of items across all pages, every ID is unique, and the last page has `nextCursor: null`.
    2. A middle page returns `cache-control: public,s-maxage=86400,max-age=0`, and the last page `s-maxage=3600`.
    3. `POST performer/bulk` with a few performer IDs from those scenes returns them.
  - Stop and remove the container afterwards.
  - If no StashDB credentials are available, run what works without them (a 404 for a bad GUID, a 400 for a bad cursor), and say what was skipped.
- [ ] Push `v5-bulk` and open a PR into `v4` using the repo's conventions. The PR body lists the commits, the test results and the container check, and links the runbook. Don't merge it; plz12345 merges.
