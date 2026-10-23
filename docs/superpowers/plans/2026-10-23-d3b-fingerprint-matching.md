# D3b: Fingerprint matching in whisparr-next — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every import checks the parser's answer against the file's hashes, links only on the spec's match tiers, sends weak or conflicting evidence to review with the candidates shown, and re-links files renamed outside Whisparr.

**Architecture:** A `FingerprintLookupClient` talks to Skyhook `fingerprint/lookup` (batched, never throws). A pure `FileMatchEvaluator` turns parser result + lookup result + file duration into a tier, a target and review candidates. Import paths call it: downloads and rescans verify parser matches local-only; manual import, the scene import folder and the library import page look parser failures up upstream. A later phash only promotes or flags. D3a's hash columns and tasks are the input.

**Tech Stack:** C# .NET 10, FluentMigrator, Dapper, NUnit/Moq/FluentAssertions, React/TS.

**Spec:** `docs/superpowers/specs/2026-10-22-fingerprints-design.md` (Part 3 from "Name and hash check each other" to "Retry"). D3a (`docs/superpowers/plans/2026-10-22-d3a-file-hashing.md`) is built.

## Global Constraints

- Repo `~/Git/whisparr-next`, branch `develop`. Commit there; do not push.
- Skyhook endpoint `POST {skyhook}/fingerprint/lookup` (no source route prefix; source goes in the body). Request `{ "source": "stashDb"|"tpdb", "localOnly": bool, "files": [{ "key": string, "oshash": string?, "phash": string?, "duration": number? }] }`, at most **100** files. Response `{ "files": [{ "key", "status": "matched"|"notFound"|"tryLater", "candidates": [{ "sceneId", "algorithm": "OSHASH"|"PHASH", "distance", "submissions", "duration" }] }] }`. `source` is the locked scene source (`IConfigService.WhisparrSceneMetadataSource`: `StashDb` → `"stashDb"`, `Tpdb` → `"tpdb"`). phash is sent unpadded lowercase as stored; duration in seconds rounded to 0.01.
- Batch sizes: local-only **100**; upstream **100** for stashDb, **20** for tpdb (Skyhook asks TPDB one hash at a time). Request timeout **180 s**.
- Files the parser matched are looked up **local-only**. Only parser failures may go upstream.
- Match tiers (a "candidate" is an in-library scene; phash candidates beyond distance **8** are dropped):

  | Tier | Evidence | Action |
  |---|---|---|
  | Confirmed | parser and hash point to the same scene | link |
  | Hash (oshash) | no parser match; exactly one oshash candidate; file duration within **5 s** of the candidate's duration | link automatically |
  | Hash only (phash) | no parser match; phash candidate(s) | review |
  | Name | parser match; no hash data for the file | link, as today |
  | Conflict | parser says scene A, hash says scene B | review, both shown |

  Several oshash candidates, a duration mismatch, or a candidate not in the library also go to review.
- Hits never add scenes. A candidate not in the library is only shown (title, studio, date).
- A later phash only promotes a file to Confirmed or flags it as Conflict; it never relinks.
- A failed or "tryLater" lookup never rejects, aborts or delays an import: it is treated as "no hash data" for a parser match and "unmatched" for a parser failure; the next scheduled pass retries. No tight retry loop.
- Re-link only when the old path is missing on disk; nothing on disk is touched.
- Api.V3 is frozen: new fields on Api.V5 resources only. New strings only in `src/NzbDrone.Core/Localization/Core/en.json` (alphabetical).
- File edits with Edit/Write only — no sed, awk, heredocs or scripts. One plain Bash command per call, absolute paths, no `cd &&`, no `..`. No AI attribution. Commit prefixes `New:` / `Fix:` / `Chore:`. Implementers never dispatch subagents.

## Review Focus

