# Migration wizard (workstream G): design

## Intent

A v2 or Eros (v3) user swaps the Docker tag or installs the v5 build and opens the web UI. Instead of the app, they see a friendly "Migration needed" page. It says what was found and what will happen, and lets them pick their metadata sources. One click backs up the legacy data and builds a separate v5 database and config. The app then opens and fills itself in, showing real progress, until the library is back the way they had it:
- monitored state is the same;
- files keep their names and folders;
- nothing on disk has moved.

The legacy DB and `config.xml` are never written to, so going back to the old tag still works. This is the last alpha workstream (roadmap `docs/v5-roadmap.md`, G). It builds on A (own storage, legacy guard), C (bulk onboarding), D (fingerprints), E (source lock, TPDB scenes) and F (flat folders, v2 token map).

## Goals

- Detect a legacy install (v2, Eros, or both) before any migration runs, and halt on the migration page.
- Phase 1: back up, seed `config5.xml`, and build the v5 DB, with progress shown on the halt page.
- Phase 2: a resumable library import that runs inside the normal app. Its progress, with a real percentage, shows in System → Tasks.
- No automatic grabbing until the migration is complete.
- A migration report that lists everything that didn't carry over.
- OIDC login, ported from v2 into v5, so v2 users who log in with OIDC keep working after migration (plz12345).

## Non-goals

- Porting v2 import lists.
- Carrying over v2 history, download history or the blocklist.
- A separate `MediaCover` folder or new log file names for v5.
- Container images (workstream H).

## Anti-goals (any of these is a failure, even if everything else works)

- The migration renames, moves or deletes a user file.
- The legacy DB, `config.xml` or the legacy Postgres DBs are written to.
- A grab happens during the migration because a scene looked missing before its file was linked.
- A wrong identification is linked silently. Anything weaker than the D auto-link rules goes to manual import.
- A restart in the middle of phase 2 loses work, or calls the migration done when it isn't.

## Decisions

### Detection

- Detection happens in `Startup` before the main migration runs. The A guard (`LegacyDatabaseGuard`, `LegacyDatabaseException`) already refuses to migrate a DB without the v5 marker. G turns that refusal, and the AppData scan below, into setup mode instead of a startup failure.
- Setup mode is entered when there's no v5 DB with the marker and any of these is true:
  - `whisparr2.db` or `whisparr3.db` exists in AppData;
  - the configured Postgres main DB (from `config.xml` or `Whisparr__Postgres__*`) has the `Series` (v2) or `Movies` (Eros) table.
- Which app made a DB: `Series` without `Movies` means v2; `Movies` means Eros.
- **Refusals**, each shown on the halt page with what to do:
  - v2 schema older than 221: "Update v2 first".
  - An Eros DB containing a migration v5 doesn't know: "Update v5 first".
- **Setup mode:** a dummy DB and no lifecycle events, so no command threads and no scheduler. It serves the normal frontend bundle with a `/setup` route, backed by a `SetupController` outside the versioned API.

### The halt page (one screen)

The approved layout is option B: a summary sits above the source form, on one page.
- **Summary:** what was found (app, DB, and counts of studios, scenes and files). Then: the legacy data is backed up first and never changed, nothing on disk moves, and renaming is disabled.
  - The wording is **"renaming is disabled"**, not "off".
  - A "What carries over?" link expands the table under "Data disposition" below. It also holds:
    - the advice for v2 users to finish or clear their download queue first;
    - the reset and downgrade notes.
- **Both legacy DBs present:** the summary becomes a choice between v2 and Eros, and the form's defaults follow that choice.
- **Source form:**
  - It offers a scene source (StashDB or TPDB) and a movie source (TMDB or TPDB).
  - The previous source is marked "your previous source; fastest, most faithful". Picking a different one shows "rebuilds from your files; much slower".
  - Picking TPDB for scenes strongly suggests TPDB for movies too.
  - A line says the choice can't be changed later without starting over.
  - **Defaults:**
    - v2: TPDB for scenes and movies.
    - Eros: StashDB for scenes, plus the configured movie source. An Eros movie source of `None` defaults to TMDB. (my call)
- **Buttons:**
  - "Back up & migrate".
  - "Shut down": exits cleanly and logs how to go back to the old tag.
- **Auth:** v5's built-in rules apply. Auth is required, with the optional "disabled for local addresses". There are no v5 users yet, so credentials are checked read-only against the legacy `Users` table, and the auth settings come from the legacy `config.xml`. The same users and settings then carry into v5. (my call)
  - **OIDC:** v2's OIDC authentication (`AuthenticationType.Oidc` and its config keys and handlers in `~/Git/Whisparrv2`) is ported into v5 and lands before the wizard. A v2 OIDC install keeps OIDC: its keys carry over through `config5.xml`, and the halt page signs in through it. If OIDC isn't configured, v2's own fallback to forms applies.
  - If the legacy install has no usable credentials (no `Users` row, or auth turned off), the halt page is served only to local addresses, and the page explains why. (my call)

