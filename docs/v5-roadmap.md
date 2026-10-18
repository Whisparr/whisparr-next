# Whisparr v5 alpha: unified v2 + Eros line (`Whisparr/whisparr-next`)

## Context

plz12345 and gykes (the v2 maintainer) agreed to merge maintenance into one repo and one
version line, **v5**. A v5 Skyhook will eventually serve every app line. This plan covers
the short-term work that makes moving to v5 painless for both v2 and Eros users, up to an
**alpha MVP**.

- Work is built locally and pushed to a private repo, `Whisparr/whisparr-next`. The name
  may change before launch.
- GitHub Actions and container tooling wait until the repo goes public.

The base is Eros (Radarr lineage, `~/Git/Whisparr`). v2 (Sonarr lineage,
`~/Git/Whisparrv2`) is a migration *source* only.

**Success:** a v2 or Eros user swaps the Docker tag or installs the build. Startup stops at
a migration screen. The user opts in and confirms their metadata sources. Their library
comes back the way they had it:
- same monitored state;
- files in the same names and folders;
- nothing on disk moved;
- the legacy config and DB backed up and untouched.

## Goals / non-goals / anti-goals

- **Goals:** items 1–5, 7 and 8 from the request. Item 6 (own container builds) is only
  noted.
- **Non-goals for alpha:**
  - folding the Skyhook v3/v4 loops into one v5 service;
  - migrating Skyhook cache data;
  - CI, container images and the public repo;
  - porting v2 import lists.
- **Anti-goals.** The alpha fails if any of these happens:
  - the migration renames or moves user files;
  - the legacy DB or `config.xml` is changed or deleted;
  - a wrong identification is applied silently;
  - mass searches start after migration;
  - integrations break because `/api/v3` went away.

## Decisions (plz12345's)

1. **Migration covers v2 and Eros DBs.** On a legacy DB, startup halts with two choices:
   migrate (one-way) or shut down.
2. **Backups first.** Migration backs up the legacy config and DB, then builds a **new**
   v5 DB. It ports:
   - naming formats;
   - root folders;
   - other settings;
   - the library.

   Then it asks for a permanent scene source (StashDB/TPDB) and a permanent movie source
   (TMDB/TPDB), and starts the import with **renaming off** (the user is told so).
3. **Source picker:**
   - It shows the user's **previous source**: v2 used TPDB for scenes; Eros used StashDB
     for scenes and their configured movie source.
   - It says that keeping the previous source gives a faster, more faithful migration.
   - **Defaults:**
     - v2 → TPDB for scenes *and* movies. Explain why: the library is seeded straight
       from the v2 data, and metadata stays single-source.
     - Eros → StashDB for scenes, and their configured movie source.
   - Picking TPDB for scenes strongly encourages TPDB for movies too.
   - Any choice is allowed. Moving off the previous source warns that the import will
     take a lot longer.
4. **The legacy library seeds the migration; disk is the fallback.**
   - **v2 with scenes on TPDB:** v2's `Series`/`Episodes`/`EpisodeFiles` (numeric TPDB IDs)
     seed studios, scenes, files and monitored state.
   - **Eros with the same source:** the library carries over as is.
   - **Rebuild from disk** happens only when the user moves off their previous source.
5. **Catalog scope:**
   - **v2:** each studio brings in its whole scene catalog, as v2 already did.
   - **Eros:** only the data the user already had.
   - **Monitored state is copied from the legacy DB.**
   - No searches run during migration.
6. **TPDB scenes, studios and performers are in the alpha**, end to end (Skyhook + app).
7. **New Skyhook features go in as v5 routes on `Whisparr-Eros-Metadata`**, sharing its
   DB. Merging the v3/v4 loops and migrating data come after alpha.
8. **The API serves both `/api/v5` and `/api/v3`**, the way Sonarr does:
   - a frozen `Api.V3` project;
   - a parallel `Api.V5` project;
   - the frontend calls v5.
9. **v5 has its own `config5.xml`**, seeded from `config.xml`. The legacy file is never
   written.
