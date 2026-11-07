# v5 JAV in the client — design

Status: agreed with plz12345 on 2026-11-07. Skyhook v5 serves JAV from a fully hydrated cache, which the roadmap's "JAV (Skyhook side built)" section describes.

## What JAV is in v5

- **Its own content type.** JAV is not a scene and not a movie: `ItemType.Jav`, with metadata source `TpdbJav`.
  - Both enums gain a value appended at the end, so `Jav = 4` and `TpdbJav = 4`, and stored ints keep their numbers.
  - A JAV release is a `Movie` row whose `ForeignId` is the TPDB JAV UUID.
- **TPDB only.** JAV studios and performers are TPDB sites and performers, so JAV is available only when the locked scene source is TPDB.
  - Under StashDB the studio and performer ids don't line up, so JAV is unavailable everywhere: no nav section, no toggles, and the API rejects it.
  - The movie source (TMDb or TPDB) doesn't matter.
- **Identity is the release code** (e.g. `ABP-123`), stored in `MovieMetadata.Code`.
  - Codes are normalised to upper case with one dash: `abp123`, `ABP 123`, `ABP_123` and `abp-123` all become `ABP-123`. This uses the same rule as Skyhook's `JavCode`.
  - TPDB titles start with the code (`ABP-123 - Momo Sakai …`). The client strips a leading `<code> - ` from the title so the code isn't shown twice.

## Skyhook surface used

All routes are under `tpdb/`. JAV has no StashDB route.

| Need | Route |
|---|---|
| One release | `jav/{uuid}` |
| Add JAV search (JAV-only; code-shaped terms try an exact code match first) | `jav/search?q=` |
| Changed releases | `jav/changed?since=` |
| A studio's or performer's catalog (the `scenes/full` paging contract) | `site/{uuid}/jav/full`, `performer/{uuid}/jav/full` |
| Has JAV | `HasJav` on `site/{uuid}` and `performer/{uuid}` (Skyhook prerequisite below) |
| v2 numeric id → UUID | `POST resolve`, kind `jav` |

**Skyhook prerequisite:** the site and performer responses carry `HasJav` (`true`, `false` or `null`, where `null` means unknown or JAV off).
- The client stores the last non-null value on the studio or performer at refresh time.
- That lands in Eros-Metadata first, from its own plan.

## Library

- **Nav:** a JAV section between Movies and Studios, with an index at `/jav` and "Add New" at `/add/new/jav`.
  - The index is the Movies index driven by an `itemType` prop, with its own persisted options.
  - Tiles are portrait 3:4 (height = 4/3 × width).
  - TPDB JAV posters are the full DVD jacket (landscape, back|spine|front), so tiles crop with `object-fit: cover; object-position: right` to show the front cover. The details page poster crops the same way.
- **Add JAV:** the Add New search page with `itemType=jav` calls `movie/lookup?itemType=jav`, which proxies `tpdb/jav/search`. Results are JAV only.
- **Studio search is unchanged.** One TPDB studio can hold scenes, JAV and movies.
  - Studios and performers gain `JavMonitored` beside `Monitored` (scenes) and `MoviesMonitored`, plus `HasJav`, `JavCount` and `TotalJavCount`.
  - The JAV toggle shows only when JAV is available and `HasJav` is true.
- **Sync:** a JAV-monitored studio or performer walks its `jav/full` pages with the existing scene catalog importer. It uses the same monitor-new-items, search-on-add and give-up rules as scenes.
- **Everywhere else, JAV appears where movies do:** Calendar, Wanted (Missing and Cutoff Unmet), Queue/History, library search (its own bucket) and System status counts.

## Files on disk

- JAV gets its own naming settings: `RenameJav`, `StandardJavFormat` and `JavFolderFormat`.
  - Defaults: folder `jav/{Studio CleanTitle}/{Scene Code}`, file `{Scene Code} - {Scene CleanTitle} {[Quality Title]}`.
  - Studio and scene tokens are available, as for scenes.
- File and folder handling (one release per folder, no flat layout, the import and rescan rules) behaves like a movie.

## Search and import

- **Indexer search:** a JAV release searches with `SceneSearchCriteria`. The scene titles are the code variants `ABP-123` and `ABP123`, with no date and no studio, so indexers query by code in their XXX categories.
- **Matching:** a parsed release or file whose code normalises to a JAV code matches the JAV release with that code (`ItemType.Jav`, case-insensitive on `Code`) before any title or studio matching.
  - Parser code extraction already exists. The new part is the lookup, plus the normalisation of the parsed code.
- **Unmapped Files:** when a file's parsed code is JAV-shaped and JAV is available, the suggestions search JAV.

## v2 migration gap

v2 marks JAV studios with `Series.SeriesType = 1`. Today the snapshot stages it but nothing reads it.

- **TPDB scene source:**
  - A JAV series imports as a studio with `JavMonitored` taken from the series and `Monitored` off.
  - Its episodes resolve through `resolve` kind `jav`, and the catalog walks `site/{uuid}/jav/full`.
  - Files link to the resolved release, exactly as the TPDB scene runner does. Episodes with no file keep their v2 monitored state.
- **StashDB scene source:** JAV can't be carried over.
  - Each JAV series writes one `NeedsAttention` report row: "JAV needs the TPDB scene source; N wanted, M files parked".
  - Its files are parked with that reason. Nothing is dropped silently.

## Out of scope

- Folder import ("Import JAV" from an existing tree).
- Import lists for JAV.
- FC2-PPV multi-part codes, which Skyhook's code search doesn't match either.
- Custom formats keyed on JAV.
- Per-release JAV fingerprints.
