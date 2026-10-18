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
  - reads the slice from the scene cache only.
- **`HydrationQueue`:** a new background service. Anything not yet cached goes on this queue, so no client request waits on StashDB.
  - It deduplicates IDs and fills the cache through the existing `ISkyHook.GetScenes`/`GetPerformers`, under the existing rate limiters.
  - IDs that upstream reports as gone are remembered for an hour, so clients stop waiting on them.
- **Controllers:** two new GET actions serve the pages, and a POST action serves bulk performers.
  - When everything requested is cached: **200**, cacheable.
  - When something is still being fetched: **202 Accepted** with `Retry-After` and `no-store`. The body carries the cached items plus a `pending` ID list, and the client repeats the same request.
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
- **Partial responses (202):**
  - Status `202`, with header `Retry-After: 5` and header `Cache-Control: no-store`, so Cloudflare never caches a partial result.
  - Body: the same resource as the 200, with `pending` holding the IDs still being fetched.
  - A 200 always has `pending: []`.
- **Gone upstream:** an ID that upstream reports as not found is kept in a "gone" set for 1 hour. It is in neither the items nor `pending`, so clients can't poll forever on it.
- **Tests:** NUnit fixtures in `src/Backend.Test`, following `src/Backend.Test/API/SceneControllerFixture.cs` (Moq, a `DefaultHttpContext` on the controller).
  - Run with: `dotnet test src/Backend.Test/Backend.Test.csproj --filter "FullyQualifiedName~<Fixture>"`
  - Run the full project before each commit: `dotnet test src/Backend.Test/Backend.Test.csproj`

## Review Focus

1. **A cursor whose scene has since left the list** (deleted upstream, or moved to another studio). The next page carries on from the cursor's key position, without skipping or repeating scenes. Pinned in Task 3.
2. **A slice ID that upstream no longer has.** Once the queue marks it gone, the page drops it from both the items and `pending`, and returns 200 when nothing else is pending. A client can't poll forever. Pinned in Tasks 2 and 3.
3. **Many clients onboarding the same big studio at once** (16K scenes). The key list is fetched once per hour per site. Uncached scenes are queued once, not once per request. Pinned in Tasks 2 and 3.
4. **Garbage input:**
   - a non-GUID id → 404;
   - a malformed or tampered cursor → 400, not 500;
   - `limit` of 0, a negative number, or above 1000 → clamped to 1..1000.

   Pinned in Task 4.
5. **A partial page leaking into Cloudflare's cache.** Every 202 carries `no-store`, and only a page with nothing pending gets the 24h or 1h TTL. Pinned in Tasks 4 and 5.

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

### Task 2: Cache-only reads and the hydration queue

**Files:**
- Modify: `src/Backend/Cache/CachedEntityService.cs`. Add `Task<List<TCached>> GetCached(IEnumerable<string> entityIds)` to `ICachedEntityService` and implement it with `_repo.GetCache(ids)`. It never calls upstream, and an existing row counts as cached even when stale.
- Create: `src/Backend/SkyHook/HydrationQueue.cs`, with `HydrationKind`, `IHydrationQueue` and `HydrationQueue : BackgroundService, IHydrationQueue`.
- Modify: `src/API/Startup.cs`. Register `AddMemoryCache()` if it's missing, plus `services.AddSingleton<HydrationQueue>()`, `services.AddSingleton<IHydrationQueue>(sp => sp.GetRequiredService<HydrationQueue>())` and `services.AddHostedService(sp => sp.GetRequiredService<HydrationQueue>())`.
- Test: create `src/Backend.Test/SkyHook/HydrationQueueFixture.cs`, and add `GetCached` cases to the existing cache-service tests, or to a new `src/Backend.Test/Cache/CachedEntityServiceGetCachedFixture.cs`.

**Interfaces:**
- Consumes: the existing `ISkyHook.GetScenes(List<string>)` and `ISkyHook.GetPerformers(List<string>)`, which fill the cache from upstream.
- Produces:

```csharp
public enum HydrationKind { Scene, Performer }

public interface IHydrationQueue
{
    void Enqueue(HydrationKind kind, IEnumerable<string> ids); // ignores ids already queued/in flight or gone
    bool IsGone(HydrationKind kind, string id);                // upstream said not found within the last hour
}
```

- [ ] **Step 1: Write the failing tests.**