10. **Hashes are a fallback** after the parser. phash runs at most once per file.
11. **Flat studio folders are derived from the scene folder format**, not a separate
    setting. A v2 format carries over.
12. **Mixed TMDB/TPDB movies in Eros are accidental.** They come from changing source
    with movies already present, which caused duplicates. That's why the source is now
    locked once.

## Recon facts the plan rests on

- **Eros scenes are StashDB-only.**
  - The ForeignId is the StashDB GUID.
  - Movies use `WhisparrMovieMetadataSource` (TMDB/TPDB), which can be changed at runtime
    (`ConfigService.cs:610`).
- **v2 is sites > scenes, TPDB-only.**
  - Each site pulls in its whole scene list (episodes, in Sonarr terms).
  - It stores **numeric** TPDB IDs: `Series.TvdbId`, `Episodes.TvdbId`, plus
    `Episodes.Monitored`.
  - Eros stores TPDB **UUIDs**.
- **Eros studio add today** (`RefreshStudioService.cs:285`, `AddMovieService.cs:95/108`):
  - `site/{id}/scenes` returns IDs only.
  - Each new scene is fetched twice.
  - Performers are fetched *inline* in each `RefreshMovieCommand`.
  - The queue orders by priority, then FIFO, on 3 threads (`CommandQueue.TryGet`,
    `CommandExecutor.cs:123`).
- **Covers already download outside the command threads.** `MediaCoverService` keeps its
  own bounded queue, fed by `MovieUpdatedEvent` (`MediaCover/MediaCoverService.cs:253`).
- **Skyhook v4:**
  - The only bulk scene call is the API-key `POST scene/bulk/refresh`.
  - It stores no fingerprints.
  - `/v4` is a proxy prefix; controllers use `[Route("")]`.
  - TPDB REST is used for movies only.
  - The v2 Skyhook (`WhisparrAPI.Metadata`) has TPDB scene/site client and mapping code
    that can be reused.
- **Startup:**
  - Migrations run at `Startup.cs:245`.
  - Kestrel listens after `Configure` returns.
  - Command threads and the scheduler start on `ApplicationStartedEvent`
    (`AppLifetime.cs:57`).
  - `AddDummyDatabase()` and `StartingUpMiddleware` already exist.
- **AppData:**
  - Shared `Whisparr` folder and `config.xml`; DBs are `whisparr2.db` / `whisparr3.db`.
  - **Both apps support Postgres**, and both default to `whisparr-main` / `whisparr-log`.
  - Detection rule: `Series` present and `Movies` absent → v2; `Movies` present → Eros.
- **Fingerprints:**
  - Part A is built (`~/Git/Fingerprints`: stash-compatible oshash/phash, static ffmpeg).
  - Part B (integration in the app) isn't done.
  - No app hashes files today.
- **v2 default layout** is `<root>/<Site Title>/<release name>`: flat, renaming off.

## Step 0: Eros issues (done)

Filed as Whisparr-Eros#1000–#1002 and fixed in #1004–#1006 (plus the Pkcs pin, #1003)
before this repo was seeded, so the fixes are in the seed:
1. `SkyHookProxy.MapForeignId` emits `tpdbId:`, but `AddMovieService.cs:237` and
   `RefreshMovieService.cs:305` match `tpdbid:` case-sensitively.
2. `ImportDecisionMaker.cs:270-276` has two null dereferences. The branch turned out to
   be unreachable and was removed.
3. `BackupService.Restore` doesn't recognise `whisparr3.db`, so an Eros backup restores
   without its DB. Line 97 also deletes `whisparr.db-journal`.

## Workstreams and order

```
A Foundation ──┬─> B Skyhook v5 ──┬─> C Bulk onboarding ───┐
               │                  ├─> E Source lock + TPDB ┤
               └─> D Fingerprints ┴─> F Flat folders ──────┴─> G Migration wizard ─> alpha
```

B and D can run in parallel. G comes last because it uses all the others.

### A. Foundation (item 7, app side)