1. **Skyhook unreachable, slow or returning 5xx during a big import** — every file imports on its parser result (tier Name), nothing is rejected, no exception escapes. (Tasks 1, 3.)
2. **A file whose oshash is shared by two scenes** (duplicate uploads on StashDB) — never auto-linked; goes to review with both shown. (Task 2.)
3. **The downloaded file is a different scene from the grab** (wrong file in a release, or the `~/Sites` PansexualX folder holding the Shady Spa file) — Conflict: not linked, manual import shows both. (Tasks 3, 11.)
4. **TPDB lookups for 20 unknown files take ~40 s** — the HTTP client waits (180 s), the command thread isn't blocked beyond that, nothing retries in a loop. (Task 1.)
5. **A renamed file plus an unrelated new file in the same folder** — only the file whose oshash equals the missing row's is re-linked; the other imports normally. (Task 8.)

---

### Task 1: Fingerprint lookup client

**Files:**
- Create: `src/NzbDrone.Core/MetadataSource/SkyHook/Fingerprints/FingerprintLookupClient.cs`
- Create: `src/NzbDrone.Core/MetadataSource/SkyHook/Fingerprints/FingerprintLookupResources.cs` (request/response DTOs)
- Test: `src/NzbDrone.Core.Test/MetadataSource/SkyHook/Fingerprints/FingerprintLookupClientFixture.cs`

**Interfaces:**
- Produces:
  ```csharp
  public class FileFingerprint { public string Key; public string OsHash; public string PHash; public double? Duration; }
  public enum FingerprintLookupStatus { Matched, NotFound, TryLater }
  public class FingerprintCandidateResult { public string SceneId; public string Algorithm; public int Distance; public int Submissions; public int? Duration; }
  public class FingerprintLookupResult { public string Key; public FingerprintLookupStatus Status; public List<FingerprintCandidateResult> Candidates; }
  public interface IFingerprintLookupClient
  {
      // one entry per input key, never throws; a failed batch yields TryLater for its keys
      Dictionary<string, FingerprintLookupResult> Lookup(IReadOnlyCollection<FileFingerprint> files, bool localOnly);
  }
  ```
  Use properties, not fields, following the codebase. Namespace `NzbDrone.Core.MetadataSource.SkyHook.Fingerprints`.

- [ ] **Step 1: Failing tests** (mock `IHttpClient`; build requests the way `SkyHookProxy` does with `IWhisparrCloudRequestBuilder` / `_whisparrMetadata.Create().SetSegment("route", "fingerprint/lookup")`):
  - posts the documented JSON (camelCase, source from config, `localOnly`, keys) and maps the response;
  - files with neither hash are not sent and come back `NotFound`;
  - 250 local-only files → 3 requests (100/100/50); 45 upstream files with tpdb → 3 requests (20/20/5); with stashDb → 1 request;
  - HTTP exception, timeout, non-2xx or malformed JSON for one batch → that batch's keys `TryLater`, other batches still mapped, no exception;
  - a key missing from the response → `TryLater`;
  - request timeout is 180 s.
- [ ] **Step 2:** Implement. Log batch failures at Warn once per batch (message, not stack).
- [ ] **Step 3:** Run the fixture. Expected: PASS.
- [ ] **Step 4: Commit** — `New: Skyhook fingerprint lookup client`.

### Task 2: Match evaluator and candidates

**Files:**
- Create: `src/NzbDrone.Core/MediaFiles/Fingerprints/FileMatchEvaluator.cs`
- Create: `src/NzbDrone.Core/MediaFiles/Fingerprints/FingerprintCandidate.cs`
- Modify: `src/NzbDrone.Core/MediaFiles/MatchTier.cs` (add `Conflict = 5`)
- Test: `src/NzbDrone.Core.Test/MediaFiles/Fingerprints/FileMatchEvaluatorFixture.cs`