```csharp
[Test] public async Task GetCached_should_return_only_existing_rows_without_calling_upstream()
[Test] public async Task should_hydrate_queued_scenes_through_skyhook()        // Enqueue(Scene,[a,b]); worker → GetScenes([a,b]) once
[Test] public async Task should_not_queue_an_id_twice_while_in_flight()        // Enqueue a; Enqueue a → GetScenes called with a once
[Test] public async Task should_batch_up_to_100_ids_per_call()                 // Enqueue 250 performers → GetPerformers calls of ≤100
[Test] public async Task should_mark_ids_upstream_did_not_return_as_gone()     // GetScenes([a,b]) returns only a → IsGone(Scene,b) true, IsGone(Scene,a) false
[Test] public async Task should_not_queue_gone_ids()                           // after b is gone, Enqueue(Scene,[b]) → no further GetScenes call
[Test] public async Task should_keep_working_after_a_failed_batch()            // GetScenes throws once → logged, next Enqueue still hydrates; failed ids NOT marked gone
```

Drive the worker with `StartAsync` and a short wait on a `TaskCompletionSource` that the mock completes. Don't use `Thread.Sleep`.

- [ ] **Step 2: Run them to verify they fail.**
  Run: `dotnet test src/Backend.Test/Backend.Test.csproj --filter "FullyQualifiedName~HydrationQueue|FullyQualifiedName~GetCached"`
  Expected: build failure, because the types aren't defined yet.

- [ ] **Step 3: Implement.**
  - **Queue:** an unbounded `Channel<(HydrationKind, string)>` plus a `ConcurrentDictionary` holding the queued and in-flight IDs.
  - **Worker:** it drains up to 100 IDs of one kind per batch, calls the matching `ISkyHook` method, and removes each ID from the in-flight set afterwards.
  - **Gone set:** `IMemoryCache` entries `gone:{kind}:{id}`, with a 1-hour absolute expiry.
  - **Concurrency:** one batch at a time per kind. The upstream rate limiters already bound throughput.

- [ ] **Step 4: Run the tests to verify they pass.** Run the Step 2 command, then the full project.
  Expected: PASS.

- [ ] **Step 5: Commit.**

```bash
git commit -am "new: Background hydration queue and cache-only reads"
```

### Task 3: ScenePageService with keyset cursor

**Files:**
- Create: `src/Backend/SkyHook/SceneCursor.cs`
- Create: `src/Backend/SkyHook/ScenePageService.cs`
- Modify: `src/API/Startup.cs`, adding `services.AddSingleton<IScenePageService, ScenePageService>()`.
- Test: create `src/Backend.Test/SkyHook/SceneCursorFixture.cs` and `src/Backend.Test/SkyHook/ScenePageServiceFixture.cs`.

**Interfaces:**
- Consumes:
  - `SceneKey` and `IStashDbProxy.GetSiteSceneKeys`/`GetPerformerSceneKeys` (Task 1);
  - `ICachedSceneService.GetCached` and `IHydrationQueue` (Task 2).
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
    public List<Episode> Items { get; init; }          // cached scenes of the slice, in key order
    public List<string> Pending { get; init; }         // slice ids not cached and not gone; queued for hydration
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

// ScenePageServiceFixture: keys d1..d5 with dates 2020-01-01..2020-01-04 plus one null date, given to the proxy unsorted; all cached unless stated
[Test] public async Task should_order_by_release_date_then_id_with_undated_last()
[Test] public async Task should_return_next_cursor_until_the_last_page()     // limit 2 → pages of 2,2,1; last NextCursor null; Total 5
[Test] public async Task should_continue_after_a_cursor_whose_scene_left_the_list()
[Test] public async Task should_preserve_key_order_in_items()                // GetCached returns reversed; Items follow key order
[Test] public async Task should_report_and_queue_uncached_scenes_as_pending()
    // 1 of 2 slice ids uncached → Items has 1, Pending == [that id], Enqueue(Scene,[that id]) called; NextCursor still encodes the slice's last key