- **Seed `whisparr-next`** from Eros `eros-develop` with full history. (my call)
- **Version 5:** `WHISPARR_MAJOR_VERSION=5` (`build.sh`, `src/Directory.Build.props:87`),
  pre-release label `alpha`.
- **Own storage:**
  - `whisparr5.db` / `whisparr5.restore` (`PathExtensions.cs:15`);
  - Postgres defaults `whisparr5-main` / `whisparr5-log`;
  - `config5.xml` (`ConfigFileProvider`);
  - update the `AppFolderFactory.cs:116-148` legacy rename and the `BackupService`
    names.
- **Never migrate a legacy DB:**
  - The first v5-only migration writes a marker.
  - Startup refuses to migrate a DB that has a `VersionInfo` but no marker, and goes to
    setup mode instead.
  - This catches `Whisparr__Postgres__*` env vars that still name a legacy DB. The setup
    screen then says which env vars to change.
- **Migration numbering:** v5-only migrations start at **300**. Eros migrations
  (247–299) are cherry-picked in under their own numbers. The wizard refuses an Eros DB
  containing a migration v5 doesn't know, and says to update v5 first. (my call)
- **API, following Sonarr's layout:**
  - Add `V5ApiControllerAttribute`, modelled on
    `~/Git/Sonarr/src/Sonarr.Http/VersionedApiControllerAttribute.cs:37`.
  - Create a new `Whisparr.Api.V5` project, seeded from V3. V3 is frozen and gets bug
    fixes only.
  - The frontend `apiRoot` points to v5 (`InitializeJsonController.cs:48`, `getQueryPath`).
  - Also update `ApiInfoController.cs:14`, Swagger for both versions
    (`Startup.cs:101/304/325`), `OpenApi/OperationIds.cs:10`, and `Bootstrap.cs:40`.
  - This also gives Sonarr's `Api.V5` commits somewhere to land.
- **SkyHook base URL:**
  - Becomes `https://api.whisparr.com/v5/{route}`: `SkyHookProxy.cs:63`,
    `WhisparrCloudRequestBuilder.cs:25`.
  - The `ConfigFileProvider.cs:188-202` guard rejects saved v3/v4 values.

### B. Skyhook v5 (`~/Git/Whisparr-Eros-Metadata`, branch v4)

plz12345 commits directly on this repo, so leave the work uncommitted until a prod push
is called.

**Routing:**
- A proxy rule strips `/v5` the same way `/v4` is stripped, so every existing route is
  also served under `/v5`.
- New routes have no `v5/` prefix in code.
- Add a second `Cloudflare.Sites` entry for purges. (my call)

**Cursor-paged bulk endpoints** (this is where onboarding speed comes from):
- **Studio scenes:** `GET site/{id}/scenes/full?cursor=&limit=`
  - Returns full scene objects from `scenecache`.
  - Stable ordering: release date, then ID.
  - Default 250, max 1000.
- **Performer scenes:** `GET performer/{id}/scenes/full?cursor=&limit=`, with the same
  shape. For TPDB-fed data, include linked movies, *assuming TPDB links them upstream;
  to be confirmed*.
- **Why cursors?** A huge studio becomes many small pages. Example: nubiles.net has about
  16K scenes. Paging means:
  - no request ties up a Skyhook thread for long;
  - each page is a stable URL that Cloudflare can cache;
  - the last page gets a short TTL, earlier pages a long one.
- **Cache gaps:** a page with uncached scenes fills them through the existing StashDB/TPDB
  paging before it's returned.
- **Performers:** `POST performer/bulk` (by IDs) for deferred performer hydration.

**TPDB scene path:**
- Cache tables keyed by TPDB UUID: `tpdbscenecache`, `tpdbsitecache`,
  `tpdbperformercache`.
- Routes: `tpdb/scene|site|performer/{uuid}`, search, and `tpdb/site|performer/{uuid}/
  scenes/full`.
