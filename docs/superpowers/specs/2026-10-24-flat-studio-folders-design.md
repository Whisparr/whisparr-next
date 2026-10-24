# Flat studio folders (workstream F): design

## Intent

Many v2 users keep scenes flat, several to a studio folder:

```
{root}/{network}/{studio}/{scene 1}.mp4
{root}/{network}/{studio}/{scene 2}.mp4
```

Every folder level can use the normal naming tokens. v5 must support this layout natively, so that:

- a v2 migration (workstream G) needs no file moves;
- a v3 user or a new install can switch to it whenever they like.

v5 supports the layout but doesn't default to it. The defaults stay the v3 baseline: one folder per scene.

**Why it's hard.** The code is inherited from Radarr, and Radarr assumes a folder is one movie. Once several scenes share a folder, that assumption doesn't just misbehave. It deletes user files. The live spike (below) showed sibling files claimed by the wrong scene and sidecars deleted from disk. Reading the code showed that deleting one scene with its files deletes the whole studio folder.

**Core principle: in a shared folder, a scene owns files, not the folder.**

## Goals

1. A scene folder format that ends at studio level (or any level without a per-scene segment) is accepted and works end to end.
2. **A Whisparr operation never deletes, moves or claims a file the scene doesn't own.** This covers rescan, rename/organise, delete, move and manual import.
3. **New downloads** land in the studio folder, named by the scene file format, exactly as imports work today.
4. **Organise (Rename) moves existing scenes into the flat layout** exactly as an import would place them:
   - each owned video file and its owned sidecars move and are renamed per scene;
   - old per-scene folders are removed once empty.
5. **Sidecars stay next to their video file and are named per scene.** Kodi metadata becomes `<file>.nfo` and `<file>-poster.jpg`, not `movie.nfo` and `poster.jpg`.
6. **StashApp renames are followed** (best effort):
   - an in-place rename re-links by oshash, which already works;
   - a file moved to another library folder is re-linked when a later scan finds it.
7. **The v2 folder and file naming carry over as is** (G uses this): a v2 `SeriesFolderFormat` maps to a v5 scene folder format that reproduces the same paths.

## Non-goals

- Changing the default scene folder format.
- Movies. Everything here is scene-only (`ItemType.Scene`); movie behaviour stays exactly as it is.
- Moving files during migration. G ports the formats with renaming off.
- Surfacing unknown files that sit in shared studio folders (files no library scene owns) in Library Import. They're left alone. Reporting them is G's job (migration report) or later work.
- Following StashApp moves into folders no scan visits, such as a folder outside every scene's path.

## Anti-goals (any of these is a failure, even if everything else works)

- **Any user file deleted, recycled or moved that the acting scene doesn't own.** This includes sidecars and including the whole studio folder.
- **Sidecars deleted from disk because their video went missing.** That's what happens after a StashApp move or rename, and it applies in every layout, not just flat.
- **A file linked to two scenes**, or a scene claiming a sibling's file.
- **A per-scene-folder library changing behaviour.** Users who never adopt flat must notice nothing except the bug fixes below.

## Spike findings (2026-10-24, dev instance, APFS-cloned test files)

| # | Finding | Status in v5 today |
|---|---|---|
| 1 | The naming API rejects a folder format with no scene-title token. | Blocks flat entirely. |
| 2 | Adding a scene whose path equals another scene's path fails: "Path … is already configured for an existing movie" (`MoviePathValidator`, via `AddMovieValidator`). | Blocks adding scenes in flat. |
| 3 | Rescan of scene X imports *every* video and sidecar in X's folder as X's (`DiskScanService` passes X into `GetImportDecisions`, which never checks ownership). Two scenes each ended up with both files. | Data corruption. |
| 4 | Organise moved the video files to the flat folder, but sidecars stayed in the old per-scene folders and those folders were left behind. `ExtraFileManager.MoveFile` builds the old path from the *new* `movie.Path` plus the old relative path, finds nothing and skips. | Bug in **every** layout whenever a rename changes the scene's folder. |
| 5 | A StashApp move left S1's file missing. Its `MovieFileDeletedEvent` (`MissingFromDisk`) **recycled every sidecar linked to it from disk**, including the sibling's subtitle that S1 had wrongly claimed (`ExtraFileService.HandleAsync(MovieFileDeletedEvent)`). | Catastrophic, and a hazard in every layout. |
| 6 | Re-link by oshash only looks in the scene's own folder (D). A file moved elsewhere is dropped as missing. | Expected, but must re-link when found later. |
| 7 | `MediaFileDeletionService.HandleAsync(MoviesDeletedEvent)` checks for shared paths only *among the scenes being deleted in that request*, then recycles `movie.Path`. Deleting one flat scene with "delete files" recycles the whole studio folder. Found by reading the code; the live run couldn't show it because only the deleted scene's file was left by then. | Catastrophic. |