[Test] public async Task should_drop_gone_scenes_from_items_and_pending()    // IsGone true → neither in Items nor Pending, not enqueued
[Test] public async Task should_fetch_the_key_list_once_per_site_within_an_hour()  // two calls → GetSiteSceneKeys once (real MemoryCache)
[Test] public async Task should_page_performer_scenes_the_same_way()
```

- [ ] **Step 2: Run them to verify they fail.**
  Run: `dotnet test src/Backend.Test/Backend.Test.csproj --filter "FullyQualifiedName~SceneCursor|FullyQualifiedName~ScenePageService"`
  Expected: build failure, because the types aren't defined yet.

- [ ] **Step 3: Implement.**
  - **Sort key:** `(ReleaseDate ?? "9999-99-99", Id)`, compared ordinally.
  - **Slice:** the first `limit` keys strictly after the decoded cursor key, found with a binary search.
  - **`NextCursor`:** `Encode(lastKeyInSlice)` when keys remain, otherwise null.
  - **Key list cache:** `site:{guid}` / `performer:{guid}` in `IMemoryCache`, with a 1-hour expiry.
  - **Never** call `ISkyHook.GetScenes` from here. Only the queue fetches upstream.

- [ ] **Step 4: Run the tests to verify they pass.** Run the Step 2 command, then the full project.
  Expected: PASS.

- [ ] **Step 5: Commit.**

```bash
git commit -am "new: Keyset-paged scene pages for sites and performers"
```

### Task 4: `scenes/full` endpoints

**Files:**
- Create: `src/API/Resources/ScenePageResource.cs`, with `int Total`, `string NextCursor`, `List<Episode> Items`, `List<string> Pending`.
- Modify:
  - `src/API/Controllers/SiteController.cs`: inject `IScenePageService` and add the action after `GetSiteScenes`.
  - `src/API/Controllers/PerformerController.cs`: inject `IScenePageService` and add the action after `GetPerformer`.
  - Update the existing fixtures that construct these controllers.
- Test: create `src/Backend.Test/API/SceneFullControllerFixture.cs`.

**Interfaces:**
- Consumes: `IScenePageService`, `ScenePage` (Task 3).
- Produces:
  - `GET site/{guid}/scenes/full?cursor=&limit=` → 200 or 202 `ScenePageResource`;
  - `GET performer/{guid}/scenes/full?cursor=&limit=` → the same.

- [ ] **Step 1: Write the failing tests.**

```csharp
[TestCase("not-a-guid")] [TestCase("")]
public async Task should_404_for_a_bad_site_guid(string guid)
[Test] public async Task should_400_for_a_malformed_cursor()            // service throws FormatException → 400
[TestCase(null, 250)] [TestCase(0, 1)] [TestCase(-5, 1)] [TestCase(5000, 1000)] [TestCase(40, 40)]
public async Task should_clamp_limit(int? limit, int expected)
[Test] public async Task should_cache_a_complete_middle_page_for_24_hours()  // Pending empty, NextCursor set → 200, "public,s-maxage=86400,max-age=0"
[Test] public async Task should_cache_a_complete_last_page_for_1_hour()      // Pending empty, NextCursor null → 200, "public,s-maxage=3600,max-age=0"
[Test] public async Task should_return_202_no_store_while_scenes_are_pending()
    // Pending non-empty → 202, Cache-Control "no-store", Retry-After "5", body Pending == service Pending
[Test] public async Task should_normalise_the_guid_to_lowercase()
[Test] public async Task should_serve_performer_scenes_full_the_same_way()   // 404 / 400 / 200 TTL / 202 on PerformerController
```

- [ ] **Step 2: Run them to verify they fail.**
  Run: `dotnet test src/Backend.Test/Backend.Test.csproj --filter "FullyQualifiedName~SceneFullControllerFixture"`
  Expected: build failure, because the actions don't exist yet.

- [ ] **Step 3: Implement.**
  - Use priority 7, the same as `GetSiteScenes`.
  - Write the 200 TTL headers from a fixed `TimeSpan`, so the seconds are exact.
  - Map an upstream 404 on the key-list fetch to 404, as the neighbouring actions do.

- [ ] **Step 4: Run the tests to verify they pass.** Run the Step 2 command, then the full project.
  Expected: PASS.

- [ ] **Step 5: Commit.**

```bash
git commit -am "new: site and performer scenes/full paged endpoints"
```

### Task 5: `POST performer/bulk`

**Files:**
- Create: `src/API/Resources/PerformerBulkResource.cs`, with `List<Performer> Performers` and `List<string> Pending`.
- Modify: `src/API/Controllers/PerformerController.cs`. Add the action next to `RefreshPerformerCache`. It's a public read with no API key.
- Test: create `src/Backend.Test/API/PerformerBulkControllerFixture.cs`.

**Interfaces:**
- Consumes: `ICachedPerformerService.GetCached` and `IHydrationQueue` (Task 2).
- Produces: `POST performer/bulk`, with a body of `["guid", ...]` → 200 or 202 `PerformerBulkResource`.

- [ ] **Step 1: Write the failing tests.**

```csharp
[Test] public async Task should_return_200_when_all_performers_are_cached()      // Pending empty, no Enqueue
[Test] public async Task should_return_202_and_queue_uncached_performers()
    // 1 of 3 uncached → 202, Retry-After "5", Cache-Control "no-store", Performers has 2, Pending == [id], Enqueue(Performer,[id])