- A TPDB `/changes` loop next to the movie loop in `CacheRefreshService`.
- **Reuse the v2 Skyhook's TPDB scene/site client and mapping code**
  (`~/Git/WhisparrAPI.Metadata` `TPDbProxy.cs`).
- **Accept numeric TPDB IDs and resolve them to UUIDs.** The v2 seed relies on this.

**Fingerprints in the cache:**
- Fetch scene hashes during normal hydration and the refresh loops.
- Store them in a `scenefingerprint` table (hash, algorithm, source, scene ID), indexed
  by hash.
- **To confirm first:** I expect both sources return hashes inline, but this needs
  checking. StashDB's `Scene.fingerprints { hash algorithm duration }` should come back
  in the same GraphQL query as the scene, and TPDB scene objects should carry a `hashes`
  array. Fall back to a separate call only if they don't.
- Then `POST fingerprint/lookup {oshash?, phash?, source}` answers from the local table
  first, and asks upstream only on a miss.

### C. Bulk onboarding: studios and performers (items 1, 2): built

Adding or refreshing a studio or performer now pulls its scenes from Skyhook's paged
`scenes/full` endpoint instead of one HTTP call per scene.

- **Prefetch cache.**
  - Each `scenes/full` page is held in a short-lived in-memory cache in the Skyhook proxy.
  - The distinct performer IDs on a page are fetched with `performer/bulk` (batches of 100) and cached the same way.
  - The existing per-scene refresh code then reads scenes and performers from the cache without touching the network.
  - Only IDs that `performer/bulk` reports as pending, or whose batch failed, fall back to a single `performer/{id}` call.
- **Inline refresh, no queued commands.**
  - The importer refreshes each new scene directly in the add or refresh command's thread.
  - No `RefreshMovieCommand` is queued and no `MovieUpdatedEvent` is published for the bulk-added scenes.
  - The `RefreshStudios` command is the single unit of work and finishes when the last scene row exists.
- **Covers are hotlinked until a later refresh.** Scenes carry their remote image URL, so the UI loads covers straight from the source.
  Nothing is written under `MediaCover/` for these scenes until a later refresh caches them.
- **Shared importer.** Studio and performer adds go through one scene importer, so both get the same paging, caching and refresh behaviour.
- **Walker rules (202 and 503).**
  - A **202** page carries the items already cached and a `pending` ID list. The importer upserts what it got, moves on, and revisits the page after `Retry-After`.
  - A **503** is treated as a throttle: back off for `Retry-After` and retry the same page.
  - A 503 that persists for 5 minutes aborts the walk at that page. A 202 page whose pending list stops shrinking for 5 minutes is abandoned and its pending IDs are logged. Both are retried on the next sync.
- **Bounded memory.** The proxy cache exists only so the inline refresh can read a page without the network.
  The importer evicts a page's scenes and performers as soon as the page is done, so memory stays at about one page. The two-hour lifetime is only a safety net.
- **Scheduled syncs walk full catalogs (accepted trade-off).** A daily scheduled sync walks every monitored studio's and performer's pages, not only what changed.
  For a 4K-scene studio that is about 58 MB per day and holds one of the two command threads for minutes. This was kept on purpose in exchange for a single code path shared with adds; eviction keeps the memory cost bounded.
- **Owner deleted mid-import.** Before each page the importer checks that the studio or performer still exists and is monitored, and ends the import with an Info log if not, so a delete during an import is not undone.
- **Scenes before performers.** Credits already hold `PersonName` and `Images` (migration 016), so scenes display before performer rows exist.

**Measured** (fresh local instance, trace logging, live `api.whisparr.com/v5`, Brazzers Exxtra, 3,987 scenes, monitored, search on add off):

| | Result |
|---|---|
| Add to last scene row (`Synced` log line) | 120 s |
| `RefreshStudios` command completed | 120 s |
| `site/{id}/scenes/full` | 17 (1 first page + 16 cursor pages) |
| `performer/bulk` (POST) | 25 (about 1,700 performers; 25 x 100 is only the upper bound, and the run created 1,724 performer rows) |
| `scene/{id}` | 0 |
| `performer/{id}` | 0 |
| Other Skyhook calls | 2 `site/{id}`, 1 `site/search` (the lookup before the add) |
| `RefreshMovie` commands queued | 0 |
| Skyhook responses | 45, all 200 |
| Files under `MediaCover/` for the scenes | 0 |