**Interfaces:**
- Consumes: `FingerprintLookupResult` (Task 1); `IMovieService.FindByForeignId(string)`; `IProvideMovieInfo.GetSceneInfo(string)` for display info of non-library candidates.
- Produces:
  ```csharp
  public class FingerprintCandidate        // stored as JSON on ImportFiles (Task 6) and returned on manual import items
  {
      public string SceneId { get; set; }
      public string Algorithm { get; set; }   // "OSHASH" | "PHASH"
      public int Distance { get; set; }
      public int Submissions { get; set; }
      public int? Duration { get; set; }
      public int? MovieId { get; set; }        // set when the scene is in the library
      public string Title { get; set; }
      public string StudioTitle { get; set; }
      public string ReleaseDate { get; set; }
  }
  public class FileMatchOutcome
  {
      public MatchTier Tier { get; set; }      // Confirmed | Hash | Name | Unknown (review)
      public Movie Target { get; set; }        // movie to link; null when review is needed
      public bool NeedsReview { get; set; }
      public bool Conflict { get; set; }       // parser and hash disagree
      public List<FingerprintCandidate> Candidates { get; set; }
  }
  public interface IFileMatchEvaluator
  {
      FileMatchOutcome Evaluate(Movie parserMatch, FingerprintLookupResult lookup, double? fileDurationSeconds);
      List<FingerprintCandidate> BuildCandidates(FingerprintLookupResult lookup);   // de-duplicated per scene, best first, enriched
  }
  ```

