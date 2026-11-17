# Covers for bulk-added items

## Intent

Items added through the inline bulk refresh (`IRefreshNewScenes.RefreshNewScenes`) never get their
covers. The inline refresh deliberately skips `MovieUpdatedEvent` (61f6762f1e), and that event is
the only thing that queues movie covers. The refresh does fetch the cover URLs and store them, but
nothing downloads them until a later metadata refresh. For scenes and JAV, the scheduled refresh
only refreshes what the TPDB changed-feed lists, so an untouched item may never get one.

On one production library this left 87 items without covers (JAV and scenes linked through
Unmapped Files). Covers that are missing also mean no extras metadata images
(`ExtraService.CreateAfterMediaCoverUpdate`) and no SignalR push of the new poster, since both hang
off `MediaCoversUpdatedEvent`.

## Affected paths

Every caller of `RefreshNewScenes`:

- `UnmappedFileService` (linking a file to an item not yet in the library). Seen on production.
- `SceneCatalogImporter` (new scenes from a monitored studio or performer refresh). From the code.
- `V2TpdbImportRunner`, `V2StashImportRunner`, `ErosSeedImportRunner`, `ErosRebuildImportRunner`.
  From the code.

Performers and studios are unaffected (all visible performers and all studios had their images on
the same library; hidden performers keep no artwork by design).

## Goals

- Every item added through `RefreshNewScenes` gets its covers downloaded in the background.
- Downloads use the URLs the inline refresh already stored. No metadata is fetched again.

## Constraints (anti-goals)

- **Never block the caller.** A migration or catalog import of thousands of items must run at the
  same speed as today. The existing cover queue waits when full (capacity 100, from #644), so bulk
  work must not go through that wait.
- **No per-item disk checks in bulk work.** No `FileExists`, no `.source` read, and no walk of
  `MediaCover/` to fill a cache. On large libraries (400K+ items) these cause contention. The
  download and its write are the only disk work.
- No re-refresh of metadata to get covers.
- `RefreshNewScenes` keeps not publishing `MovieUpdatedEvent`.

## Non-goals

- No backfill of items already missing covers. They land when they are next refreshed (manual
  Refresh, or a TPDB change). On the library above, a manual Refresh of the affected studios
  fixes them.
- No change to the scheduled RefreshMovie task. Refreshed items already get covers through the
  event; the backoff also backs off covers, by decision.
- The backlog is in memory. Ids still queued at shutdown are dropped and come back with the next
  refresh of those items. Accepted.
- The studio cover content-type match (`"image/jpeg "` with a trailing space in
  `DownloadCover(Studio, …)`) is a separate bug and a separate PR.

## Approaches considered

1. **A separate id backlog that never waits (chosen).** Bulk callers add ids and return
   immediately. Workers drain it after the existing queue. Handles thousands of items without
   blocking anything.
2. **The backlog plus a sweep for items missing covers** (at startup, or inside the daily refresh
   loop). Rejected: one disk check per item, on every run, in a bulk task.
3. **Publish `MovieUpdatedEvent` from the inline refresh again.** Rejected: bulk adds would wait
   on the full queue and run all six handlers per item.

## Design

- **New interface** in `NzbDrone.Core.MediaCover`, implemented by `MediaCoverService`:
  `void QueueNewMovieCovers(IEnumerable<int> movieIds)`. It adds ids to an in-memory backlog (a
  queue plus a set to drop duplicates) under the existing `_movieCoverQueueLock` and returns. It
  never waits, and does no I/O.
- **`RefreshNewScenes`** calls it once with the ids it refreshed, after its loop (in the same
  `finally` that runs `ScanMovies`, so a loop that throws still queues what it refreshed).
- **Workers** take from the existing queue first and from the backlog only when that queue is
  empty, so a single add from the UI never waits behind an import. Adding to the backlog wakes a
  worker.
- **During a migration** (`ILegacyMigrationGate.AutomationPaused`), workers leave the backlog
  alone, so covers don't compete with the migration for bandwidth or CPU.
  `LegacyMigrationCompletedEvent` wakes them to drain it.
- **Processing a backlog id:** one database read for the movie (with its metadata) by id. If the
  movie is gone, skip it. If the existing queue already has work for that id (pending, or a newer
  sequence), drop the backlog entry: that work covers it. The existing deletion tombstones apply
  the same way.
- **No existence check.** The item was just added, so its covers are known to be missing.
  Backlog work downloads each cover without calling `AlreadyExists`, records its `.source` as
  today, and forces the resize (which already skips its own `FileExists` when forced). If a folder
  is left over from a reused id, the download overwrites it.
- `MediaCoversUpdatedEvent` is published as today, so extras images and the UI update follow.

## Left to the builder

- How the backlog flag reaches `EnsureCovers` / `DownloadCover` (a parameter, or a separate entry
  point), as long as the event path keeps its `AlreadyExists` check.
- How worker wake-up is signalled for backlog adds and on migration completion, within the
  existing lifecycle and shutdown handling.

## Tests

`MediaCoverServiceFixture` and `RefreshMovieServiceFixture`:

- `RefreshNewScenes` queues covers for every refreshed id, including when an item fails to
  refresh or the loop throws, and still publishes no `MovieUpdatedEvent`.
- `QueueNewMovieCovers` returns without waiting when the existing queue is full.
- Backlog work downloads without `FileExists` or `.source` reads on the cover paths.
- Workers prefer the existing queue over the backlog.
- Backlog work is held while the migration gate is paused and drains on
  `LegacyMigrationCompletedEvent`.
- A deleted movie, or an id the event path already covers, is skipped.
- Duplicate ids are downloaded once.