Working today: organising video files into the flat folder (the file and `Movie.Path` update correctly), and in-place re-link by oshash.

## Decisions

### Folder model

- **`Movie.Path` keeps its meaning:** the folder the scene folder format produces. In flat mode that's the studio folder, shared by every scene in it.
- **`MovieFile.RelativePath`** stays relative to `Movie.Path`. In flat mode that's the bare file name.
- **A scene's folder is *shared* when any of these holds:**
  - the scene folder format has no per-scene segment, i.e. it's flat;
  - another item's `Path` equals the scene's `Path`;
  - the scene's `Path` is an ancestor of another item's `Path`.

  The last two cover mixed states, such as a library half-organised after a format switch, or migrated data. Otherwise the folder is *exclusive* and today's per-scene behaviour applies unchanged.
- **One service answers "is this scene's folder shared?"** for rescan, delete, move, rename, extras and metadata. It combines the naming config with an in-memory index of item paths, which is invalidated when an item is added, deleted or has its path changed. Metadata consumers and per-scene scans must not query the DB per call (my call).
- **How "flat" is detected** is up to the builder, but it must not depend on the token being literally `{Studio …}`. A v2 user may build the studio level from other tokens. The format is flat when its last path segment contains no scene-level token (a title, date, ID or performer token).

### Ownership rule (shared folders)

- **A scene owns a video file** when one of these holds:
  1. it has a `MovieFile` row whose path is that file; or
  2. the file identifies as that scene. The order is D's: the parser (`FindScene` with this studio), then an oshash match (the local `MovieFiles.OsHash`, then the fingerprint lookup's Hash tier).
- **A scene owns a non-video file** when it's tracked as that scene's extra/metadata file, or when one of these holds. **Reuse v2's approach rather than inventing a new one.**
  1. **The sidecar's own name identifies the scene.** This is v2's `ExistingSubtitleImporter`, `ExistingOtherExtraImporter` and `ExistingMetadataImporter` (`~/Git/Whisparrv2/src/NzbDrone.Core/Extras/`). They parse each sidecar's name through the same identification as video files, link it to that item's file, and skip it when the name maps to more than one file. The v5 importers get the same shape, with the parser plus `FindScene` in place of v2's episode aggregation.
  2. **Fallback, when the name doesn't parse:** it starts with the base name of a video file the scene owns, **followed by a boundary** (`.`, `-` or the end of the name). So `Title.en.srt` and `Title-poster.jpg` match `Title.mp4`, but `Title 2.nfo` doesn't. When several owned base names match, the longest wins; a tie leaves the file unowned (my call).
- **The import-time sidecar test** in `OtherExtraService`/`SubtitleService.ImportFiles` (a plain `StartsWith`, the same in v2) gets the boundary rule. v2 rarely needs it because `S01E01` names are seldom prefixes of each other; scene titles can be.
- **A file that identifies as a scene that already has a file** goes through the same import decision rules an exclusive folder applies today: upgrade, or rejected as not an upgrade. No new duplicate handling (my call).
- **Generic-named files in a shared folder belong to no scene.** Examples: `movie.nfo`, `poster.jpg`, `fanart.jpg`. Whisparr never claims, renames or deletes them.
- **A file that identifies as no scene, or as more than one, is left alone.** It's never imported into the scene being scanned.

### Rescan

- In a shared folder, rescan imports only files the scene owns, using the rule above.
- **Each file is identified once per scan, not once per scene.** A library-wide rescan of a studio folder with N scenes must not parse every file N times. One approach is to scan by folder: list the folder once, identify each unmapped file once, and hand each one to its owner. The approach is the builder's call; the performance requirement is not.
- The unmapped-file cleanup at `DiskScanService.cs:213-230` matches with `OriginalFilePath.Contains(movie.Path)`. It must match owned files only.

### Re-linking (StashApp)

- **In-place rename:** D's oshash re-link stays. In a shared folder, its candidates are the folder's unowned files, not every new file in it.
- **Cross-folder move** (my call on the mechanism):
  - When an owned file goes missing and no in-folder re-link applies, record a *lost file* entry: oshash, size, movie ID, the old relative path and when it was lost. Keep it for 30 days.
  - A later scan of any scene folder in the library hashes its unowned video files (oshash is cheap). A file that matches exactly one lost entry, by oshash and size, is re-linked in place to that scene.
  - The re-link moves nothing on disk. `Movie.Path` follows the file's folder; in an exclusive folder the per-scene folder becomes the file's folder.
  - The next organise puts the file back into the configured layout.
  - Re-linking across root folders is out of scope.
