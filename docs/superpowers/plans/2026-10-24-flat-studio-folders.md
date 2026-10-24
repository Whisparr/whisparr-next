# Flat Studio Folders Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let several scenes share one studio folder, so that a v2 flat layout works natively and a v3 user can switch to it. No Whisparr operation may ever touch a file the acting scene doesn't own.

**Architecture:**
- **One new service, `ISceneFolderLayout`,** answers two questions for every disk operation: "is the format flat?" and "is this scene's folder shared?".
- **In shared folders, a scene owns files, not the folder.** Ownership comes from `MovieFile` rows, file identification (the existing `GetSceneImportDecisions`) and sidecar-name parsing (ported from v2's existing-extra importers).
- **The two catastrophic deletion bugs are fixed first,** before anything makes flat reachable.

**Tech Stack:** C# / .NET 10, NUnit + Moq + FluentAssertions (`CoreTest<T>`, `DbTest`), FluentMigrator, FluentValidation.

**Spec:** `docs/superpowers/specs/2026-10-24-flat-studio-folders-design.md`. Read it before any task; it's the binding authority.

## Global Constraints

- **Scene-only.** Every behaviour change is gated on `ItemType.Scene`, with two exceptions that apply to all items: the delete-guard fix (Task 1) and the `MissingFromDisk` sidecar fix (Task 2).
- **No Whisparr operation deletes, recycles, moves or claims a file the acting scene doesn't own.** This includes sidecars and the studio folder.
- **No DB migration moves files.** New migrations are v5-only; the next free number is **304**.
- **Never overwrite on a file-name collision.** Skip and log a warning.
- **Forward only:** nothing is backported to Eros.
- **File edits use Edit/Write only.** No sed, awk, python or shell redirection to change files.
- **Commit straight to `develop`,** one commit per task, message prefix `New:`/`Fix:`. No AI attribution anywhere, and never push.
- **Tests:** NUnit `*Fixture` classes; 80% coverage on new code; run with the filter `TestCategory!=IntegrationTest&TestCategory!=AutomationTest&TestCategory!=ManualTest`.
- **Every task's final verification runs all five test projects:** Core, Common, Api, Host, Mono. Build with `dotnet build src/NzbDrone.Core.Test/Whisparr.Core.Test.csproj` and its siblings, never `--no-build` after a solution build.
- **Constants:**
  - lost-file lifetime: **30 days**;
  - unidentified-file cache in rescan: **10 minutes** (my call, from the spec's identify-once requirement);
  - the flat-format collision warning names `{Release Date}` and `{Scene ForeignId}`.

## Review Focus

1. **Base-name prefixes in a flat folder.** `Title.mp4` and `Title 2.mp4` sit side by side, so `Title 2.en.srt` must never attach to `Title`. Tested in Task 7.
2. **Old per-scene folders nested under a flat studio folder.** These are half-organised libraries. The studio-level scene's recursive listing sees files in sub-folders that belong to other scenes; those must stay unowned. Tested in Task 6.
3. **Deleting the last scene in a flat folder that still holds unknown files.** Only that scene's files go, and the folder stays because it isn't empty. Tested in Task 10.
4. **Two scenes whose names resolve to the same target file in one folder,** on organise and on import. The second is skipped and the first file is untouched. Tested in Task 8.
5. **A library that never adopts flat.** A per-scene folder with an oddly named single file still imports that file on rescan, as it does today. Tested in Task 6.

---

### Task 1: Delete guard compares against every item (v2's guard)

**Files:**
- Modify: `src/NzbDrone.Core/MediaFiles/MediaFileDeletionService.cs:196-233` (`HandleAsync(MoviesDeletedEvent)`)
- Test: `src/NzbDrone.Core.Test/MediaFiles/MediaFileDeletionService/MoviesDeletedFixture.cs` (create)

**Interfaces:**
- Consumes: `IMovieService.AllMoviePaths(): Dictionary<int, string>`.
- Produces: nothing new.

- [ ] **Step 1: Write the failing tests** in `MoviesDeletedFixture : CoreTest<MediaFileDeletionService>`:
  - `should_not_recycle_folder_when_another_item_not_in_batch_shares_the_path`. Movie 1 is being deleted, path `/lib/scenes/Shady Spa`. `AllMoviePaths` returns `{1: that path, 2: same path}`. Assert `IRecycleBinProvider.DeleteFolder` is never called.
  - `should_not_recycle_folder_when_it_is_parent_of_another_item_not_in_batch`. Movie 2's path is `/lib/scenes/Shady Spa/old scene`. Assert it's never called.
  - `should_recycle_folder_when_no_other_item_shares_it`. Assert it's called once with movie 1's path.
- [ ] **Step 2: Run them.** `dotnet test src/NzbDrone.Core.Test/Whisparr.Core.Test.csproj --filter "FullyQualifiedName~MoviesDeletedFixture"`. Expected: the first two FAIL.
- [ ] **Step 3: Implement.** Replace `_movieService.FindByIds(movieIds)` with `_movieService.AllMoviePaths()` and skip entries whose key is the movie being handled. This mirrors `~/Git/Whisparrv2/src/NzbDrone.Core/MediaFiles/MediaFileDeletionService.cs:91-126`.
  - Keep the per-movie `return`-on-conflict semantics, but use `continue` to the next movie in the batch instead of returning from the whole handler. Log at Error with the existing messages.
  - Movies being deleted in the same batch are still in the DB at this point, so a batch that deletes two scenes sharing a folder also skips the folder recycle. That's the safe outcome; Task 10 deletes their owned files.
- [ ] **Step 4: Run them again.** Expected: PASS.
- [ ] **Step 5: Commit.** `Fix: Never recycle a folder another item still uses when deleting with files`

### Task 2: A missing video never deletes sidecars from disk

**Files:**
- Modify: `src/NzbDrone.Core/Extras/Files/ExtraFileService.cs:103-131` (`HandleAsync(MovieFileDeletedEvent)`)
- Test: `src/NzbDrone.Core.Test/Extras/Files/ExtraFileServiceFixture.cs` (create; generic, so test through `ExtraFileService<SubtitleFile>`)

**Interfaces:** none new.

- [ ] **Step 1: Write the failing tests.**
  - `should_not_recycle_extras_when_reason_is_missing_from_disk`: reason `DeleteMediaFileReason.MissingFromDisk`, one subtitle row, `FileExists` true. Assert `DeleteFile` is never called and `DeleteForMovieFile(id)` is called.
  - `should_recycle_extras_when_reason_is_manual`: same as above, but `DeleteFile` is called once.
  - `should_recycle_extras_when_reason_is_upgrade`: same as above, but `DeleteFile` is called once.
- [ ] **Step 2: Run.** `--filter "FullyQualifiedName~ExtraFileServiceFixture"`. Expected: the first test FAILS.
- [ ] **Step 3: Implement.** Disk deletion happens only when the reason is `Manual` or `Upgrade`. Every other reason (including `MissingFromDisk` and `NoLinkedEpisodes`) removes the DB rows only. Keep the existing debug message, generalised: "not deleting extra files from disk".
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit.** `Fix: Keep sidecars on disk when their video goes missing`

### Task 3: `ISceneFolderLayout`: flat detection and shared folders

**Files:**
- Create: `src/NzbDrone.Core/Organizer/SceneFolderLayout.cs`
- Create: `src/NzbDrone.Core/Movies/MoviePathEntry.cs`
- Modify: `src/NzbDrone.Core/Movies/MovieRepository.cs` (add `AllMoviePathEntries`)
- Modify: `src/NzbDrone.Core/Movies/MovieService.cs` (pass-through)
- Test: `src/NzbDrone.Core.Test/OrganizerTests/SceneFolderLayoutFixture.cs`
- Test: `src/NzbDrone.Core.Test/MovieTests/MovieRepositoryTests/AllMoviePathEntriesFixture.cs` (a `DbTest`)

**Interfaces:**
- Produces:
  - `public class MoviePathEntry { public int Id { get; set; } public string Path { get; set; } public ItemType ItemType { get; set; } }`
  - `List<MoviePathEntry> IMovieRepository.AllMoviePathEntries()` and `IMovieService.AllMoviePathEntries()`. These join `Movies` to `MovieMetadata` for `ItemType`.
  - `public interface ISceneFolderLayout`:
    - `bool IsFlatFormat(string sceneFolderFormat)`
    - `bool IsFlat()`, which uses the current naming config
    - `bool IsShared(Movie movie)`
    - `List<MoviePathEntry> GetItemsInFolder(string folder)`: items whose `Path` equals `folder`
  - The implementation `SceneFolderLayout : ISceneFolderLayout, IHandle<MovieAddedEvent>, IHandle<MoviesImportedEvent>, IHandle<MoviesDeletedEvent>, IHandle<MovieEditedEvent>, IHandle<MovieMovedEvent>, IHandle<MovieRenamedEvent>`. Every handler clears the path cache.

- [ ] **Step 1: Write the failing tests** in `SceneFolderLayoutFixture : CoreTest<SceneFolderLayout>`.
  - `IsFlatFormat` returns **true** for:
    - `"scenes/{Studio CleanNetwork}/{Studio CleanTitle}"`
    - `"{Studio Title}"`
    - `"{Studio Network}/{Studio TitleSlug}"`
    - `"scenes/Literal"`
  - `IsFlatFormat` returns **false** for:
    - `"scenes/{Studio CleanTitle}/{Release Date} - {Scene CleanTitle} {[Scene ForeignId]}"`
    - `"{Studio Title}/{Scene Title}"`
    - `"{Studio Title}/{StashId}"`
    - `"{Studio Title}/{Release Date}"`
  - `is_shared_false_for_movies`: a movie item, even when the format is flat.
  - `is_shared_true_for_scene_when_format_is_flat`
  - `is_shared_true_when_another_item_has_same_path`: per-scene format.
  - `is_shared_true_when_scene_path_is_ancestor_of_another_item`
  - `is_shared_false_for_scene_alone_in_per_scene_folder`
  - `path_cache_is_cleared_on_movie_edited_event`: `AllMoviePathEntries` is called twice across an event.
  - `AllMoviePathEntriesFixture.should_return_item_type_per_movie`: one scene and one movie are inserted with `MovieMetadata` rows.
- [ ] **Step 2: Run.** `--filter "FullyQualifiedName~SceneFolderLayoutFixture|FullyQualifiedName~AllMoviePathEntriesFixture"`. Expected: FAIL (types don't exist).
- [ ] **Step 3: Implement.**
  - **Flat rule:** split on `/` and `\`, take the last non-empty segment, and collect its tokens with `\{[^}]+\}`. The format is flat when there are no tokens, or every token's name (after `{`, any leading `[`/`(` inside the braces is ignored) starts with `Studio`.
  - **Cache:** `AllMoviePathEntries` sits in an `ICached<List<MoviePathEntry>>` (via `ICacheManager`, key `"all"`, 5-minute lifetime), cleared by every handler.
  - **Path comparisons** use `PathEquals` / `IsParentPath`.
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit.** `New: Detect flat scene folder formats and shared scene folders`

### Task 4: Naming validation accepts flat formats

**Files:**
- Modify: `src/NzbDrone.Core/Organizer/FileNameValidation.cs:45-54` (`ValidMainSceneFolderFormat`)
- Modify: `src/Whisparr.Api.V5/Config/NamingConfigController.cs:28-36`, and the same rules in `src/Whisparr.Api.V3/Config/NamingConfigController.cs`
- Test: `src/NzbDrone.Core.Test/OrganizerTests/FileNameValidationFixture.cs` (create, or add to an existing fixture if one exists for these rules)
- Test: `src/NzbDrone.Api.Test/` naming controller validation fixture, following the neighbours

**Interfaces:**
- Consumes: `ISceneFolderLayout.IsFlatFormat(string)`.
- Produces: `IRuleBuilderOptions<T, string> NotCollidingWithMainFolders<T>(this IRuleBuilder<T, string> ruleBuilder, Func<T, string> movieFolderFormat, Func<T, string> sceneImportFolderFormat)` in `FileNameValidation.cs`.

- [ ] **Step 1: Write the failing tests.**
  - Valid:
    - `"scenes/{Studio CleanNetwork}/{Studio CleanTitle}"`
    - `"{Studio Title}"`, with no main-folder prefix
    - the current default per-scene format
  - Invalid: `"movies/{Studio Title}"` when the movie folder format's first segment is `movies`. The message is "Must not start with the movie or scene import folder".
  - Invalid: `"import/{Studio Title}"` when the scene import folder format's first segment is `import`.
  - Still invalid: `"scenes/{Release Date}"`, which has no studio token (existing `ValidSceneFolderFormat`).
  - Warning (severity `Warning`, not an error): a flat scene folder format plus `StandardSceneFormat = "{Scene Title}"`. The message is "Flat scene folders need {Release Date} or {Scene ForeignId} in the scene file format to keep file names unique".
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
  - In `ValidMainSceneFolderFormat`, drop both the `MainFolderRegex` match and the `SceneTitleTokenRegex` predicate. Keep `NotEmpty` and the illegal-character check.
  - Add `NotCollidingWithMainFolders`. It compares the first segment, when that segment is a literal with no `{`, case-insensitively against the first segment of `MovieFolderFormat` and of `SceneImportFolderFormat`.
  - Add the warning rule with the repo's `.AsWarning()` (`src/NzbDrone.Core/Validation/RuleBuilderExtensions.cs`). It fires when `IsFlatFormat(SceneFolderFormat)` holds and `StandardSceneFormat` contains neither `{Release Date` nor `{Scene ForeignId`.
  - Wire both rules in V5 and V3 `NamingConfigController`.
- [ ] **Step 4: Run** the Core and Api test projects. Expected: PASS.
- [ ] **Step 5: Commit.** `New: Accept flat scene folder formats in naming settings`

### Task 5: Path validators let scenes share a folder with scenes

**Files:**
- Modify: `src/NzbDrone.Core/Validation/Paths/MoviePathValidation.cs` (`MoviePathValidator<T>`)
- Modify: `src/NzbDrone.Core/Validation/Paths/MovieAncestorValidator.cs`
- Test: `src/NzbDrone.Core.Test/ValidationTests/MoviePathValidatorFixture.cs` and `MovieAncestorValidatorFixture.cs` (create)

**Interfaces:**
- Consumes: `IMovieService.AllMoviePathEntries()`.
- The validated instance can be a `Movie` (`AddMovieValidator`) or a `MovieResource` (V3/V5). Read the item type from the instance:
  - `Movie.MovieMetadata.Value.ItemType` for a `Movie`;
  - `MovieResource.ItemType` for a resource. Check the V5/V3 resource for the property name.

  The `dynamic instance` pattern already in `MoviePathValidator` is acceptable.

- [ ] **Step 1: Write the failing tests.**
  - `scene_may_share_path_with_another_scene`: valid.
  - `scene_may_not_share_path_with_a_movie`: invalid.
  - `movie_may_not_share_path_with_a_scene`: invalid.
  - `movie_may_not_share_path_with_a_movie`: invalid (unchanged).
  - Ancestor:
    - a scene path that's an ancestor of another scene's path is valid;
    - a scene path that's an ancestor of a movie's path is valid. This is the studio that has both: scenes flat in `scenes/Shady Spa`, a movie in `scenes/Shady Spa/Some Movie`;
    - a movie path that's an ancestor of anything is invalid.
  - `scene_added_after_movie_nested_in_studio_folder_is_valid`: entries hold a movie at `/lib/scenes/Shady Spa/Some Movie`, and the scene's path is `/lib/scenes/Shady Spa`.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
  - **Equal paths** conflict unless both items are scenes.
  - **Ancestor** conflicts only when the instance is a movie. A scene may contain any item's folder.
- [ ] **Step 4: Run the Core and Api tests.** Expected: PASS.
- [ ] **Step 5: Commit.** `New: Allow scenes to share or nest folders with other scenes`

### Task 6: Rescan imports only owned files in shared folders

**Files:**
- Modify: `src/NzbDrone.Core/MediaFiles/DiskScanService.cs` (`Scan(Movie, bool)` lines 90-254; `RelinkRenamedFiles` lines 319-378)
- Create: `src/NzbDrone.Core/MediaFiles/SharedFolderScanner.cs`
- Test: `src/NzbDrone.Core.Test/MediaFiles/DiskScanServiceTests/SharedFolderScanFixture.cs`
- Test: `src/NzbDrone.Core.Test/MediaFiles/SharedFolderScannerFixture.cs`

**Interfaces:**
- Consumes:
  - `ISceneFolderLayout.IsShared(Movie)` and `GetItemsInFolder(string)` (Task 3);
  - `IImportDecisionMaker.GetSceneImportDecisions(List<string>)`, which returns `(List<Movie> MoviesToAdd, List<MovieFile> UnmappedFiles, List<ImportDecision> Decisions)`;
  - `IImportApprovedMovie.Import(...)`.
- Produces:
  - `public interface ISharedFolderScanner { List<ImportDecision> IdentifyOwned(Movie movie, List<string> unmappedVideoFiles); }`
  - It returns approved and rejected decisions **only** for files that identify as an in-library scene whose `Path` equals `movie.Path`.
  - Decisions for sibling scenes are included, so a single scan imports for the whole folder.
  - Files that identify as no scene, as a scene outside the folder, or as a scene not in the library are dropped. `MoviesToAdd` and `UnmappedFiles` are always ignored: no auto-add and no unmapped rows.
  - Paths already checked and unidentified are cached for **10 minutes**, keyed by path + size + last-write, and not re-identified.

- [ ] **Step 1: Write the failing tests.**
  - `SharedFolderScanFixture : CoreTest<DiskScanService>`, with the shared folder `/lib/scenes/Shady Spa` holding S1 and S2:
    - `rescan_of_s1_does_not_import_s2_file_as_s1`. Files on disk are `s1.mp4` and `s2.mp4`. The scanner identifies `s2.mp4` → S2 and `s1.mp4` → S1. Assert every `ImportDecision` passed to `Import` has `LocalMovie.Movie.Id` equal to the scene that file identified as, and that no decision attaches `s2.mp4` to S1.
    - `rescan_of_s1_leaves_unidentified_file_alone`: an `unknown.mp4` gives no decision and no `MovieFile`.
    - `rescan_of_studio_scene_ignores_files_in_nested_old_scene_folder_owned_by_other_scene` (Review Focus 2): `/lib/scenes/Shady Spa/old S3 folder/s3.mp4` identifies as S3, whose path is that sub-folder. No decision is made for it from S1's scan.
    - `rescan_of_flat_scene_ignores_movie_files_in_nested_movie_folder`: the movie at `/lib/scenes/Shady Spa/Some Movie` has a tracked `movie.mp4` and an untracked `movie-extra.mp4`. S1's scan makes no decision for either file.
    - `exclusive_folder_still_imports_any_file_as_the_scene` (Review Focus 5): per-scene folder, `IsShared` false, an odd name `a8f3c0de.mp4`. Assert the existing `GetImportDecisions(files, movie, false)` path is used, unchanged.
    - `unmapped_cleanup_only_removes_rows_for_owned_files`. An unmapped `MovieFile` row with `OriginalFilePath` `/lib/scenes/Shady Spa/s2.mp4` exists while scanning S1, and S1 doesn't own that file. Assert it isn't deleted.
    - `relink_in_shared_folder_only_considers_unowned_files`. S1's file is missing, and S2's tracked file happens to share the oshash (synthetic). Assert S1 isn't re-pointed to S2's file.
  - `SharedFolderScannerFixture`:
    - `drops_movies_to_add_and_unmapped_files`
    - `drops_decisions_for_scenes_outside_the_folder`
    - `caches_unidentified_files_for_ten_minutes`: a second call within the window doesn't call `GetSceneImportDecisions` for that path.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
  - In `Scan`, after `RelinkRenamedFiles`/`CleanMediaFiles`, branch on `_sceneFolderLayout.IsShared(movie)`:
    - **shared** → `decisions = _sharedFolderScanner.IdentifyOwned(movie, unmappedFiles)`;
    - **exclusive** → today's call.
  - In the shared branch, the existing-extras step (`CompletedScanning(movie, possibleExtraFiles)`) is unchanged here; Task 7 makes the importers ownership-aware.
  - Fix the unmapped cleanup: replace `OriginalFilePath.Contains(movie.Path)` with an exact path match against files that S1's decisions imported.
  - Restrict `RelinkRenamedFiles`' `newFiles` in a shared folder to files no `MovieFile` row of any item in the folder points at. `GetItemsInFolder` plus `GetFilesByMovie` per item is acceptable.
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit.** `Fix: Rescan only imports files a scene owns in shared folders`

### Task 7: Sidecar ownership (v2's name parsing, plus the boundary rule)

**Files:**
- Create: `src/NzbDrone.Core/Extras/SidecarOwnership.cs`
- Modify:
  - `src/NzbDrone.Core/Extras/Subtitles/ExistingSubtitleImporter.cs`
  - `src/NzbDrone.Core/Extras/Others/ExistingOtherExtraImporter.cs`
  - `src/NzbDrone.Core/Extras/Metadata/ExistingMetadataImporter.cs`
  - `src/NzbDrone.Core/Extras/Others/OtherExtraService.cs:111`
  - `src/NzbDrone.Core/Extras/Subtitles/SubtitleService.cs:120`
- Test: `src/NzbDrone.Core.Test/Extras/SidecarOwnershipFixture.cs`, plus a case in each of `SubtitleServiceFixture` and `OtherExtraServiceFixture`

**Interfaces:**
- Consumes: `ISceneFolderLayout.IsShared(Movie)`, `IParsingService.ParseMinimalPathMovieInfo(string)`, `IMovieService.FindScene(ParsedMovieInfo, ...)`.
- Produces:
  - `public static bool SidecarNameMatches(string sidecarFileName, string videoBaseName)`. True when the sidecar name (without its directory) starts with `videoBaseName`, case-insensitive, and the next character is `.`, `-` or the end of the name.
  - `public interface ISidecarOwnership { MovieFile FindOwner(Movie movie, string sidecarPath, List<MovieFile> folderFiles); }`. In a shared folder it returns the owning file of `movie`, or null:
    1. **Parse the sidecar's name** (v2's way): `ParseMinimalPathMovieInfo` → `FindScene`. If that's a scene other than `movie`, return null. If it's `movie`, return `movie`'s file.
    2. **Otherwise use the boundary match** against `folderFiles` (every tracked video file in the folder). The longest matching base name wins. A tie returns null, and so does a winner that isn't `movie`'s file.

- [ ] **Step 1: Write the failing tests.**
  - `SidecarNameMatches`:
    - true for `("Title.en.srt", "Title")`, `("Title-poster.jpg", "Title")`, `("Title.nfo", "Title")`;
    - false for `("Title 2.nfo", "Title")` and `("Titled.nfo", "Title")`.
  - `FindOwner`:
    - `longest_base_name_wins` (Review Focus 1): files `Title.mp4` (S1) and `Title 2.mp4` (S2). `Title 2.en.srt` → S2's file. Asking for S1 returns null.
    - `tie_is_unowned`
    - `parsed_scene_overrides_name_match`
    - `generic_names_are_unowned`: `movie.nfo` and `poster.jpg` return null when the folder is shared.
  - Importers, in a shared folder:
    - `ExistingSubtitleImporter` doesn't create rows for a sibling's `.srt`, and sets `MovieFileId` to the owning file's ID, not `movie.MovieFileId`.
    - The same for `ExistingOtherExtraImporter` and `ExistingMetadataImporter`.
    - In an exclusive folder, their behaviour is unchanged (existing tests stay green).
  - `OtherExtraService`/`SubtitleService.ImportFiles` don't pick up `Title 2.en.srt` when importing `Title.mp4`, in every layout.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.** In each existing importer's loop, when `IsShared(movie)` holds, call `FindOwner` and skip on null. Replace the plain `StartsWith` in both `ImportFiles` methods with `SidecarNameMatches`.
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit.** `Fix: Attach sidecars only to the scene that owns them`

### Task 8: Organise into flat: move sidecars, clean up old folders, preview and collisions

**Files:**
- Modify: `src/NzbDrone.Core/Extras/Files/ExtraFileManager.cs:94-130` (`MoveFile`)
- Modify: the `MoveFilesAfterRename` signature in `ExtraFileManager`, `SubtitleService`, `OtherExtraService`, `MetadataService` and `ExtraService` (the dispatcher handling `MovieRenamedEvent`)
- Modify: `src/NzbDrone.Core/MediaFiles/RenameMovieFileService.cs:137-220`
- Modify: `src/Whisparr.Api.V5/Movies/RenameMovieController.cs` (V5 only)
- Test: `RenameMovieFileServiceFixture`, `SubtitleServiceFixture`, `OtherExtraServiceFixture`, `MoveMovieFileFixture`

**Interfaces:**
- Changed signature: `IEnumerable<ExtraFile> MoveFilesAfterRename(Movie movie, List<RenamedMovieFile> renamedFiles)` replaces `(Movie movie, List<MovieFile> movieFiles)`. `RenamedMovieFile.PreviousPath` gives the old full path.
- The old location of an extra is `Path.Combine(Path.GetDirectoryName(previousVideoPath), Path.GetFileName(extra.RelativePath))` when the extra sat next to the video. Otherwise it's the extra's relative path resolved against the old scene folder, which is derived as `previousVideoPath` minus `PreviousRelativePath`.

- [ ] **Step 1: Write the failing tests.**
  - `subtitle_moves_with_video_when_rename_changes_folder` (finding 4). The previous video path is `/lib/scenes/Shady Spa/2020 - Buffy [id]/s1.mp4`; the new movie path is `/lib/scenes/Shady Spa`; the subtitle's relative path is `s1.en.srt`. Assert `MoveFile` goes from the old folder to `/lib/scenes/Shady Spa/<new video base>.en.srt`.
  - `rename_removes_old_folder_when_empty`: `RemoveEmptySubfolders` (or `DeleteFolder` when empty) is called on the *old* folder.
  - `rename_leaves_old_folder_with_untracked_files`: `FolderEmpty` is false, nothing is deleted, and an Info line is logged.
  - `preview_paths_are_root_relative_when_folder_changes` (V5): `ExistingPath` = `scenes/Shady Spa/2020 - Buffy [id]/s1.mp4`, `NewPath` = `scenes/Shady Spa/<new name>.mp4`.
  - `preview_paths_stay_movie_relative_when_folder_unchanged`
  - `rename_skips_when_destination_exists_and_keeps_both_files` (Review Focus 4): `FileAlreadyExistsException` gives no transfer, a warning, and leaves the movie file row unchanged.
  - `import_into_flat_folder_with_siblings_places_file_in_studio_folder` (`MoveMovieFileFixture`): a scene with no file under a flat format. The destination is `/lib/scenes/Shady Spa/<name>.mp4`, and no sibling path is touched.
  - `import_collision_is_rejected_not_overwritten`: `DestinationAlreadyExistsException` gives an `ImportResult` rejection, and `TransferFile` is never called with `overwrite: true`.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
  - **Extras move:** thread the renamed files through to `MoveFile(movie, movieFile, extraFile, previousVideoPath, suffix)`.
  - **Old-folder cleanup:** in `RenameFiles`, remember each file's previous folder. After the loop, call `RemoveEmptySubfolders` on each distinct previous folder that's no longer `movie.Path`, then delete it if `FolderEmpty`; otherwise log Info "Left {0} in place, it still holds files".
  - **Preview:** in `GetPreviews`, when the old and new directories differ, set both paths relative to `_rootFolderService.GetBestRootFolderPath(...)`.
  - V3 isn't touched.
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit.** `Fix: Move sidecars and clean old folders when a rename changes the scene folder`

### Task 9: Per-file metadata names in shared folders (movie-style)

**Files:**
- Modify: `src/NzbDrone.Core/Extras/Metadata/Consumers/Xbmc/XbmcMetadata.cs`:
  - `GetMovieMetadataFilename` (line 431);
  - `ProcessMovieImages` (line 420);
  - `FindMetadataFile` (lines 50-120), which recognises `<file>-poster.jpg` via `MovieFileImageRegex`.
- Modify: `Wdtv`, `Roksbox`, `MediaBrowser`, `Kometa` consumers in `src/NzbDrone.Core/Extras/Metadata/Consumers/`
- Modify: `src/NzbDrone.Core/Extras/Metadata/MetadataService.cs`, so organise renames tracked folder-level metadata per file (lines 160-185)
- Test: `src/NzbDrone.Core.Test/Extras/Metadata/Consumers/Xbmc/MovieMetadataFixture.cs`, `FindMetadataFileFixture.cs`, plus one fixture per touched consumer

**Interfaces:**
- Consumes: `ISceneFolderLayout.IsShared(Movie)`. It's injected into each consumer's constructor; `MetadataBase` subclasses take dependencies via their constructors.
- `MovieImages(Movie movie)` has no file context today. **Change `List<ImageFileResult> MovieImages(Movie movie)` to `List<ImageFileResult> MovieImages(Movie movie, MovieFile movieFile)`** on `IMetadata`/`MetadataBase` and every consumer. `MetadataService` already has the file in hand where it calls it.

- [ ] **Step 1: Write the failing tests.**
  - Xbmc in a shared folder, with `UseMovieNfo = true`: the metadata file is `<file>.nfo`.
  - Xbmc in a shared folder: images are `<file>-poster.jpg` and `<file>-fanart.jpg`, and the nfo root element is `<movie>`.
  - Xbmc in an exclusive folder: unchanged (`movie.nfo` with `UseMovieNfo`, and `poster.jpg`).
  - `FindMetadataFile` recognises `<file>-poster.jpg` as `MovieImage` for that file.
  - Each other consumer, in a shared folder: no result whose relative path lacks the video base name. Either a per-file name or no result.
  - `MetadataService` on organise: a tracked `movie.nfo` in the old per-scene folder ends up as `<file>.nfo` beside the video, and the old copy is gone.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.** For each non-Kodi consumer, choose per-file naming where its format allows it; otherwise return no folder-level result in a shared folder. Record the per-consumer choice as a one-line comment at the change.
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit.** `New: Write per-file metadata names for scenes in shared folders`

### Task 10: Delete and move act on owned files in shared folders

**Files:**
- Modify: `src/NzbDrone.Core/MediaFiles/MediaFileDeletionService.cs` (`HandleAsync(MoviesDeletedEvent)`, after Task 1)
- Modify: `src/NzbDrone.Core/Movies/MoveMovieService.cs:38-114`
- Test: `MoviesDeletedFixture` (from Task 1), `src/NzbDrone.Core.Test/MovieTests/MoveMovieServiceFixture.cs`

**Interfaces:**
- Consumes: `ISceneFolderLayout.IsShared(Movie)`; `IMediaFileService.GetFilesByMovie(int)`; `IExtraFileService<SubtitleFile|OtherExtraFile|MetadataFile>.GetFilesByMovie(int)`.
- `MoviesDeletedEvent` handlers run after the rows are deleted, so the `MovieFile`/extra rows may already be gone. Capture the scene's owned paths in `MovieService.DeleteMovie(s)` **before** deletion, as a new property `MoviesDeletedEvent.OwnedFilePaths : Dictionary<int, List<string>>`. That's populated only for scenes in shared folders and holds full paths of video files plus extras.

- [ ] **Step 1: Write the failing tests.**
  - `deleting_scene_in_shared_folder_recycles_only_its_owned_files`: `DeleteFile` once for each owned path; `DeleteFolder` never; siblings' paths are never passed.
  - `deleting_last_scene_in_flat_folder_with_unknown_files_keeps_folder` (Review Focus 3): the owned files are recycled, `FolderEmpty` is false, and the folder isn't deleted.
  - `deleting_last_scene_in_flat_folder_removes_folder_when_empty`
  - `deleting_scene_keeps_nested_movie_folder`: the movie sits at `/lib/scenes/Shady Spa/Some Movie`. Only the scene's files are recycled, and the movie folder is never recycled or deleted.
  - `moving_scene_in_shared_folder_moves_owned_files_one_by_one`: `TransferFile` for each owned file and extra; `TransferFolder` never; `movie.Path` becomes the destination.
  - `moving_scene_in_exclusive_folder_still_moves_folder`: unchanged.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.** In a shared folder, after recycling, call `_diskProvider.RemoveEmptySubfolders(movie.Path)` and delete the folder only if `FolderEmpty`.
- [ ] **Step 3b: Audit the remaining folder-level `movie.Path` uses** the spec names, and gate any folder-as-scene behaviour on `IsShared`:
  - `CleanMetadataFileService`
  - `IdentificationService`, around lines 154-233
  - `MovieFileMovingService.EnsureMovieFolder`
  - `CleanupExtraFilesInExcludedFolders`
  - the other housekeeping extras cleanups

  The full list comes from `rg -n 'movie.Path|Movie.Path' src/NzbDrone.Core`. Uses that only build `Path.Combine(movie.Path, file.RelativePath)` are fine. For each change, add one test to that class's fixture: in a shared folder, a sibling's file is untouched. Write the audited list, with "changed" or "fine" for each entry, into the task report.
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit.** `Fix: Delete and move only a scene's own files when its folder is shared`

### Task 11: Manual import doesn't treat a shared folder as one scene

**Files:**
- Modify: `src/NzbDrone.Core/MediaFiles/MovieImport/Manual/ManualImportService.cs:105-135` (`GetMediaFiles(int movieId)`) and around line 455 (`FindByPath(baseFolder)`)
- Test: `src/NzbDrone.Core.Test/MediaFiles/MovieImport/Manual/ManualImportServiceFixture.cs`

**Interfaces:** consumes `ISceneFolderLayout.IsShared(Movie)`, `ISceneFolderLayout.GetItemsInFolder(string)` and `ISharedFolderScanner.IdentifyOwned(Movie, List<string>)` (Task 6).

- [ ] **Step 1: Write the failing tests.**
  - `manual_import_for_scene_in_shared_folder_lists_owned_and_identified_files_only`: the sibling's file and an unknown file aren't listed.
  - `shared_folder_does_not_resolve_to_a_scene_by_path`: `FindByPath` isn't used when `GetItemsInFolder(baseFolder).Count > 1`, or when the format is flat. Each file then identifies on its own.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit.** `Fix: Manual import lists only a scene's own files in shared folders`

### Task 12: Re-link files StashApp moved to another folder (lost files)

**Files:**
- Create: `src/NzbDrone.Core/Datastore/Migration/304_add_lost_movie_files.cs`
- Create: `src/NzbDrone.Core/MediaFiles/LostFiles/LostMovieFile.cs`, `LostMovieFileRepository.cs`, `LostMovieFileService.cs`
- Create: `src/NzbDrone.Core/Housekeeping/Housekeepers/CleanupExpiredLostMovieFiles.cs`
- Modify: `src/NzbDrone.Core/Datastore/TableMapping.cs`
- Modify: `src/NzbDrone.Core/MediaFiles/DiskScanService.cs` (record on missing; check unowned files)
- Test:
  - `src/NzbDrone.Core.Test/MediaFiles/LostFiles/LostMovieFileServiceFixture.cs`
  - `src/NzbDrone.Core.Test/Datastore/Migration/304_add_lost_movie_filesFixture.cs`
  - `src/NzbDrone.Core.Test/Housekeeping/Housekeepers/CleanupExpiredLostMovieFilesFixture.cs`

**Interfaces:**
- Table `LostMovieFiles`:
  - `Id`
  - `MovieId` int, indexed
  - `OsHash` string, indexed
  - `Size` long
  - `RelativePath` string
  - `LostAt` DateTime
- Produces `public interface ILostMovieFileService`:
  - `void Record(Movie movie, MovieFile file)`. Skip when `file.OsHash` is null.
  - `MovieFile TryRelink(string path, string osHash, long size)`. It finds exactly one entry with that `OsHash` and `Size`, whose `LostAt` is within **30 days** and whose scene has no file. It re-creates the `MovieFile` for that movie, in place, without moving anything, sets `movie.Path` to the file's folder, and deletes the entry. Otherwise it returns null.
  - It handles `MoviesDeletedEvent` (delete entries for those movies) and `MovieFileImportedEvent` (delete entries for that movie).
- `CleanupExpiredLostMovieFiles : IHousekeepingTask` deletes entries with `LostAt` older than 30 days.

- [ ] **Step 1: Write the failing tests.**
  - `record_skips_files_without_oshash`
  - `relinks_single_match_in_place_and_sets_movie_path_to_file_folder`: no transfer call, and `movie.Path` equals the file's directory.
  - `does_not_relink_when_two_entries_share_hash_and_size`
  - `does_not_relink_after_thirty_days`
  - `does_not_relink_when_scene_already_has_a_file`
  - `entries_removed_when_movie_deleted` and `entries_removed_when_movie_gets_a_file`
  - Migration fixture: the table and indexes exist.
  - Housekeeper: an entry older than 30 days is removed, and a 29-day-old one is kept.
  - DiskScan:
    - `missing_owned_file_is_recorded_when_no_in_folder_relink`
    - `unowned_unidentified_file_matching_lost_entry_is_relinked_during_another_scenes_scan`
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
  - In `DiskScanService`, before `CleanMediaFiles` deletes rows for missing files, call `Record` for each missing row that wasn't re-linked.
  - After shared-folder identification (Task 6), and for unknown files in exclusive folders too, compute the oshash of files left unowned and unidentified. Use the existing `_videoHashService.ComputeOsHashStamp`, then call `TryRelink`.
  - Hashing happens only for files that reach this point, which the Task 6 cache already throttles.
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit.** `New: Re-link scene files moved to another library folder by oshash`

### Task 13: Live check (controller-run, not a subagent)

- [ ] **Step 1:** Publish both the Console and Mono projects: `dotnet publish src/NzbDrone.Console/Whisparr.Console.csproj -c Debug -f net10.0 -r osx-arm64 --self-contained -o _output/net10.0/osx-arm64/publish`, then the same for `src/NzbDrone.Mono/Whisparr.Mono.csproj`.
- [ ] **Step 2:** Re-run the spike script `f_spike.py` (scratchpad) with two changes:
  - set the flat format through the API instead of the DB;
  - add steps that delete a scene with files in a shared folder, rename a sidecar's video, and move a file to another scene's folder, then rescan that scene.
- [ ] **Step 3:** **Expected:** all seven spec findings are fixed.
  1. The flat format saves through the API.
  2. S4 adds into the shared folder.
  3. Each scene owns only its own file and subtitle.
  4. The subtitles move with the organise, and the old folders are removed.
  5. No sidecar is deleted after a move.
  6. The moved file re-links when the other folder is scanned.
  7. Deleting S2 leaves S1's files.

  Record the output path in the ledger.
