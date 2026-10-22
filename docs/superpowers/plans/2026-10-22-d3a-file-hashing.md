# D3a: File hashing in whisparr-next — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every scene file Whisparr knows about gets an oshash (inline on import, backfilled for existing files) and, in the background, at most one phash attempt, with no effect on imports when hashing fails.

**Architecture:** A thin `IFingerprintCalculator` wraps the `Whisparr.Fingerprints` package; `VideoHashService` calls it and swallows every failure. Import, root-folder refresh and rescan compute oshash inline; two Low-priority scheduled tasks backfill oshash and generate phash in bounded runs. Matching (Skyhook lookups, tiers, review, re-link) is D3b and is not in this plan.

**Tech Stack:** C# .NET 10, FluentMigrator, Dapper, NUnit/Moq/FluentAssertions, React/TS (one settings toggle).

**Spec:** `docs/superpowers/specs/2026-10-22-fingerprints-design.md` (Part 3: Package, Columns, `VideoHashService`, When, Health check, Changed files, Settings). D3b covers the rest of Part 3.

## Global Constraints

- Repo `~/Git/whisparr-next`, branch `develop`. Commit straight to develop; do not push.
- Package: `Whisparr.Fingerprints` version `0.1.0-alpha.1` from `https://nuget.whisparr.com/index.json`, with package source mapping so `Whisparr.*` only ever comes from that feed. Building needs no secrets.
- API: `OsHash.Compute(path)` → 16 zero-padded lowercase hex; `PerceptualHash.ComputeAsync(path, durationSeconds, options?, ct)` → lowercase hex, unpadded; `FFmpegLocator.IsBundledAvailable`.
- phash duration: the container `format.duration` in seconds, rounded to 0.01 (`Math.Round(d, 2, MidpointRounding.AwayFromZero)`).
- Migration number **302**.
- A hashing problem (no ffmpeg, unreadable file, timeout) never rejects, aborts or delays an import. `VideoHashService` never throws.
- phash runs at most once per unchanged file: only when `PHash` and `PHashAttemptedAt` are both null; a failure still sets `PHashAttemptedAt`.
- A size or modified-time change on rescan clears `OsHash`, `PHash`, `PHashAttemptedAt`. A null `OsHash` is retried on the next scan.
- phash setting `EnablePerceptualHashing`, default **true**, in Media Management. The ffmpeg health check only fires when it is on.
- New user-facing strings go in `src/NzbDrone.Core/Localization/Core/en.json` only (alphabetical).
- Api.V3 is frozen: new fields go on Api.V5 resources only.
- File edits with Edit/Write only — no sed, awk or shell redirection. One plain Bash command per call, absolute paths, no `cd &&`, no `..`. No AI attribution in commits. Commit prefixes `New:` / `Fix:` / `Chore:`.
- Implementers never dispatch subagents.

## Review Focus