- A missing file still clears `HasFile` as it does today; the lost entry is what lets it come back.
- A lost entry is dropped when its scene is deleted, when the scene gets another file, or after 30 days, whichever comes first. A housekeeping task removes expired entries (my call).

### Sidecars and metadata

- **`MissingFromDisk` never deletes extras or metadata files from disk, in any layout, for scenes and movies alike.** The DB rows go, or are detached; disk is untouched. For movie libraries the visible change is that sidecars of a vanished file stay on disk. That's the intended fix, not a regression. Disk deletion of extras happens only on user-initiated deletion and on upgrade (replacement), and only for extras the scene owns.
- **Fix the extras move on rename** (finding 4, every layout):
  - Resolve each extra's current location from the folder the scene was in before the rename, not the new `movie.Path`. `MovieFileRenamedEvent`/`MovieRenamedEvent` already carry the previous paths.
  - Move and rename owned extras next to the video file.
- **Metadata consumers in a shared folder** write per-file names only, following v2's per-file Kodi naming but **movie-style** (plz12345's decision: a `<movie>` nfo, never v2's TV-style `tvshow.nfo`/`<episodedetails>`):
  - Kodi `UseMovieNfo` is ignored there, so the file is `<file>.nfo`.
  - Movie images become `<file>-<type>.<ext>`, a Kodi-supported form that `MovieFileImageRegex` already recognises.
  - Every other consumer (WDTV, Roksbox, MediaBrowser, Kometa) must either write a per-file name or skip folder-level files in a shared folder. It must never write one shared file for several scenes. The builder audits each consumer.
- **Organise to flat:** Whisparr-tracked folder-level metadata for the scene (`movie.nfo`, `poster.jpg`) moves with the scene and is renamed per file, or is regenerated per file and the old copy removed. Untracked files stay where they are.

### Organise (rename) to flat

- This is the existing rename flow, which already moves the video file and updates `Movie.Path`, plus three things:
  - the extras fix above;
  - after moving, the old folder is removed only if it's empty; removal follows `RemoveEmptySubfolders` on the *old* folder, not the new one. A folder that still holds untracked files is left in place and logged at Info; there's no UI message (my call);
  - the rename preview shows the folder change. **V5 only:** when the folder changes, `existingPath`/`newPath` on the rename resource are relative to the root folder instead of the scene folder. The frontend needs no change because it already renders those strings. V3 is frozen and unchanged (my call).
- **File-name collisions:** two scenes can resolve to the same file name in one flat folder. Whisparr never overwrites. The move is skipped and a warning logged, matching today's `FileAlreadyExistsException` handling in rename, and the same for imports. Naming validation **warns, but doesn't reject**, when the scene folder format is flat and the scene file format has neither `{Release Date}` nor `{Scene ForeignId}` (my call).
- Changing the format alone moves nothing; only the user's organise action does.

### Delete and move