Before C the same add costs about 2 calls per scene plus one per performer. That is an estimate,
not a measurement: 2 x 3,987 scene calls plus about 1,700 performer calls, roughly 9,700 calls
against 45 now. Scenes appear on the studio page as pages arrive, covers load by hotlink, and a
scene's cast shows on its details page.

### D. Fingerprints in the app (item 8, Part B of `for-whisparr-one-of-tidy-sparkle.md`)

- **Package:** `PackageReference Whisparr.VideoHashes`, from a local NuGet feed until
  it's published.
- **Columns:** a migration adds `MovieFiles.OsHash` (indexed), `MovieFiles.PHash`, and
  `MovieFiles.PHashAttemptedAt`. `ImportFiles` gets the same columns.
- **`VideoHashService` swallows its own failures.** Hashes are computed while the import
  builds the `MovieFile` row. If hashing fails (ffmpeg missing, unreadable file, timeout),
  the service logs it and returns null hashes. The `MovieFile` row and the import still
  complete. A hashing problem must never reject or abort an import.
- **When hashes run:**
  - oshash: inline on import, plus a backfill.
  - phash: `GenerateVideoHashesCommand` at Low priority, behind a setting.
- **phash runs once per file.**
  - It runs only when `PHash` and `PHashAttemptedAt` are both null.
  - A failure still records `PHashAttemptedAt`, so it isn't retried.
  - Only a size change on rescan (`DiskScanService.cs:173-183`) clears the hashes and
    allows a recompute.
- **Health check:** warn when ffmpeg is missing.
- **Identification order:**
  1. Parser (`MovieService.FindScene`).
  2. oshash lookup. It reads 128 KiB per file and the match is exact.
  3. phash, once per file, in the background, only for files that are still unmatched.

  Hook it where `FindScene` returns null (`ParsingService.GetSceneMovie`,
  `ImportDecisionMaker`) and into library-import lookup.
- **Matching rules:**
  - An oshash match links automatically.
  - A phash match links automatically only at distance 0.
  - Anything weaker goes to `ImportFiles` for review. (my call)
- **Files renamed outside Whisparr** (common with StashApp):
  - oshash depends only on content, so it survives a rename or move.
  - On rescan, a file that has disappeared from a scene's path, plus a new unknown file
    whose oshash matches an existing `MovieFiles.OsHash`, is treated as a move: the row
    is re-pointed instead of the scene being marked missing.
  - Part of F's research spike; measure how often this happens.

### E. Metadata-source lock and TPDB scenes in the app (item 5)

- **Config:** `WhisparrSceneMetadataSource` (StashDb/Tpdb) plus the existing
  `WhisparrMovieMetadataSource`.
  - They're set only by the setup screen. Fresh installs see it too, without the
    migration step.
  - Once set, the config API rejects changes and the UI shows them read-only.
- **Help tip on the locked fields:** to switch, stop Whisparr, rename or move
  `whisparr5.db` (or point to a new Postgres DB), and start again. That runs first-time
  setup, which can migrate the old v5 DB with a new source.
- **TPDB scenes:**
  - `ForeignId` holds the locked source's native ID: the **TPDB UUID**, just as StashDB
    uses its UUID.
  - Add `MetadataSource.TpdbScene`, plus the TPDB studio and performer equivalents.
  - `SkyHookProxy` routes by the locked source.
  - Go through every StashId-specific path: `FindScene`, naming tokens (add a
    source-neutral `{Scene ForeignId}`), and the Stash notification.
- **Import lists:** rename the StashDB lists to source-neutral names. TPDB has a GraphQL
  layer that could back the same lists with a TPDB API key; that needs testing first. For
  alpha, the lists stay StashDB-only and are hidden when scenes are TPDB.