[Test] public async Task should_leave_gone_performers_out_of_pending()           // IsGone → not pending, not enqueued; all others cached → 200
[Test] public async Task should_drop_duplicates_and_non_guids_before_lookup()    // ["A","a","x"] → GetCached(["a"])
[Test] public async Task should_400_above_100_ids_after_cleaning()
[Test] public async Task should_return_empty_200_for_an_empty_or_null_body()
```

- [ ] **Step 2: Run them to verify they fail.**
  Run: `dotnet test src/Backend.Test/Backend.Test.csproj --filter "FullyQualifiedName~PerformerBulkControllerFixture"`
  Expected: build failure, because the action doesn't exist yet.

- [ ] **Step 3: Implement.** Lowercase each ID, keep those that pass `Guid.TryParse`, and remove duplicates. Then read with `GetCached`, and enqueue whatever is neither cached nor gone. Use priority 7.

- [ ] **Step 4: Run the tests to verify they pass.** Run the Step 2 command, then the full project.
  Expected: PASS.

- [ ] **Step 5: Commit.**

```bash
git commit -am "new: POST performer/bulk with 202 while hydrating"
```

### Task 6: `/v5` purge site and deployment runbook

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

### Task 7: Batched StashDB hydration (aliased `findScene` / `findPerformer`)

Added after the live container test. It makes each hydration batch **one** StashDB request instead of one request per id.

Verified against StashDB on 2026-10-18:
- 100 aliased `findScene` with Skyhook's full field list return 100/100 in 1.6 s (200 also works).
- 100 aliased `findPerformer` return 100/100 in 1.4 s.
- A missing id comes back as a `null` alias, with no error.

The queue, cache-only reads, 202/pending and gone rules don't change. Only the upstream fetch inside `HydrateScenes`/`HydratePerformers` changes.

**Files:**
- Modify:
  - `src/Backend/MetadataSource/StashDb/Requests/GetSceneRequest.cs` and `GetPerformerRequest.cs`: move the field selection into a `public const string Selection`, so the single and batch queries share it verbatim.
  - `src/Backend/MetadataSource/StashDb/StashDbProxy.cs`: add the two batch methods, mapping and validating each scene exactly as `GetScene` does (same `MapScene`, same deleted/invalid-date rule) and each performer as `GetPerformer` does.
  - `src/Backend/SkyHook/SkyHook.cs`: `HydrateScenes`/`HydratePerformers` fetch misses through the batch methods. Before attaching credits, `HydrateScenes` warms the performer cache for every credited performer in the batch with one `HydratePerformers` call. That way `AttachCachedStudioAndPerformers` reads cached performers instead of making one upstream call per credit.
- Create:
  - `src/Backend/MetadataSource/StashDb/Requests/GetScenesBatchRequest.cs`
  - `src/Backend/MetadataSource/StashDb/Requests/GetPerformersBatchRequest.cs`
  - `src/Backend/MetadataSource/StashDb/Resources/BatchResponse.cs`: `data` is a dictionary of alias → resource.
- Test: create `src/Backend.Test/MetadataSource/StashDbBatchFixture.cs`, and extend `src/Backend.Test/SkyHook/SkyHookHydrateFixture.cs`.

**Interfaces:**
- Consumes: `HydrationOutcome`, `HydrateScenes`/`HydratePerformers` (Task 2 and the final fix wave), and the existing `MapScene`/`MapPerformer`.
- Produces:

```csharp
public const int MaxBatch = 100; // StashDbProxy
// Per requested id: Found(entity) | NotFound (null alias, deleted, or invalid date). A whole-request failure throws (transient for every id in it).
Task<IReadOnlyDictionary<string, Episode>> GetScenesBatch(IReadOnlyList<string> ids, ICollection<string> notFound);
Task<IReadOnlyDictionary<string, Performer>> GetPerformersBatch(IReadOnlyList<string> ids, ICollection<string> notFound);
```

- [ ] **Step 1: Write the failing tests.** Mock `IStashDbRequestService`.

```csharp
[Test] public async Task should_fetch_250_scene_ids_in_three_requests()          // ExecuteRequest called 3 times (100, 100, 50 aliases)
[Test] public async Task should_alias_ids_and_reuse_the_single_scene_selection() // request body contains "s0: findScene(id:", GetSceneRequest.Selection
[Test] public async Task should_report_null_deleted_and_undated_aliases_as_not_found()
[Test] public async Task should_map_a_batched_scene_identically_to_GetScene()    // same StashSceneResource → equal serialized Episode
[Test] public async Task should_throw_for_a_failed_batch_request()               // request service throws → method throws, nothing reported not-found
[Test] public async Task should_fetch_performers_100_per_request()
// SkyHookHydrateFixture
[Test] public async Task HydrateScenes_should_make_one_upstream_call_per_100_misses()
[Test] public async Task HydrateScenes_should_warm_credited_performers_in_one_batch_before_attaching()
    // 100 scenes with 150 distinct uncached performers → GetPerformersBatch called with ≤100 ids per call, 2 calls; zero single GetPerformer calls