- **Deleting a scene with "delete files" in a shared folder** recycles that scene's owned video files and owned extras, then removes empty folders. It never recycles the folder.
- **The shared-path guard in `HandleAsync(MoviesDeletedEvent)`** compares against **every item in the DB**, not just the deleted batch. This is v2's guard (`MediaFileDeletionService.HandleAsync(SeriesDeletedEvent)`, which uses `GetAllSeriesPaths`), ported as is. It applies to movies too. That's a correctness fix that keeps movie behaviour identical when no paths are shared.
- **`DeleteMovieFilesCommand`** is already per file and stays so.
- **`MoveMovieService`** (changing a scene's root folder or path, single or bulk) moves owned files and extras one by one in a shared folder, never `TransferFolder` on the shared folder. Exclusive folders keep the folder move.

### Path validation

- **`MoviePathValidator` and `MovieAncestorValidator`** let a scene share a path with another scene, and let a scene's folder contain any item's folder. That covers a studio that has both: scenes flat in `scenes/Shady Spa`, a movie in `scenes/Shady Spa/Some Movie`. A scene still may not have the same path as a movie, and a movie's folder may not equal or contain any other item's folder.
  - This applies wherever those validators run: `AddMovieValidator`, the V5 and V3 movie controllers, and `RootFolderController`.
  - Root folder ancestor rules stay as they are.
- **Naming validation (`ValidMainSceneFolderFormat`)** drops the scene-title-token requirement. It keeps the illegal-character check and the studio-token check (`ValidSceneFolderFormat`).
- **Main-folder prefix** (my call, following from goal 7): today the scene folder format must start with a fixed segment such as `scenes/`. That's dropped for the scene folder format, because a v2 layout `{root}/{Site}/…` can't otherwise be reproduced without moving files.
  - When no prefix is used, a studio folder could collide with the movie main folder (`movies/`) or the scene import folder. Naming validation rejects a scene folder format whose first segment is a literal equal to either.
  - A token-built first segment can't be checked statically; the builder decides whether to warn at runtime on a collision.

### Manual import

- **In a shared folder,** "manual import for scene X" (`ManualImportService` around line 107) lists X's owned files plus unowned files that identify as X. It doesn't list every file in the folder.
- **`FindByPath(baseFolder)`** (line 455) must not resolve a shared folder to an arbitrary scene. A shared folder yields no folder-level scene; each file identifies on its own.

### Downloads and imports

- No new mechanism. When the scene has no file, `MovieFileMovingService` already recomputes `Movie.Path` from the format, so a flat format places the download in the studio folder.
- The ownership fixes keep upgrades from touching a sibling. Tests must cover:
  - a download into a flat folder that already holds sibling scenes;
  - an upgrade in a flat folder.

### Every folder-level `movie.Path` use

About 60 files read `movie.Path`. The places that treat it as "this scene's folder" for disk scans, deletes, moves or empty-folder removal are in scope:

- `DiskScanService`
- `MediaFileDeletionService`
- `MoveMovieService`
- `RenameMovieFileService`
- `MovieFileMovingService.EnsureMovieFolder`
- `ManualImportService`
- `ExtraFileService`
- `ImportExistingExtraFilesBase`
- the existing extra/subtitle/metadata importers
- `CleanMetadataFileService`
- `IdentificationService`
- the housekeeping cleanups for extras

The builder audits the full list, which `rg -n 'movie.Path|Movie.Path' src/NzbDrone.Core` produces. Uses that only build a file path (`Path.Combine(movie.Path, file.RelativePath)`) are unaffected.

## For G (migration wizard): token map

v2 → v5 (`Organizer/FileNameBuilder.cs` in both trees):

| v2 | v5 |
|---|---|
| `{Site Title}`, `{Site CleanTitle}`, `{Site TitleSlug}`, `{Site TitleThe}`, `{Site TitleFirstCharacter}` | `{Studio Title}`, `{Studio CleanTitle}`, `{Studio TitleSlug}`, `{Studio TitleThe}`, `{Studio TitleFirstCharacter}` |
| `{Site Network}` | `{Studio Network}` |
| `{Episode Title}`, `{Episode CleanTitle}` | `{Scene Title}`, `{Scene CleanTitle}` |
| `{Episode Performers}`, `…Female`, `…Male`, `…Other` | `{Scene Performers}`, `…Female`, `…Male`, `…Other` |
| `{Release Date}`, `{Release ShortDate}` | same |
| `{Quality *}`, `{MediaInfo *}`, `{Release Group}`, `{Original Title}`, `{Original Filename}`, `{Custom Formats}` | same |
| `{TpdbSceneId}` | `{Scene ForeignId}` (holds the TPDB UUID when the scene source is TPDB; G resolves numeric to UUID, but the file name keeps the numeric ID only if G adds a token for it) |

**No direct v5 equivalent**, so G reports these:
- `{Site Year}` and the `…Year` / `…WithoutYear` variants;
- `{Episode Year}`;
- `{TpdbId}`;
- `{External Id}`.

G owns the final table. Example port: v2 `SeriesFolderFormat = "{Site Title}"` → v5 `SceneFolderFormat = "{Studio Title}"` (no prefix, per the prefix decision).

## Constraints

- Scene-only. Every behaviour change is gated on `ItemType.Scene`, except the delete-guard fix and the `MissingFromDisk` sidecar fix, which are safety fixes for all items.
- No migration may move files. Any DB migration (for example the lost-file table) is v5-only, numbered from the v5 range (≥ 300).
- Forward only: no backports to Eros.
- Tests:
  - NUnit `*Fixture`, 80% coverage on new code;
  - every test project runs (Core, Common, Api, Host, Mono);
  - a live check on the dev instance repeats the spike and must show all seven findings fixed.

## Left to the builder

- How the folder-level scan is structured (by folder, or by scene with a shared per-scan identification cache).
- The lost-file storage shape (a new table, or extra columns on an existing one), within the 30-day, oshash-plus-size, single-match rule.
- The exact flat-detection token list.
- Per-consumer metadata naming for WDTV, Roksbox, MediaBrowser and Kometa.
- Whether to warn at runtime when a token-built first segment collides with `movies/` or the import folder.
- Task order. The two catastrophic fixes (findings 5 and 7) land first, before anything makes flat reachable.