- [ ] **Step 1: Failing tests** — one `[Test]` per row of the tier table plus the review cases, with exact expectations:
  - parser A + oshash candidate A → Confirmed, Target A; parser A + phash candidate A (distance 3) → Confirmed;
  - parser A + lookup NotFound/TryLater/null → Name, Target A;
  - parser A + candidate B (in library or not) → Conflict, NeedsReview, Target null, candidates contain B;
  - parser A + candidates A and B → Confirmed (the parser's scene is among them) — my call: hash agreement with the parser wins over an extra candidate;
  - no parser + one in-library oshash candidate, file 944.4 s vs candidate 944 → Hash, Target that movie; same with candidate 950 → review (duration > 5 s); file duration null → review;
  - no parser + two oshash candidates → review; one oshash candidate not in library → review with display info;
  - no parser + phash-only candidate → review; phash distance 9 → dropped (NotFound behaviour);
  - candidates for the same scene by both algorithms collapse to one entry (oshash preferred), ordered oshash-before-phash then by distance asc, submissions desc;
  - `GetSceneInfo` is called for at most **5** non-library candidates, and a throw leaves Title/Studio/Date null without failing.
- [ ] **Step 2:** Implement. Pure apart from the movie/metadata lookups; no I/O beyond those.
- [ ] **Step 3:** Run the fixture. Expected: PASS.
- [ ] **Step 4: Commit** — `New: Fingerprint match tiers`.

### Task 3: Verify parser matches on download and rescan imports

**Files:**
- Modify: `src/NzbDrone.Core/MediaFiles/MovieImport/ImportDecisionMaker.cs` (`GetImportDecisions(…, Movie movie, …)` path)
- Modify: `src/NzbDrone.Core/Parser/Model/LocalMovie.cs` (`string OsHash`, `DateTime? HashedFileModifiedAt`, `MatchTier MatchTier`, `List<FingerprintCandidate> FingerprintCandidates`)
- Modify: `src/NzbDrone.Core/MediaFiles/MovieImport/ImportApprovedMovie.cs` (reuse `LocalMovie.OsHash` when set; persist `MatchTier`)
- Modify: `src/NzbDrone.Core/MediaFiles/MovieImport/ImportRejectionReason.cs` (`FingerprintConflict`)
- Modify: `en.json` (`FingerprintConflictRejection` = "Fingerprint matches a different scene: {title}")
- Test: `ImportDecisionMakerFixture`, `ImportApprovedMoviesFixture` (new cases)

**Interfaces:**
- Consumes: Tasks 1-2; D3a `VideoHashExtensions` oshash+stamp helper; `IConfigService.WhisparrSceneMetadataSource`.
- Produces: `LocalMovie.MatchTier` and `LocalMovie.OsHash` populated by the decision maker for scene items; `MovieFile.MatchTier` persisted on import.

- [ ] **Step 1: Failing tests:**
  - for a batch of scene files with a known movie, oshash is computed once per file and all files go to the client in **one** local-only call;
  - Confirmed → decision accepted, `MatchTier.Confirmed`; NotFound/TryLater/client returns nothing → accepted, `MatchTier.Name`;
  - Conflict → decision rejected with `ImportRejectionReason.FingerprintConflict` and the localized message naming the other scene's title (or its scene id when the title is unknown); `FingerprintCandidates` carried on the LocalMovie;
  - movies (`ItemType.Movie`) skip fingerprinting entirely;
  - `ImportApprovedMovie` doesn't hash again when `LocalMovie.OsHash` is set, and writes `MatchTier` to the `MovieFile`.
- [ ] **Step 2:** Implement. The lookup runs once per `GetImportDecisions` call, before per-file decisions.
- [ ] **Step 3:** Run both fixtures plus `FullyQualifiedName~MovieImport`. Expected: PASS.
- [ ] **Step 4: Commit** — `New: Verify scene imports against their fingerprints`.

### Task 4: Manual import shows fingerprint candidates

**Files:**
- Modify: `src/NzbDrone.Core/MediaFiles/MovieImport/Manual/ManualImportService.cs` (`ProcessFile` unknown-movie branch; `MapItem`)
- Modify: `src/NzbDrone.Core/MediaFiles/MovieImport/Manual/ManualImportItem.cs` (`List<FingerprintCandidate> FingerprintCandidates`)
- Modify: `src/Whisparr.Api.V5/ManualImport/ManualImportResource.cs` (+ mapper)
- Modify: the manual-import execute path so files imported through it get `MatchTier.Manual`
- Test: `ManualImportServiceFixture` (create if absent)

**Interfaces:**
- Consumes: Tasks 1-3.
- Produces: `ManualImportResource.FingerprintCandidates` (array of the `FingerprintCandidate` shape, camelCase).

- [ ] **Step 1: Failing tests:**
  - unknown-movie file → oshash computed, one **upstream** lookup for all unknown files of the request (batched by the client), candidates on the item; the item keeps the `UnknownMovie` rejection (review, not auto-link — the user picks);
  - a Conflict decision from Task 3 surfaces its candidates on the item alongside the rejection;
  - lookup failure → item without candidates, no exception;
  - importing through `ManualImportCommand` stores `MatchTier.Manual`.
- [ ] **Step 2:** Implement.
- [ ] **Step 3:** Run the fixture plus `FullyQualifiedName~Manual`. Expected: PASS.
- [ ] **Step 4: Commit** — `New: Manual import lists fingerprint candidates`.

### Task 5: Scene import folder links Hash-tier files

**Files:**
- Modify: `src/NzbDrone.Core/MediaFiles/MovieImport/ImportDecisionMaker.cs` (`GetSceneImportDecisions`, the unmapped branch)
- Test: `ImportDecisionMakerFixture` (new cases)

**Interfaces:**
- Consumes: Tasks 1-3.

- [ ] **Step 1: Failing tests:**
  - files the parser/identification couldn't match are hashed and looked up **upstream** in one batched call;
  - Hash tier (one in-library oshash candidate within 5 s) → a normal decision for that movie with `MatchTier.Hash`, not an unmapped file;
  - anything else → unmapped file as today; no scene is ever added from a hash hit;
  - lookup failure → unmapped as today.
- [ ] **Step 2:** Implement. Leave the identification path (which may add and move scenes) unchanged — my call: fingerprint-checking it would mean reworking `SceneIdentificationService`'s move logic; deferred.
- [ ] **Step 3:** Run the fixture. Expected: PASS.
- [ ] **Step 4: Commit** — `New: Scene import folder links files by oshash when the name doesn't parse`.

### Task 6: Library import candidates (ImportFiles)

**Files:**
- Create: `src/NzbDrone.Core/Datastore/Migration/303_add_import_file_candidates.cs`
- Modify: `src/NzbDrone.Core/RootFolders/ImportFile.cs` (`List<FingerprintCandidate> Candidates`, `DateTime? CandidatesCheckedAt`), `ImportFileRepository.cs`, `ImportlFileService.cs`
- Create: `src/NzbDrone.Core/RootFolders/ImportFileMatchService.cs`
- Modify: `src/NzbDrone.Core/MediaFiles/Fingerprints/FileHashBackfillService.cs` (call the match service at the end of the oshash run; when phash is newly set on an ImportFile with no candidates, clear `CandidatesCheckedAt`)
- Test: migration fixture, `ImportFileMatchServiceFixture`, repository DbTests

**Interfaces:**
- Consumes: Tasks 1-2.
- Produces: `ImportFile.Candidates` (JSON column, embedded document) and `CandidatesCheckedAt`; serialized on the root-folder resource automatically (the V5 resource returns `ImportFile` models).

- [ ] **Step 1: Failing tests:**
  - migration 303 adds `ImportFiles.Candidates` (text, nullable) and `CandidatesCheckedAt` (datetime, nullable);
  - `MatchPending()` takes ImportFiles with `OsHash` set and `CandidatesCheckedAt` null, at most **500** per run, looks them up upstream (client batches), stores candidates (empty list for NotFound) and stamps `CandidatesCheckedAt`; `TryLater` rows stay unstamped; saves are field-restricted (`Candidates`, `CandidatesCheckedAt`) without events;
  - an ImportFile whose phash was just computed and whose candidate list is empty gets `CandidatesCheckedAt` cleared so the next run asks with the phash.
- [ ] **Step 2:** Implement.
- [ ] **Step 3:** Run the fixtures. Expected: PASS.
- [ ] **Step 4: Commit** — `New: Fingerprint candidates for library import files`.

### Task 7: A later phash promotes or flags linked files

**Files:**
- Modify: `src/NzbDrone.Core/MediaFiles/Fingerprints/FileHashBackfillService.cs` (after a phash run)
- Create: `src/NzbDrone.Core/MediaFiles/Fingerprints/LinkedFileVerifier.cs`
- Test: `LinkedFileVerifierFixture`

**Interfaces:**
- Consumes: Tasks 1-2; `MovieFile.MatchTier` (`Conflict = 5` from Task 2).

- [ ] **Step 1: Failing tests:**
  - linked MovieFiles (`MovieId > 0`) that got a phash in this run are looked up **local-only** in batches of 100 (phash only);
  - candidate equals the file's movie → `MatchTier` becomes Confirmed (from Name or Unknown; Hash and Manual stay);
  - a candidate for a different scene → `MatchTier.Conflict`; the file is not moved, relinked or deleted;
  - no candidates / TryLater → unchanged;
  - saves are field-restricted to `MatchTier`.
- [ ] **Step 2:** Implement.
- [ ] **Step 3:** Run the fixture. Expected: PASS.
- [ ] **Step 4: Commit** — `New: Perceptual hash confirms or flags linked files`.

### Task 8: Re-link renamed files on rescan

**Files:**
- Modify: `src/NzbDrone.Core/MediaFiles/DiskScanService.cs` (`Scan(Movie, bool)`, before `CleanMediaFiles`)
- Test: `ScanFixture` (new cases)

**Interfaces:**
- Consumes: D3a oshash helper.

- [ ] **Step 1: Failing tests:**
  - a MovieFile whose path is missing, with `OsHash` X, and one new unknown file in the movie folder with oshash X → the row's `RelativePath` is re-pointed, `HashedFileModifiedAt` re-stamped, every other field (`PHash`, `MatchTier`, quality, etc.) kept; the file is not imported as new and the row is not deleted;
  - a second new file with a different oshash imports normally;
  - no missing rows → no hashing done here (new files are hashed by the import later, as today);
  - two missing rows with the same oshash → no re-link (ambiguous), today's behaviour;
  - a missing row with null `OsHash` → today's behaviour.
- [ ] **Step 2:** Implement. Scope: the movie's own folder (cross-folder moves belong to workstream F).
- [ ] **Step 3:** Run `ScanFixture` plus `FullyQualifiedName~MediaFiles`. Expected: PASS.
- [ ] **Step 4: Commit** — `New: Re-link files renamed outside Whisparr by oshash`.

### Task 9: Library import page preselects hash matches

**Files:**
- Modify: `frontend/src/typings/ImportFile.ts` (`candidates?: FingerprintCandidate[]`)
- Create: `frontend/src/typings/FingerprintCandidate.ts`
- Modify: `frontend/src/AddMovie/ImportMovie/Import/ImportMovie.tsx` (initial lookup), `ImportMovieTypes.ts`, `ImportMovieRow.tsx` (badge)
- Modify: `en.json` (`HashMatch` = "Hash match", `HashMatchHelpText` = "Suggested from the file's fingerprint")

**Interfaces:**
- Consumes: Task 6's `importFiles[].candidates`.

- [ ] **Step 1:** When an item's file has a top candidate with `algorithm === 'OSHASH'` and it's the only oshash candidate, queue its initial lookup with `term` = that candidate's `sceneId` instead of the file-name term, and mark the item `hashMatch: true`. Other items behave as today.
- [ ] **Step 2:** `ImportMovieRow` shows a small "Hash match" label (with the help text as its title) next to the selected scene while it is the hash-suggested one; it disappears once the user picks something else.
- [ ] **Step 3:** From the repo root: `yarn typecheck` (zero errors), `yarn lint`, `yarn build`. Mark props `Readonly<>` on files you touch.
- [ ] **Step 4: Commit** — `New: Library import preselects scenes matched by fingerprint`.

### Task 10: Manual import candidates and file match tier in the UI

**Files:**
- Modify: `frontend/src/InteractiveImport/Interactive/InteractiveImportRow.tsx` (+ the item typing under `frontend/src/InteractiveImport/`)
- Modify: `frontend/src/MovieFile/MovieFile.ts`, `frontend/src/MovieFile/FileDetailsModal.tsx`
- Modify: `en.json` (`FingerprintCandidates` = "Fingerprint candidates", `MatchTier` = "Matched by", `MatchTierConfirmed` = "Name and fingerprint", `MatchTierHash` = "Fingerprint", `MatchTierName` = "Name", `MatchTierManual` = "Manual", `MatchTierConflict` = "Fingerprint conflict", `MatchTierUnknown` = "Unknown", `NotInLibrary` = "Not in library", `UseScene` = "Use")

**Interfaces:**
- Consumes: Task 4's `fingerprintCandidates` on manual import items; V5 `MovieFile.matchTier` (D3a, JSON shape as the API serializes it — check the actual response before typing it).

- [ ] **Step 1:** Interactive import row: when `fingerprintCandidates` is non-empty, show an icon in the scene cell; its popover lists each candidate (title, studio, date, algorithm, distance, submissions). An in-library candidate has a "Use" button that selects that scene for the row (same path as picking it in the scene selector). A not-in-library candidate shows "Not in library" — no add action in this task.
- [ ] **Step 2:** File details modal: a "Matched by" row with the tier label; Conflict renders as a warning.
- [ ] **Step 3:** `yarn typecheck`, `yarn lint`, `yarn build`. `Readonly<>` props on touched files.
- [ ] **Step 4: Commit** — `New: Show fingerprint candidates in manual import and the match tier in file details`.

### Task 11: Live check (controller)

- [ ] **Step 1:** Run every test project (Core, Common, Api, Host, Mono) with the usual category filter; report counts.
- [ ] **Step 2:** Build and run on the v5 dev data (one scratchpad script, as in D3a). With a scene in the library for Shady Spa `019cf217-1e22-73bf-acd3-57088c9b6e18` and for PansexualX `019bfe57-b0cd-764d-b261-dc1e63dd099f`:
  - manual import of the `~/Sites/scenes/Evil Angel/PansexualX/...` file → rejected as a fingerprint conflict naming the Shady Spa scene, candidates listed;
  - manual import of the Shady Spa file → accepted, Confirmed after import;
  - a root-folder import folder holding a symlink to the Shady Spa file renamed to junk → the import file gets candidates and the library import page preselects Shady Spa with the "Hash match" label (screenshot with SFW mode on).
- [ ] **Step 3:** Report; don't push until plz12345 confirms.