[Test] public async Task HydrateScenes_should_only_fetch_cache_misses()          // 30 of 100 cached → batch request carries 70 ids
```

- [ ] **Step 2: Run them to verify they fail.**
  Run: `dotnet test src/Backend.Test/Backend.Test.csproj --filter "FullyQualifiedName~StashDbBatchFixture|FullyQualifiedName~SkyHookHydrateFixture"`
  Expected: build failure, because the batch methods don't exist yet.

- [ ] **Step 3: Implement as described under Files.**
  - **Alias names:** `s{index}` / `p{index}`. Map each alias back to its id by index.
  - **GraphQL `errors`:** keep the existing rule. A response with any `errors` throws, so the whole batch is transient.
  - **Rate limiting:** one rate-limiter slot per batch request, at the hydration priority (9).
  - **Studios** on attached scenes keep using `GetSite` through the cache. There are only a handful per batch.

- [ ] **Step 4: Run the tests to verify they pass.** Run the Step 2 command, then the full project.
  Expected: PASS.

- [ ] **Step 5: Measure it in the local stack** (`skyhook-v5` + persistent `skyhook-v5-pg`).
  1. Delete Brazzers Exxtra's cached scenes and their performers, so the cache is as cold as in the baseline run.
  2. Rebuild and restart the Skyhook container.
  3. Re-run `hydrate_walk.py` for Brazzers Exxtra.
  4. Compare the elapsed time and the number of StashDB requests against the baseline.
  5. Run `hydrate_walk.py` for Nubiles.net (`7e4dc83a-5fe6-490b-a421-3e289f5e5d7d`, 13,916 scenes, never fully hydrated). Expected: about 4–6 minutes with one batch at a time per kind. Record the elapsed time, the scene and performer batch counts, and the unique items against `Total`.
  6. Report both measurements. Running 2–3 scene batches at once is a later decision, made after these numbers.

- [ ] **Step 6: Commit.**

```bash
git commit -am "new: Hydrate scenes and performers 100 per StashDB request"
```

---

## Finish

- [ ] Run the full `src/Backend.Test` project and build the solution. Both must pass with no new warnings.
- [ ] **Local container test**, using two throwaway containers on a dedicated Docker network.
  - **Postgres:** `docker network create skyhook-v5`, then `docker run -d --name skyhook-v5-pg --network skyhook-v5 -e POSTGRES_USER=abc -e POSTGRES_PASSWORD=abc -e POSTGRES_DB=whisparr postgres:16`.
  - **Skyhook:** build it with `docker build -t whisparr-metadata:v5-bulk .`, then run it with `--network skyhook-v5 -p 8080:8080`. Pass the DB connection (host `skyhook-v5-pg`) and the StashDB credentials from `appsettings.Development.json` as environment variables.
  - Then check:
    1. **Paging:** page through a large studio with `limit=250`. Uncached pages first answer `202` (`no-store`, `Retry-After: 5`, a non-empty `pending`). Repeating the same URL turns them into `200` with `pending: []`.
       - Across all the final 200 pages, `Total` equals the number of items, every ID is unique, and the last page has `nextCursor: null`.
    2. **Cache headers:** a complete middle page carries `s-maxage=86400` and the complete last page `s-maxage=3600`.
    3. **Performer bulk:** `POST performer/bulk` with performer IDs from those scenes answers `202`, then `200` once hydrated.
    4. **Logs:** the container log shows each scene ID fetched upstream once, even when the same page is requested repeatedly.
  - **Clean up:** `docker rm -f skyhook-v5 skyhook-v5-pg && docker network rm skyhook-v5`.
  - If no StashDB credentials are available, run what works without them (a 404 for a bad GUID, a 400 for a bad cursor), and say what was skipped.
- [ ] Push `v5-bulk` and open a PR into `v4` using the repo's conventions. The PR body lists the commits, the test results and the container check, and links the runbook. Don't merge it; plz12345 merges.