1. **A file under 64 KiB, a zero-byte file, or a file deleted between listing and hashing** — oshash comes back null, the import or scan carries on. (Task 3 tests.)
2. **Bundled ffmpeg missing on this RID** (e.g. linux-musl-arm falling back to a glibc build that won't start) — phash task does nothing and doesn't mark files attempted; health check warns. (Tasks 1, 6, 7.)
3. **A file replaced in place with the same size** (re-encode or re-download that happens to match size) — the modified-time check still clears both hashes. (Task 4 test.)
4. **Huge library on first start after upgrade** (tens of thousands of files, NAS) — the backfill runs in bounded batches and never holds a command thread for long. (Task 5 test on the batch limit.)
5. **phash hangs on a corrupt file** — a per-file timeout ends it, `PHashAttemptedAt` is set, the run moves on. (Task 3/6 tests.)

---

### Task 1: Package, feed and calculator wrapper

**Files:**
- Modify: `src/NuGet.config` (add the feed and `packageSourceMapping`)
- Modify: `src/NzbDrone.Core/Whisparr.Core.csproj` (PackageReference)
- Create: `src/NzbDrone.Core/MediaFiles/Fingerprints/FingerprintCalculator.cs`
- Test: `src/NzbDrone.Core.Test/MediaFiles/Fingerprints/FingerprintCalculatorFixture.cs`

**Interfaces:**
- Produces:
  ```csharp
  public interface IFingerprintCalculator
  {
      bool FFmpegAvailable { get; }                  // FFmpegLocator.IsBundledAvailable
      string OsHash(string path);                    // OsHash.Compute; may throw
      Task<string> PHashAsync(string path, double durationSeconds, CancellationToken cancellationToken); // may throw
  }
  public class FingerprintCalculator : IFingerprintCalculator
  ```
  Namespace `NzbDrone.Core.MediaFiles.Fingerprints`. No logic beyond delegating; exceptions pass through (Task 3 swallows them).

- [ ] **Step 1:** `src/NuGet.config`: add `<add key="whisparr" value="https://nuget.whisparr.com/index.json" />` and a `<packageSourceMapping>` that maps `Whisparr.*` to `whisparr` and `*` to every existing source (nuget.org and both Servarr feeds keep serving what they serve today). Mapping must cover every existing package, or restore fails — run Step 3 to prove it.
- [ ] **Step 2:** `Whisparr.Core.csproj`: `<PackageReference Include="Whisparr.Fingerprints" Version="0.1.0-alpha.1" />` next to NetVips.
- [ ] **Step 3:** Run `dotnet restore ~/Git/whisparr-next/src/Whisparr.sln` with no credentials configured. Expected: succeeds.
- [ ] **Step 4: Failing test** `FingerprintCalculatorFixture`:
  - `OsHash_returns_16_lowercase_hex_for_a_file_over_128KiB`: write 256 KiB of deterministic bytes to a temp file; result matches `^[0-9a-f]{16}$` and equals `Whisparr.Fingerprints.OsHash.Compute` on the same file.
  - `FFmpegAvailable_matches_locator`: equals `FFmpegLocator.IsBundledAvailable`.
- [ ] **Step 5:** Implement; run `dotnet test src/NzbDrone.Core.Test/Whisparr.Core.Test.csproj --filter "FullyQualifiedName~FingerprintCalculatorFixture"`. Expected: PASS.
- [ ] **Step 6: Publish check.** Run `dotnet msbuild -restore ~/Git/whisparr-next/src/Whisparr.sln -p:Configuration=Release -p:Platform=Posix -p:RuntimeIdentifiers=linux-musl-arm -t:PublishAllRids`, then the same for `linux-x64` and `linux-musl-x64`. In each `_output/net10.0/<rid>/` the `ffmpeg` binary exists, and `file` on it reports **statically linked** (musl RIDs fall back to the linux glibc builds; a dynamically linked one won't start on Alpine). Record the three `file` lines in the report. If any is dynamic, stop and report BLOCKED — do not work around it.
- [ ] **Step 7: Commit** — `New: Reference the Whisparr.Fingerprints package from the Whisparr NuGet feed`.

### Task 2: Migration 302 and model fields

**Files:**
- Create: `src/NzbDrone.Core/Datastore/Migration/302_add_file_fingerprints.cs`
- Create: `src/NzbDrone.Core/MediaFiles/MatchTier.cs`
- Modify: `src/NzbDrone.Core/MediaFiles/MovieFile.cs`, `src/NzbDrone.Core/RootFolders/ImportFile.cs`
- Modify: `src/Whisparr.Api.V5/MovieFiles/MovieFileResource.cs` (fields + both mappers)
- Test: `src/NzbDrone.Core.Test/Datastore/Migration/302_add_file_fingerprintsFixture.cs`

**Interfaces:**
- Produces:
  - `MovieFile`: `string OsHash`, `string PHash`, `DateTime? PHashAttemptedAt`, `DateTime? HashedFileModifiedAt`, `MatchTier MatchTier`.
  - `ImportFile`: `string OsHash`, `string PHash`, `DateTime? PHashAttemptedAt`, `DateTime? HashedFileModifiedAt`.
  - `public enum MatchTier { Unknown = 0, Name = 1, Confirmed = 2, Hash = 3, Manual = 4 }` in `NzbDrone.Core.MediaFiles`. D3b sets it; D3a only stores the default.
  - `MovieFileResource` (V5): `OsHash`, `PHash`, `MatchTier`.

- [ ] **Step 1: Failing migration test** (follow an existing `MigrationTest<T>` fixture in that folder): insert a `MovieFiles` row and an `ImportFiles` row at 301, migrate to 302, assert the new columns exist and read back null / `MatchTier` = 0, and that an index on `MovieFiles.OsHash` exists (named `IX_MovieFiles_OsHash`).
- [ ] **Step 2:** Implement: `Alter.Table("MovieFiles")` adds `OsHash` (string, nullable, indexed), `PHash` (string, nullable), `PHashAttemptedAt` (datetime, nullable), `HashedFileModifiedAt` (datetime, nullable), `MatchTier` (int, not null, default 0). `ImportFiles` gets the same minus `MatchTier`, no index. `HashedFileModifiedAt` is my addition to the spec's columns: the file's last-write time when it was hashed, so a modified-time change can be detected (the spec requires that check and nothing else stores mtime).
- [ ] **Step 3:** Add the model properties, the enum and the V5 resource fields (map both ways in `ToResource`; V5 resource is read-only for these, no `ToModel` change needed if none exists).
- [ ] **Step 4:** Run the migration fixture and the full `Whisparr.Core.Test` `Datastore` namespace. Expected: PASS. Run the same fixture against Postgres if the Postgres runsettings work locally (`src/postgres.runsettings`); report if skipped.
- [ ] **Step 5: Commit** — `New: File fingerprint columns (migration 302)`.

### Task 3: VideoHashService

**Files:**
- Create: `src/NzbDrone.Core/MediaFiles/Fingerprints/VideoHashService.cs`
- Modify: `src/NzbDrone.Core/MediaFiles/MediaInfo/VideoFileInfoReader.cs` (add `GetContainerDuration`)
- Test: `src/NzbDrone.Core.Test/MediaFiles/Fingerprints/VideoHashServiceFixture.cs`

**Interfaces:**
- Consumes: `IFingerprintCalculator` (Task 1).
- Produces:
  ```csharp
  public interface IVideoHashService
  {
      string ComputeOsHash(string path);   // null on any failure
      string ComputePHash(string path);    // null on any failure; caller sets PHashAttemptedAt either way
      bool CanComputePHash { get; }        // FFmpegAvailable
  }
  // IVideoFileInfoReader gains:
  double? GetContainerDuration(string filename); // format.duration seconds rounded to 0.01; null on failure
  ```

- [ ] **Step 1: Failing tests** (mock `IFingerprintCalculator`, `IVideoFileInfoReader`, `IDiskProvider`):
  - `ComputeOsHash_returns_calculator_value`.
  - `ComputeOsHash_returns_null_when_calculator_throws` — for `IOException`, `UnauthorizedAccessException`, `FileNotFoundException`, `ArgumentException` (`[TestCase]` per type); no exception escapes.
  - `ComputePHash_passes_rounded_container_duration` — reader returns `1234.5678` → calculator called with `1234.57`.
  - `ComputePHash_returns_null_without_calling_calculator_when_duration_is_null_or_not_positive`.
  - `ComputePHash_returns_null_when_calculator_throws_or_times_out` — calculator throws `FFmpegException`; and a calculator that never completes is cancelled by the per-file timeout (inject a short timeout through an internal constructor overload or a settable property for the test).
  - `CanComputePHash_reflects_calculator`.
- [ ] **Step 2:** Implement. Per-file phash timeout **10 minutes** via `CancellationTokenSource`; run the async call synchronously (`GetAwaiter().GetResult()`), since callers are command executors. Log failures at Warn with the path and exception message (not the stack); log a cancelled phash as `"phash timed out for {path}"`. `GetContainerDuration` uses `FFProbe.Analyse(filename)` and `analysis.Format.Duration`, catching every exception to return null.
- [ ] **Step 3:** Run the fixture. Expected: PASS.
- [ ] **Step 4: Commit** — `New: VideoHashService computes oshash and phash without ever failing the caller`.

### Task 4: oshash inline — import, root-folder refresh, scene import folder, rescan

**Files:**
- Modify: `src/NzbDrone.Core/MediaFiles/MovieImport/ImportApprovedMovie.cs:87-88`
- Modify: `src/NzbDrone.Core/MediaFiles/MovieImport/ImportDecisionMaker.cs:166-173` (unmapped files in `GetSceneImportDecisions`)
- Modify: `src/NzbDrone.Core/RootFolders/RootFolderService.cs:290-304` (`Refresh`: new `ImportFile`s only)
- Modify: `src/NzbDrone.Core/MediaFiles/DiskScanService.cs:164-190` (existing-file loop)
- Test: the existing fixtures for each class (`ImportApprovedMovieFixture`, `ImportDecisionMakerFixture`, `RootFolderServiceFixture`, `DiskScanServiceFixture`), new test cases only.

**Interfaces:**
- Consumes: `IVideoHashService.ComputeOsHash` (Task 3); `IDiskProvider.FileGetLastWrite(path)`.
- Produces: rule used everywhere a hash is written — set `OsHash` and `HashedFileModifiedAt = FileGetLastWrite(path)` together.

- [ ] **Step 1: Failing tests:**
  - Import: an approved decision produces a `MovieFile` whose `OsHash` is the service's value and `HashedFileModifiedAt` the disk provider's value, hashed from `localMovie.Path` (the source, before the move). When the service returns null, the import still succeeds and `OsHash` is null.
  - Scene import folder: an unmapped file gets `OsHash`/`HashedFileModifiedAt`.
  - Root-folder refresh: only files in `toAdd` are hashed (an already-known `ImportFile` triggers no `ComputeOsHash` call).
  - Rescan, existing file:
    - size changed → `OsHash`, `PHash`, `PHashAttemptedAt` cleared, then oshash recomputed and saved;
    - same size, last-write differs from `HashedFileModifiedAt` → same clearing and recompute;
    - same size and last-write, `OsHash` null → oshash computed (retry), `PHash`/`PHashAttemptedAt` untouched;
    - same size and last-write, `OsHash` set → no hashing call;
    - `HashedFileModifiedAt` null with `OsHash` set (shouldn't occur, but legacy-safe) → treat as unchanged and record the current last-write.
  - Every changed file is persisted (whether or not media info updated it).
- [ ] **Step 2:** Implement. In `DiskScanService`, do the hash bookkeeping before the existing `if (!manual && file.Size == fileSize) continue;`, collecting hash-changed files into the existing update list so `_mediaFileService.Update` persists them; a file whose media info `Update` succeeds is already persisted with the hash fields set (it saves the same object).
- [ ] **Step 3:** Run the four fixtures plus `FullyQualifiedName~MediaFiles`. Expected: PASS.
- [ ] **Step 4: Commit** — `New: Compute oshash on import, library import and rescan`.

### Task 5: oshash backfill task

**Files:**
- Create: `src/NzbDrone.Core/MediaFiles/Fingerprints/Commands/GenerateOsHashesCommand.cs`
- Create: `src/NzbDrone.Core/MediaFiles/Fingerprints/FileHashBackfillService.cs`
- Modify: `src/NzbDrone.Core/MediaFiles/MediaFileRepository.cs`, `MediaFileService.cs`, `src/NzbDrone.Core/RootFolders/ImportFileRepository.cs`
- Modify: `src/NzbDrone.Core/Jobs/TaskManager.cs` (default task)
- Test: `src/NzbDrone.Core.Test/MediaFiles/Fingerprints/FileHashBackfillServiceFixture.cs`; repository query tests in the existing `MediaFileRepositoryFixture` (DbTest).

**Interfaces:**
- Consumes: `IVideoHashService` (Task 3).
- Produces:
  - `IMediaFileRepository.GetFilesWithoutOsHash(int limit)` → `List<MovieFile>`, includes unmapped (`MovieId == 0`); `IImportFileRepository.GetFilesWithoutOsHash(int limit)`.
  - `GenerateOsHashesCommand : Command` (`SendUpdatesToClient => true`), handled by `FileHashBackfillService : IExecute<GenerateOsHashesCommand>`.
  - Scheduled every **30** minutes, priority Low (the `ScheduledTask` default).
  - Path resolution (shared with Task 6, keep it a private helper in the service): mapped `MovieFile` → `Path.Combine(movie.Path, RelativePath)` (load movies by id in one call); unmapped → `OriginalFilePath`; `ImportFile` → `Path`.

- [ ] **Step 1: Failing tests:**
  - `GetFilesWithoutOsHash` returns only rows with null `OsHash`, at most `limit`, including `MovieId == 0` rows (DbTest).
  - One run hashes at most **2000** files total (MovieFiles first, then ImportFiles with what's left) and stops; a second run picks up the rest.
  - A file that no longer exists on disk is skipped without hashing and without an exception.
  - A null result leaves the row's `OsHash` null (retried next run) but the run continues; successful rows set `OsHash` + `HashedFileModifiedAt` and are saved in one batch update per table.
  - Progress is reported with `_logger.ProgressInfo` ("Hashing file {n}/{total}").
- [ ] **Step 2:** Implement; the limit is applied in SQL (no loading the whole table to `Take`). Register the scheduled task in `TaskManager.Handle(ApplicationStartedEvent)`.
- [ ] **Step 3:** Run the fixtures. Expected: PASS.
- [ ] **Step 4: Commit** — `New: Backfill oshash for existing files as a system task`.

### Task 6: phash setting and generation task

**Files:**
- Modify: `src/NzbDrone.Core/Configuration/IConfigService.cs`, `ConfigService.cs` (`EnablePerceptualHashing`)
- Modify: `src/Whisparr.Api.V5/Config/MediaManagementConfigResource.cs`
- Modify: `frontend/src/typings/Settings/MediaManagement.ts`, `frontend/src/Settings/MediaManagement/MediaManagement.tsx` (after the `enableMediaInfo` group, same advanced-setting shape)
- Modify: `src/NzbDrone.Core/Localization/Core/en.json`
- Create: `src/NzbDrone.Core/MediaFiles/Fingerprints/Commands/GenerateVideoHashesCommand.cs`
- Modify: `src/NzbDrone.Core/MediaFiles/Fingerprints/FileHashBackfillService.cs` (`IExecute<GenerateVideoHashesCommand>`), the repositories, `TaskManager.cs`
- Test: `FileHashBackfillServiceFixture` (new cases), repository DbTests.

**Interfaces:**
- Consumes: `IVideoHashService.ComputePHash`, `CanComputePHash` (Task 3); path helper (Task 5).
- Produces:
  - `bool EnablePerceptualHashing` — config key `EnablePerceptualHashing`, default `true`; V5 resource field `enablePerceptualHashing`.
  - `GetFilesNeedingPHash(int limit)` on both repositories: `PHash == null && PHashAttemptedAt == null`.
  - `GenerateVideoHashesCommand`, scheduled every **60** minutes, Low.
  - en.json: `PerceptualHashing` = "Perceptual Hashing", `PerceptualHashingHelpText` = "Compute a perceptual hash of each video file in the background, once per file, to help identify scenes whose names don't parse".

- [ ] **Step 1: Failing tests:**
  - Setting off → the command returns without touching any repository.
  - `CanComputePHash` false → returns without touching any repository (no file is marked attempted).
  - A run processes files until **15 minutes** of wall time have passed (inject a clock; check before each file) or no work is left.
  - Each processed file gets `PHashAttemptedAt = now` whether `ComputePHash` returned a hash or null, and `PHash` when non-null; saved per file (a long run mustn't lose its work on shutdown).
  - A file missing on disk is marked attempted and skipped.
  - DbTest: `GetFilesNeedingPHash` excludes rows with `PHash` set or `PHashAttemptedAt` set.
- [ ] **Step 2:** Implement backend, then the UI toggle (`type={inputTypes.CHECK}`, `name="enablePerceptualHashing"`, label/help from the new keys, `advancedSettings`/`isAdvanced` like `enableMediaInfo`).
- [ ] **Step 3:** Run backend fixtures; from the repo root run `yarn typecheck` (expect zero errors), `yarn lint`, `yarn build`.
- [ ] **Step 4: Commit** — `New: Background perceptual hashing, once per file, behind a Media Management setting`.

### Task 7: ffmpeg health check

**Files:**
- Create: `src/NzbDrone.Core/HealthCheck/Checks/FFmpegCheck.cs`
- Modify: `src/NzbDrone.Core/HealthCheck/HealthCheck.cs` (`HealthCheckReason.FFmpegUnavailable`)
- Modify: `en.json` (`FFmpegUnavailableHealthCheckMessage` = "The bundled ffmpeg needed for perceptual hashing was not found or can't run. Perceptual hashing is paused; oshash and imports are unaffected.")
- Test: `src/NzbDrone.Core.Test/HealthCheck/Checks/FFmpegCheckFixture.cs`

**Interfaces:**
- Consumes: `IConfigService.EnablePerceptualHashing` (Task 6), `IVideoHashService.CanComputePHash` (Task 3).

- [ ] **Step 1: Failing tests:** setting off → ok regardless of ffmpeg; setting on + available → ok; setting on + unavailable → `HealthCheckResult.Warning` with `HealthCheckReason.FFmpegUnavailable` and the message key. Re-checked on `ConfigSavedEvent` (`[CheckOn(typeof(ConfigSavedEvent))]`).
- [ ] **Step 2:** Implement following `RecyclingBinCheck`'s shape; wiki fragment `"#ffmpeg-unavailable"`.
- [ ] **Step 3:** Run the fixture. Expected: PASS. Then run **every** test project (Core, Common, Api, Host, Mono) with `--filter "TestCategory!=IntegrationTest&TestCategory!=AutomationTest&TestCategory!=ManualTest"`; report counts.
- [ ] **Step 4: Commit** — `New: Health check for the bundled ffmpeg when perceptual hashing is on`.

### Task 8: Live check on the 6969 instance (controller)

- [ ] **Step 1:** Build the osx-arm64 self-contained publish and run it on 6969 (dev data is expendable). Confirm migration 302 ran.
- [ ] **Step 2:** Import or rescan a scene; `GET /api/v5/moviefile?movieId=<id>` shows a 16-hex `osHash`.
- [ ] **Step 3:** System → Tasks lists "Generate Os Hashes" and "Generate Video Hashes"; running both fills `osHash` for older files and `pHash` for at least one file; a second phash run doesn't re-hash it.
- [ ] **Step 4:** Toggle the setting off and on; the health check stays clear (ffmpeg is bundled). The warning path is covered by Task 7's unit tests.
- [ ] **Step 5:** Report; don't push until plz12345 confirms.