### F. Flat studio folders (item 4)

- **Derived from the scene folder format:** if the format ends at the studio level, with
  no per-scene segment, scenes are flat. There's no extra setting. A v2
  `SeriesFolderFormat` translates directly into this.
- **What changes** (follow-up 3 of the earlier plan):
  - path validators allow several scenes per folder;
  - rescan only considers files a scene owns (by `MovieFiles` path, or by hash);
  - moves are per file;
  - metadata/extra names are per file.
- **Research spike first:** what happens when another tool renames files (StashApp)?
  - Does rescan see the change as a delete plus a new unmapped file?
  - Does oshash re-linking (D) cover it?
  - Are sidecar files orphaned?
- Depends on D. Bring the earlier chat's detail over as its own spec when work starts.

### G. Migration wizard (item 3)

**Detection, before migration runs** (in `Startup.Configure`, before line 245). Enter
setup mode when there's no marked v5 DB and any of these is found:
- `whisparr2.db` or `whisparr3.db` in AppData;
- a configured Postgres DB that has the `Series`/`Movies` tables.

If both legacy DBs exist, the screen asks which one to migrate.

**Setup mode:**
- Dummy DB; no lifecycle events, so no command threads and no scheduler.
- The normal frontend bundle with a `/setup` route, backed by a `SetupController`
  outside the versioned API.
- **Auth** is checked against the legacy `config.xml` API key, or against the legacy
  `Users` table opened read-only. (my call)

**Decline:** exit cleanly and log how to roll back to the old tag.

**Migrate:**
1. **Backup.** This is a hard gate: if the backup fails, nothing else runs.
   - Write `AppData/Backups/pre-v5/<timestamp>.zip`, and show its path.
   - SQLite: the legacy DB (via `MakeDatabaseBackup.BackupDatabase` on a raw connection)
     plus `config.xml`.
   - Postgres: an in-process dump of every table (Npgsql `COPY … TO STDOUT` plus the DDL)
     plus `config.xml`. No `pg_dump` binary is needed.
   - The legacy files are never opened for writing.
   - Then seed `config5.xml` from `config.xml`.
2. **Source picker**, as in decision 3.
3. **Build the v5 DB:**
   - **Eros, same scene source:**
     - Copy the DB to `whisparr5.db` (Postgres: `CREATE DATABASE … TEMPLATE` when the role
       allows, otherwise guide the user) and run migrations. Everything carries over.
     - **Mixed movies** (accidental, decision 12): keep the rows that match the chosen
       movie source. For the rest:
       - if a row duplicates one that's kept, merge the duplicate's files into the kept
         row and drop it;
       - otherwise detach it and re-identify it.
   - **Eros, new scene source:** same copy, then detach the scene, studio and performer
     rows and rebuild them.
   - **Detach means:** a `MovieFiles` row is moved into `ImportFiles` with its path and
     hashes, never deleted. Re-linking is then just a hash or path lookup. Nothing on disk
     is ever touched.
   - **v2:** run a fresh migration, then `V2SettingsPorter` reads the legacy DB through a
     read-only Dapper connection.
4. **v2 settings port:**
   - **Naming:**
     - `{Site …}` → `{Studio …}`, `{Episode …}` → `{Scene …}`. Tokens with no
       equivalent are dropped and reported.
     - `SeriesFolderFormat` → a flat `SceneFolderFormat` (F).
     - Colon value 5 → 4.
   - **Copied as is:** root folders, users, tags, delay profiles, remote path mappings.
   - **Config keys:** copied, with the renames found in recon.
   - **Download clients:** `tv*` fields → `movie*`. RQBit and Tribler are skipped.
   - **Indexers:** v2-only fields dropped. Fanzub is skipped.
   - **Notifications:** event columns renamed, plus the email encryption conversion.
   - **Quality:**
     - the 5 differing IDs are remapped;
     - sizes move back onto `QualityDefinitions`;
     - ranks are rebuilt.
   - **Custom-format enums:** remapped.
   - **Import lists:** not ported.
   - **Supported versions:** v2 schema 221. An older DB is refused with "update v2
     first". (my call)