### Phase 1: backup and build (setup mode, progress on the halt page)

Each step shows on the halt page as it runs. On a small library it flashes past.

1. **Backup, as a hard gate.** If it fails, nothing else runs.
   - It writes `AppData/Backups/pre-v5/<timestamp>.zip` and shows the path.
   - SQLite: the legacy DB is copied through the SQLite backup API on a read-only connection, along with `config.xml`.
   - Postgres: an in-process dump of every table (Npgsql `COPY … TO STDOUT` plus the DDL), along with `config.xml`. No `pg_dump` binary is needed.
2. **Seed `config5.xml` from `config.xml`.**
   - Every key is copied.
   - The v2 key renames are applied.
   - The Postgres DB names are set to the v5 targets, so the config never points v5 at a legacy DB.
3. **Create the v5 DB.**
   - **SQLite:** write `whisparr5.db`.
   - **Postgres:** try `CREATE DATABASE` for the v5 main and log DBs (`whisparr5-main` and `whisparr5-log` unless configured otherwise).
     - On failure, the wizard **halts on the page**. It shows the error, the exact SQL to run, and the env vars to set, plus a Retry button.
     - If the legacy DB was named by `Whisparr__Postgres__*`, the last screen says which env vars to change to the v5 names. Until they're changed, the existing guard message stops startup.
     - **Safety rule:** the wizard never creates, drops or templates onto a DB it didn't create in this run. If a v5 target name is the same as a legacy DB name, it refuses on the page. A target that already exists must be empty, or be a v5 DB with the marker. Discarding an interrupted phase 1 drops only DBs the wizard recorded as created by itself. (my call)
   - **Skyhook preflight:** when the chosen path needs Skyhook (every path except Eros with the same source and no mixed movies), the wizard checks that Skyhook answers the v5 routes before it starts. On failure it halts on the page with a Retry button, the same way as Postgres. (my call)
4. **Fill it**, depending on the source (see the next section).
5. **Lock the sources.** Call `LockMetadataSources(scene, movie)` with the picked values after the migrations. On an Eros copy, migration 301 will already have locked StashDB; this overwrites it with the user's choice.
6. **Force renaming off:** `RenameScenes = false` and `RenameMovies = false`.
7. **Record the migration state** (see "Resume") and leave setup mode. Normal startup continues, and phase 2 is queued.

**Phase 1 is all or nothing.** If it's interrupted, the partial v5 DB and `config5.xml` are discarded, and the next start shows the halt page again. (my call)

### Building the v5 DB, by source

- **Eros, same scene source:**
  - Copy the DB, then run migrations 300 onward.
    - SQLite: a file copy through the backup API.
    - Postgres: `CREATE DATABASE … TEMPLATE <legacy>`. If the template copy fails but a plain create succeeds, create an empty DB and stream the tables across with the same `COPY` code the backup uses. (my call)
  - All v3 settings carry over wholesale. Only three settings are new: `WhisparrSceneMetadataSource`, `MetadataSourcesLocked` (both from the picker) and `EnablePerceptualHashing` (its default).
  - **Mixed movies** (roadmap decision 12) are handled in phase 1. Rows matching the chosen movie source are kept. Each other row either:
    - duplicates a kept row: its files are merged into the kept row and the duplicate is dropped; or
    - has no kept twin: it's detached and re-identified in phase 2.
  - If there are no mixed movies, phase 2 has nothing to do. `Completed` is written in the same transaction that records the migration state, so there's no gap between the two. The report and its health notice still appear. (my call)
- **Eros, new scene source:**
  - Do the same copy.
  - Then turn the scene, studio and performer rows into seeds for phase 2, and detach the scene files.
- **Detach means:** a `MovieFiles` row moves into `ImportFiles` with its path and hashes. It's never deleted, and nothing on disk is touched. Re-linking is then a hash or path lookup.
- **v2:**
  - Run a fresh migration.
  - The porter reads the legacy DB through a read-only connection and writes the settings (next section).
  - Phase 1 also snapshots the v2 library rows phase 2 needs (`Series`, `Episodes`, `EpisodeFiles`, monitored flags, numeric TPDB IDs) into staging tables in the v5 DB. Phase 2 then never touches the legacy DB, so it can resume even if the legacy DB is gone. (my call)