5. **Renaming off.** `RenameScenes` and `RenameMovies` are forced to false, and the user
   is told.
6. **Library import.** It runs after setup, once normal startup resumes, as a long-running
   command with progress in the UI. Search-on-add stays suppressed throughout.
   - **v2 with scenes on TPDB** (the seeded path):
     - Each `Series` row becomes a studio: numeric `TvdbId` → TPDB UUID via Skyhook. The
       studio's full catalog comes in through C.
     - `Episodes.Monitored` is copied onto each scene.
     - `EpisodeFiles` → `Episodes.TvdbId` links each file to its scene exactly, at the
       existing path.
     - Only files with no usable legacy link fall back to the D order.
   - **v2 with scenes on StashDB:**
     - Map each studio via Skyhook's TPDB↔Stash studio mapping, or by title search.
     - Identify each file through the D order: parser, then oshash, then phash.
     - Monitored state comes from the matched v2 episode.
   - **Eros rebuild:**
     - Studios and performers come from the legacy rows, through Skyhook mappings or
       search.
     - Only scenes and movies the user had are re-identified, by detached hash, path and
       parser.
     - Monitored state is copied.
   - **Unmatched files** go to `ImportFiles` for manual review.
7. **Migration report.**
   - Skipped settings, dropped tokens, unmatched files and items needing attention are
     saved in the DB.
   - The report is shown at the end and stays at System → Migration Report, with a
     download button.
   - A health-check notice links to it until the user dismisses it.

### H. Container builds (item 6): deferred

These wait until the repo goes public. For when they happen: the image must use `/config`
with hotio's AppData layout, so that swapping the tag finds the legacy DB.

### After alpha

- Fold the Skyhook v3/v4 loops into v5 and migrate both caches (data migration, not a
  cold hydrate).
- Own CI and images, and the public repo.
- When Actions come back on, the upstream sync (`upstream-watch.yml`, `.github/upstream/state.json`)
  tracks **Sonarr only**. Drop the Radarr matrix entry and its state.
- Optionally squash the v5 migration chain into a new baseline.

## Verification

- **Per workstream:**
  - NUnit `*Fixture` tests (80% coverage on new code);
  - run every test project (Core, Common, Api, Host, Mono);
  - `yarn typecheck`, lint and build for frontend changes.
- **Wizard fixtures:**
  - detection for each DB shape (v2 SQLite, Eros SQLite, both present, v2 Postgres,
    a legacy DB named by an env var);
  - porter tests driven by a real v2 DB copy;
  - Eros copy-and-migrate;
  - detach/merge for mixed movies;
  - phash once per file.
- **Skyhook:**
  - cursor pagination is stable while data is being inserted;
  - Cloudflare `Cache-Control` per page;
  - fingerprints are stored during hydration;
  - numeric TPDB IDs resolve to UUIDs.
- **End to end on the 6969 dev instance** (dev data is expendable):
  - **v2 path:** `/config` holding a copy of a real `whisparr2.db` plus `config.xml`, and
    a flat studio tree.
    - Expect the halt screen, the backup zip, TPDB defaults, and ported settings.
    - Files link in place with monitored state preserved.
    - The full catalog is present for each studio, and the queue shows no searches.
    - The report lists the misses.
    - The legacy DB's and `config.xml`'s checksums are unchanged.
    - Rolling back to the v2 image still boots v2.
  - **Eros path:** StashDB kept, so the library carries over intact. Then TPDB chosen, so
    it rebuilds and files are detached and re-linked, with nothing moved.
  - **Postgres:** run the v2 and Eros paths again.
  - **Onboarding:** add a 500+ scene studio, and separately a large performer.
    - Check the call count against the table in C.
    - Scenes should be visible before performers hydrate, and covers should fill in on
      their own.
  - **Compatibility:** Prowlarr sync works against `/api/v3`.
  - Screenshots with SFW mode on.