### Data disposition

| | Eros, same source | Eros, new source | v2 |
|---|---|---|---|
| **Copy as-is** | the whole DB | the whole DB, then the library rows below are rebuilt | `Tags`, `RootFolders`, `Users`, `DelayProfiles`, `RemotePathMappings`, keeping IDs so tag references hold |
| **Port** | n/a | studios, performers and scene rows (`Movies`, `MovieMetadata`, `Credits`, `AlternativeTitles`) become seeds; scene `MovieFiles` become `ImportFiles`; scene `ImportExclusions` map through Skyhook where it knows both IDs, and the rest are reported | `Config` (key renames), `NamingConfig` (the F token map, flat folder format, colon 5 → 4), `QualityDefinitions`, `QualityProfiles` (5 IDs remapped, ranks rebuilt into `QualityProfileQualityRanks`), `CustomFormats` (enum remap), `ReleaseProfiles` (`IndexerId` → `IndexerIds`), `AutoTagging` (the `Network` spec is dropped), `Metadata` (Xbmc, Roksbox and Wdtv map; Plex is dropped), `Indexers` (v2-only fields dropped, Fanzub skipped), `DownloadClients` (`tv*` → `movie*`, RQBit and Tribler skipped), `Notifications` (event columns renamed, email encryption converted) |
| **Stream** (phase 2) | n/a | the seeds above | `Series` → studios with their full catalog; `Episodes` → scenes, keeping `Monitored`; `EpisodeFiles` → `MovieFiles`, linked in place |
| **Leave** | the logs DB | the logs DB; `ImportListMovies`; `ExtraFiles`, `SubtitleFiles` and `MetadataFiles` for scenes (a rescan adopts them again); `History` and `Blocklist` rows for rebuilt scenes (housekeeping removes them once their `MovieId` points at nothing) | `ImportLists`, `ImportListStatus`, `ImportListExclusions`, `CustomFilters`, `History`, `DownloadHistory`, `Blocklist`, `PendingReleases`, `Commands`, `ScheduledTasks`, every `*Status` table, `UpdateHistory`, `ExtraFiles`, `SubtitleFiles`, `MetadataFiles`, and the logs DB |

- Every table or setting that's left behind or dropped gets a count in the migration report.
- `ScheduledTasks` is left behind safely: v5 recreates missing tasks with their last run set to now, so nothing fires at first boot.
- Downloads still in a v2 client can't be matched back to their scene once history is dropped, so they need a manual import. The halt page tells v2 users to finish or clear their queue first.
- The v2 → v5 naming token table lives in the F spec ("For G: token map"). G owns the final version. `{TpdbSceneId}` maps to `{Scene ForeignId}` and is noted in the report. No numeric token is added. Renaming is disabled, so no file changes because of this. (my call)
- v2 `Users` rows copy as-is. The plan verifies that v5's password hasher accepts v2 hashes, and adds a conversion step only if it doesn't. (my call)

### Phase 2: library import (normal app, resumable)

- One long-running command, the migration import, runs inside the normal app.
- **v2, scenes on TPDB** (the seeded path):
  - Numeric `Series` IDs resolve to TPDB UUIDs through Skyhook `tpdb/resolve`.
  - Each studio's full catalog comes in through C.
  - `Episodes.Monitored` is copied onto each scene. A catalog scene v2 never stored follows the series' `MonitorNewItems` setting: "all" means monitored, anything else means unmonitored. The studio's own monitored state comes from `Series.Monitored`. (my call)
  - Each `EpisodeFiles` row links to its scene by the episode's TPDB ID, at its existing path. The scene's folder is the folder the file is in, and the studio's root folder is the v2 series' root folder. (my call)
  - Only files with no usable legacy link fall back to the D order.
- **v2, scenes on StashDB:**
  - Map each studio through Skyhook's TPDB↔StashDB mapping, or by title search.
  - Identify each file in the D order: parser, then oshash, then phash.
  - Monitored state comes from the matched v2 episode.
- **Eros rebuild:**
  - Studios and performers come from the seed rows, through Skyhook mappings or search.
  - Only the scenes and movies the user had are re-identified, by detached hash, path and parser.
  - Monitored state is copied.
- **Unmatched files** go to `ImportFiles` for manual review.
- **Confidence bar for studios and performers:** a Skyhook ID mapping links automatically. A title search links automatically only for a single exact match on the normalized title. Anything else goes to the report as "needs attention" and isn't linked. (my call)
- **The app is usable during the import, but slow.** Anything that needs Skyhook, such as searches, competes with the import. The health notice says so.

### Paused automation

While the migration is incomplete, the following are paused:
- RSS sync;
- the scheduled missing and cutoff searches;
- search on add;
- import-list sync (Eros keeps its lists);
- importing completed downloads. They wait in the client and are processed once the migration completes, so a download can't land on a scene before that scene's legacy file is linked. (my call)

User-started searches are still allowed. (my call)

### Progress in System → Tasks

- Commands gain an optional progress value: `phase`, `done` and `total`. It's stored on the command model, sent on the command resource, and pushed over SignalR the same way `Message` is today.
- The queue row in System → Tasks → Queue shows a progress bar, a percentage and an ETA for any command that reports progress. The migration import is the first user. Studio refresh and C's bulk import could adopt it later.
- **The percentage is a real count**, not a guess:
  - Totals are known up front: studios to fetch, scenes to upsert, files to link, files to identify.
  - Each phase is weighted by its share of the work.
  - The ETA comes from the rate so far, and is shown only after about a minute of data. (my call)
- **Health notice:** while the import runs, it says "Library migration in progress, see System → Tasks". When the import finishes, it becomes the link to the migration report, and stays until the user dismisses it.

### Resume

- The migration state lives in the v5 DB (a v5 migration numbered ≥ 305). It records:
  - the source kind;
  - the phase;
  - how far each phase has got (cursors and counts);
  - per-item done markers;
  - a `Completed` flag.
- On every startup with an incomplete migration, the import is queued again and continues from the recorded point.
- **The migration isn't done until it's done.** `Completed` is set only after the last phase finishes. Until then the automation pause holds and the health notice shows.
- **Items that fail every retry:** Skyhook errors, timeouts and 202 or 503 answers are retried with backoff. After repeated failures spread over time, the item goes into the report as "needs attention" and counts as done. Unresolved items therefore never hold the migration or the automation pause open (plz12345). The builder picks the retry count and spacing.
- Work is idempotent: re-running an item that already finished changes nothing. Imports already dedupe by foreign ID (C/E), and file links are checked before they're written.

### Migration report

- Stored in the DB. It lists:
  - skipped settings;
  - dropped tokens;
  - left-behind counts per table;
  - unmatched files, with a link to manual import;
  - items needing attention.
- It's shown at System → Migration Report, with a download button, and the health notice links to it.

### Reset and downgrade (user-facing notes)

- **Reset:**
  1. Stop the app.
  2. Delete `whisparr5.db` and `config5.xml` (Postgres users drop the v5 DBs).
  3. Start again. The halt page returns and the migration runs from scratch.
- **Downgrade:**
  1. Kill the app.
  2. Go back to the old tag.
  3. Possibly purge `MediaCover`. v5 writes into the same folder, and on the v2 path a v5 scene ID can equal a v2 series ID.
- Log files keep the `whisparr.txt` name. The rotation risk is accepted because each log carries its version.

## Constraints

- Legacy files and DBs are opened read-only, every time.
- No step touches media files. Renaming stays disabled after migration until the user turns it on.
- v5-only DB migrations are numbered ≥ 305.
- Forward only: no backports to Eros.
- No infrastructure details in the repo.
- **Tests:**
  - NUnit `*Fixture`, 80% coverage on new code.
  - Every test project runs (Core, Common, Api, Host, Mono).
  - `yarn typecheck`, lint and build for the frontend.
  - Fixtures for detection on each DB shape: v2 SQLite, Eros SQLite, both, v2 and Eros Postgres, and a legacy DB named by an env var.
  - Porter tests driven by a real v2 DB copy, plus Eros copy-and-migrate, mixed-movie detach and merge, resume after a kill in phase 2, and the paused-automation gate.
- **Live check on the 6969 dev instance:**
  - the v2 path, with a copy of a real `whisparr2.db` and `config.xml`;
  - the Eros path, both keeping StashDB and switching to TPDB;
  - Postgres for both;
  - legacy checksums unchanged afterwards;
  - rolling back to the old tag still boots;
  - no grabs in the queue during the import;
  - screenshots with SFW mode on.

## Left to the builder

- How setup mode is wired into `Startup` (dummy DB, `SetupController`, route) and how the halt page's phase 1 progress reaches the browser (polling or SignalR).
- The migration-state and staging table shapes, and the report storage.
- How the paused-automation gate is applied (task skip, decision rejection, or both), as long as nothing automatic is grabbed.
- How the health notice is dismissed.
- The exact v2 config-key rename list and the final token table, from recon of both trees.
- How the progress weights are split between phases.
- Task order. The OIDC port is its own early task. Detection, setup mode and the backup gate land first among the wizard tasks, before anything can write a v5 DB from a legacy one.
